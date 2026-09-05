using AccountRotation.Core.Configuration;

namespace AccountRotation.App.Configuration;

/// <summary>
/// Defaults computed from the user profile: the live directory follows
/// <c>CLAUDE_CONFIG_DIR</c> exactly as the CLI does (the state file sits at the
/// home root when it is unset and inside the directory when it is set), and app
/// data goes under the platform's per-user local data directory.
/// </summary>
internal static class ConfigurationDefaults
{
    public const int DefaultListenPort = 48211;
    public const string ProductToken = "account-rotation";
    public static readonly TimeSpan DefaultRefreshLockWaitBound = TimeSpan.FromSeconds(10);

    public static AccountRotationConfiguration ForCurrentUser()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? claudeConfigDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return ForUser(home, localAppData, claudeConfigDirectory);
    }

    public static AccountRotationConfiguration ForUser(string homeDirectory, string localAppDataDirectory, string? claudeConfigDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);
        bool configured = !string.IsNullOrWhiteSpace(claudeConfigDirectory);
        string liveConfigDirectory = configured ? Path.GetFullPath(claudeConfigDirectory!) : Path.Combine(homeDirectory, ".claude");
        string stateFilePath = configured ? Path.Combine(liveConfigDirectory, ".claude.json") : Path.Combine(homeDirectory, ".claude.json");
        return new AccountRotationConfiguration(
            liveConfigDirectory,
            stateFilePath,
            Path.Combine(homeDirectory, ".claude-profiles"),
            Path.Combine(localAppDataDirectory, ProductToken),
            DefaultListenPort,
            DefaultRefreshLockWaitBound,
            ClaudeExecutable: null,
            PatchStateFile: true,
            ProductToken);
    }
}
