using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IClaudeCliAuthStatus>(Cli)));
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

    internal sealed class CannedCli : IClaudeCliAuthStatus
    {
        public string? Email { get; set; }

        public Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeAuthStatus, string>.Success(new ClaudeAuthStatus(true, Email, "claude.ai", "Personal", "max", null)));
    }
}
