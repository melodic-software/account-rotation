using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.App.Switching;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Tests;

/// <summary>
/// Hosts the app in-process over a temp layout (live directory, state file,
/// profiles root, app data) with the CLI port doubled, so no test ever touches
/// the machine's real Claude Code directories.
/// </summary>
internal sealed class AppFactory : WebApplicationFactory<Program>
{
    public AppFactory()
    {
        Root = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));
        LiveDirectory = Path.Combine(Root, "live");
        StateFilePath = Path.Combine(Root, ".claude.json");
        ProfilesRoot = Path.Combine(Root, "profiles");
        AppData = Path.Combine(Root, "appdata");
        Directory.CreateDirectory(LiveDirectory);
        Directory.CreateDirectory(ProfilesRoot);
        Directory.CreateDirectory(AppData);
        ConfigPath = Path.Combine(AppData, "config.json");
        JsonObject configuration = new()
        {
            ["liveConfigDirectory"] = LiveDirectory,
            ["stateFilePath"] = StateFilePath,
            ["profilesRoot"] = ProfilesRoot,
            ["appDataDirectory"] = AppData,
            ["refreshLockWaitSeconds"] = 0.3,
            ["claudeExecutable"] = null,
        };
        File.WriteAllText(ConfigPath, configuration.ToJsonString());
    }

    public string Root { get; }

    public string LiveDirectory { get; }

    public string StateFilePath { get; }

    public string ProfilesRoot { get; }

    public string AppData { get; }

    public string ConfigPath { get; }

    public CannedCli Cli { get; } = new();

    /// <summary>Moved by hand, so the ten-minute login expiry is asserted without a sleep.</summary>
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The scripted <c>claude auth login</c> every login test drives.</summary>
    public LoginChildScript LoginChild { get; } = new();

    public BrowserRecorder Browser { get; } = new();

    /// <summary>Every log line the host wrote, so a test can assert what never reaches one.</summary>
    public LogSink Logs { get; } = new();

    public static JsonObject AccountJson(string email) => new() { ["accountUuid"] = "uuid-" + email, ["emailAddress"] = email };

    public async Task WriteStateFileAsync(string email, CancellationToken cancellationToken)
    {
        JsonObject state = new() { ["numStartups"] = 3, ["oauthAccount"] = AccountJson(email) };
        await File.WriteAllTextAsync(StateFilePath, state.ToJsonString(), cancellationToken);
    }

    public async Task<string> ParkedProfileAsync(string email, string refreshToken, CancellationToken cancellationToken, DateTimeOffset? loginExpiresAt = null)
    {
        string folder = Path.Combine(ProfilesRoot, email);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, CredentialFiles.FileName),
            CredentialFiles.Shape(refreshToken, loginExpiresAt: loginExpiresAt).ToJsonString(),
            cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), AccountJson(email).ToJsonString(), cancellationToken);
        return folder;
    }

    public HttpClient CreateMutatingClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Claude-Code-Account-Rotation", "1");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ClaudeCodeAccountRotation:ConfigPath", ConfigPath);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IClaudeCliAuthStatus>(Cli));
            // Both ports, or a removal would resolve the real CLI on this machine.
            services.Replace(ServiceDescriptor.Singleton<IClaudeCliLogout>(Cli));
            services.Replace(ServiceDescriptor.Singleton<IBrowserLauncher>(Browser));
            // The real runner over a scripted child: the URL parsing, the retry, the
            // expiry, and the profile rewrite are the code under test, not doubles.
            services.Replace(ServiceDescriptor.Singleton<ILoginSessionRunner>(provider => new ClaudeCliLoginSessionRunner(
                LoginChild.Start,
                provider.GetRequiredService<ProfileFolderStore>(),
                provider.GetRequiredService<CredentialMutationGate>(),
                Clock)));
        });
    }

    // The base Dispose(bool) routes through DisposeAsync, so the host (and with it the
    // instance lock's delete-on-close handle) is only certainly gone once this returns.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>Records what the login flow asked the browser to open, and with which profile.</summary>
    internal sealed class BrowserRecorder : IBrowserLauncher
    {
        public List<(BrowserFamily Browser, string? ProfileDirectory, Uri Url)> Launched { get; } = [];

        public string? Error { get; set; }

        public Result<Unit, string> Launch(BrowserFamily browser, string? profileDirectory, Uri signInUrl)
        {
            if (Error is string error)
            {
                return Result<Unit, string>.Failure(error);
            }

            Launched.Add((browser, profileDirectory, signInUrl));
            return Result<Unit, string>.Success(Unit.Value);
        }
    }

    /// <summary>Collects every formatted log message the host writes.</summary>
    internal sealed class LogSink : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new Collector(_lines);

        public void Dispose() => GC.SuppressFinalize(this);

        private sealed class Collector(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                lines.Enqueue(formatter(state, exception) + " " + exception);
            }
        }
    }

    internal sealed class CannedCli : IClaudeCliAuthStatus, IClaudeCliLogout
    {
        public string? Email { get; set; }

        /// <summary>What every folder reports; "max" is the only tier the roster admits.</summary>
        public string? SubscriptionType { get; set; } = "max";

        /// <summary>Every config directory <c>auth logout</c> was run under, in order.</summary>
        public List<string> LogoutCalls { get; } = [];

        public string? LogoutError { get; set; }

        public Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeAuthStatus, string>.Success(new ClaudeAuthStatus(true, Email, "claude.ai", "Personal", SubscriptionType, null)));

        public Task<Result<Unit, string>> LogoutAsync(string configDirectory, CancellationToken cancellationToken)
        {
            LogoutCalls.Add(configDirectory);
            return Task.FromResult(LogoutError is string error
                ? Result<Unit, string>.Failure(error)
                : Result<Unit, string>.Success(Unit.Value));
        }
    }
}
