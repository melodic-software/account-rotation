using System.Reflection;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.App.Adapters.Process;
using AccountRotation.App.Configuration;
using AccountRotation.App.Dashboard;
using AccountRotation.App.Endpoints;
using AccountRotation.App.Security;
using AccountRotation.App.Switching;
using AccountRotation.Core;
using AccountRotation.Core.Configuration;
using AccountRotation.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace AccountRotation.App.Hosting;

/// <summary>
/// The composition root: configuration, validation, the single-instance lock,
/// every service with its explicit type, Kestrel on loopback, and the routes.
/// </summary>
internal static class AppComposition
{
    private const string ConfigPathSettingKey = "AccountRotation:ConfigPath";
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

        AccountRotationConfiguration defaults = ConfigurationDefaults.ForCurrentUser();
        string configPath = arguments.ConfigPath
            ?? builder.Configuration[ConfigPathSettingKey]
            ?? Path.Combine(defaults.AppDataDirectory, ConfigFileName);

        Result<AccountRotationConfiguration, string> loaded = await ConfigurationFile.LoadOrCreateAsync(configPath, defaults, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result<Unit, string>.Failure(loaded.Error);
        }

        AccountRotationConfiguration configuration = arguments.Port is int port ? loaded.Value with { ListenPort = port } : loaded.Value;
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
            MutationGateTimeout: TimeSpan.Zero,
            configuration.PatchStateFile));
        services.AddSingleton<ICredentialPairStore>(new FileSystemCredentialPairStore(configuration.LiveConfigDirectory, configuration.ProfilesRoot, TimeProvider.System));
        services.AddSingleton(new ClaudeStateFile(configuration.StateFilePath));
        services.AddSingleton(new ProfileFolderStore(configuration.ProfilesRoot));
        services.AddSingleton(new SwitchJournal(configuration.AppDataDirectory));
        services.AddSingleton<CredentialMutationGate>();
        services.AddSingleton(ManagedLoginPolicyReader.ForCurrentMachine());
        services.AddSingleton(ResolveCli(configuration));
        services.AddSingleton<LiveDirectorySwitch>();
        services.AddSingleton<DashboardState>();
        services.AddSingleton<DashboardAssembler>();
        services.AddHostedService<InstanceLockHolder>();
        services.AddHostedService<StartupReconciliation>();

        // Loopback only: the page is a local control surface, never a network service.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(configuration.ListenPort));
        return Result<Unit, string>.Success(Unit.Value);
    }

    public static void MapRoutes(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<LoopbackHostMiddleware>();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(typeof(AppComposition).Assembly, "AccountRotation.App.wwwroot"),
        });

        app.MapGet("/healthz", static () => Results.Ok(new { status = "ok" }));
        app.MapGet("/", static () => Results.Content(EmbeddedPage.IndexHtml, "text/html; charset=utf-8"));
        DashboardEndpoints.Map(app);
        SwitchEndpoints.Map(app);
    }

    private static IClaudeCliAuthStatus ResolveCli(AccountRotationConfiguration configuration)
    {
        Result<ClaudeExecutable, string> located = ClaudeExecutableLocator.Locate(
            configuration.ClaudeExecutable,
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows());
        return located.IsSuccess
            ? new ClaudeCliProcessAuthStatus(located.Value, _cliTimeout)
            : new UnavailableClaudeCliAuthStatus(located.Error);
    }

    /// <summary>Stands in when no CLI could be resolved: every read reports why.</summary>
    private sealed class UnavailableClaudeCliAuthStatus(string reason) : IClaudeCliAuthStatus
    {
        public Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeAuthStatus, string>.Failure(reason));
    }
}
