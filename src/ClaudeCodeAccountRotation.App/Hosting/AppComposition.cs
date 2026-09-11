using System.Reflection;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Adapters.Http;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.App.Configuration;
using ClaudeCodeAccountRotation.App.Dashboard;
using ClaudeCodeAccountRotation.App.Endpoints;
using ClaudeCodeAccountRotation.App.Security;
using ClaudeCodeAccountRotation.App.Switching;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Configuration;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Hosting;

/// <summary>
/// The composition root: configuration, validation, the single-instance lock,
/// every service with its explicit type, Kestrel on loopback, and the routes.
/// </summary>
internal static class AppComposition
{
    private const string ConfigPathSettingKey = "ClaudeCodeAccountRotation:ConfigPath";
    private const string ConfigFileName = "config.json";
    private static readonly TimeSpan _cliTimeout = TimeSpan.FromSeconds(30);

    public static string Version =>
        typeof(AppComposition).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppComposition).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static async Task<Result<Unit, string>> ComposeAsync(WebApplicationBuilder builder, StartupArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(arguments);

        ClaudeCodeAccountRotationConfiguration defaults = ConfigurationDefaults.ForCurrentUser();
        string configPath = arguments.ConfigPath
            ?? builder.Configuration[ConfigPathSettingKey]
            ?? Path.Combine(defaults.AppDataDirectory, ConfigFileName);

        Result<ClaudeCodeAccountRotationConfiguration, string> loaded = await ConfigurationFile.LoadOrCreateAsync(configPath, defaults, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result<Unit, string>.Failure(loaded.Error);
        }

        ClaudeCodeAccountRotationConfiguration configuration = arguments.Port is int port ? loaded.Value with { ListenPort = port } : loaded.Value;
        Result<Unit, string> verdict = ConfigurationValidator.Validate(
            configuration,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ConfigurationValidator.VolumeOf,
            Environment.GetEnvironmentVariable);
        if (verdict.IsFailure)
        {
            return Result<Unit, string>.Failure("configuration refused (" + configPath + "): " + verdict.Error);
        }

        string listenUrl = "http://127.0.0.1:" + configuration.ListenPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Result<InstanceLock, string> instance = InstanceLock.TryAcquire(configuration.AppDataDirectory, listenUrl);
        if (instance.IsFailure)
        {
            return Result<Unit, string>.Failure(instance.Error);
        }

        IServiceCollection services = builder.Services;
        // Factory-registered so the container owns it and releases the lock file at shutdown.
        InstanceLock acquired = instance.Value;
        services.AddSingleton(_ => acquired);
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new SwitchOptions(
            configuration.LiveConfigDirectory,
            configuration.StateFilePath,
            configuration.ProfilesRoot,
            configuration.AppDataDirectory,
            configuration.RefreshLockWaitBound,
            MutationGateTimeout: TimeSpan.Zero));
        services.AddSingleton<ICredentialPairStore>(new FileSystemCredentialPairStore(configuration.LiveConfigDirectory, configuration.ProfilesRoot, TimeProvider.System));
        services.AddSingleton(new ClaudeStateFile(configuration.StateFilePath));
        services.AddSingleton(new ProfileFolderStore(configuration.ProfilesRoot));
        services.AddSingleton(new RateLimitGuardTeeFileReader(configuration.StatuslineTeePath));
        // Through the factory so the container owns the gate this file disposes.
        services.AddSingleton(_ => new RosterFile(configuration.AppDataDirectory));
        services.AddSingleton<IBrowserLauncher>(new ChromiumFamilyBrowserLauncher(configuration.BrowserExecutables));
        services.AddSingleton<IBrowserProfileReader>(new ChromiumLocalStateProfileReader());

        AddOutboundClients(services, AnthropicEndpoints.UserAgent(configuration.UserAgentProductToken, Version));
        services.AddSingleton(new SwitchJournal(configuration.AppDataDirectory));
        services.AddSingleton<CredentialMutationGate>();
        services.AddSingleton(ManagedLoginPolicyReader.ForCurrentMachine());
        Result<ClaudeExecutable, string> cli = ClaudeExecutableLocator.Locate(
            configuration.ClaudeExecutable,
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows(),
            Environment.SystemDirectory);
        AddCli(services, cli);
        // The login child factory: a real process, or a start that reports why no
        // CLI could be resolved rather than throwing at the first login.
        LoginChildFactory loginChild = cli.IsSuccess
            ? ProcessLoginChild.Factory(cli.Value)
            : (_, _) => Result<ILoginChild, string>.Failure(cli.Error);
        services.AddSingleton<ILoginSessionRunner>(provider => new ClaudeCliLoginSessionRunner(
            loginChild,
            provider.GetRequiredService<ProfileFolderStore>(),
            provider.GetRequiredService<ClaudeStateFile>(),
            provider.GetRequiredService<CredentialMutationGate>(),
            provider.GetRequiredService<IClaudeCliAuthStatus>(),
            provider.GetRequiredService<IClaudeCliLogout>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<LiveDirectorySwitch>();
        services.AddSingleton<DashboardState>();
        services.AddSingleton<DashboardAssembler>();
        services.AddHostedService<InstanceLockHolder>();
        services.AddHostedService<StartupReconciliation>();
        services.AddHostedService<StateFileWatcher>();
        // No checks registered: liveness only. MapRoutes maps the /healthz route this serves.
        services.AddHealthChecks();

        // Loopback only: the page is a local control surface, never a network service.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(configuration.ListenPort));
        // The framework's own host filter, ahead of everything of ours: a request
        // carrying a rebound name is refused before the pipeline reaches a route.
        // Set here rather than left to configuration, whose default is "*".
        services.Configure<HostFilteringOptions>(static options => options.AllowedHosts = ["localhost", "127.0.0.1", "[::1]"]);
        return Result<Unit, string>.Success(Unit.Value);
    }

    public static void MapRoutes(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<LoopbackHostMiddleware>();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(typeof(AppComposition).Assembly, "ClaudeCodeAccountRotation.App.wwwroot"),
        });

        // The framework's liveness probe with no checks registered: the body is the
        // status word and the middleware writes the no-store cache headers itself.
        app.MapHealthChecks("/healthz");
        app.MapGet("/", static () => Results.Content(EmbeddedPage.IndexHtml, "text/html; charset=utf-8"));
        DashboardEndpoints.Map(app);
        SwitchEndpoints.Map(app);
        RosterEndpoints.Map(app);
        LoginEndpoints.Map(app);
    }

    /// <summary>
    /// The two outbound calls the tool ever makes, both on demand and never on a
    /// timer, each through the factory so no captive HttpClient outlives DNS.
    /// The factory's logging handler names every header at Trace and redacts
    /// every value unless told otherwise, which is what keeps a bearer token out
    /// of a log file. Naming headers to redact would *narrow* that default, so
    /// this deliberately names none. Registered here rather than inline so a
    /// test exercises the same wiring the app runs.
    /// </summary>
    internal static void AddOutboundClients(IServiceCollection services, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient(nameof(AnthropicUsageEndpointClient))
            .AddTypedClient<IUsageEndpointClient>(http => new AnthropicUsageEndpointClient(http, userAgent, TimeProvider.System));
        services.AddHttpClient(nameof(ClaudeOAuthTokenRefreshClient))
            .AddTypedClient<ITokenRefreshClient>(http => new ClaudeOAuthTokenRefreshClient(http, userAgent, TimeProvider.System));
    }

    /// <summary>
    /// The one CLI process adapter, registered under both of the ports it serves.
    /// Through the container rather than by hand, because the adapter needs a
    /// logger: what a failing child printed goes there and never into the
    /// failure string the page renders.
    /// </summary>
    private static void AddCli(IServiceCollection services, Result<ClaudeExecutable, string> located)
    {
        if (located.IsFailure)
        {
            UnavailableClaudeCli unavailable = new(located.Error);
            services.AddSingleton<IClaudeCliAuthStatus>(unavailable);
            services.AddSingleton<IClaudeCliLogout>(unavailable);
            return;
        }

        ClaudeExecutable executable = located.Value;
        services.AddSingleton(provider => new ClaudeCliProcessAuthStatus(
            executable,
            _cliTimeout,
            provider.GetRequiredService<ILogger<ClaudeCliProcessAuthStatus>>()));
        services.AddSingleton<IClaudeCliAuthStatus>(static provider => provider.GetRequiredService<ClaudeCliProcessAuthStatus>());
        services.AddSingleton<IClaudeCliLogout>(static provider => provider.GetRequiredService<ClaudeCliProcessAuthStatus>());
    }

    /// <summary>Stands in when no CLI could be resolved: every call reports why.</summary>
    private sealed class UnavailableClaudeCli(string reason) : IClaudeCliAuthStatus, IClaudeCliLogout
    {
        public Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeAuthStatus, string>.Failure(reason));

        public Task<Result<Unit, string>> LogoutAsync(string configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<Unit, string>.Failure(reason));
    }
}
