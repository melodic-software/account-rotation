using AccountRotation.Core;
using AccountRotation.Core.Configuration;

namespace AccountRotation.App.Configuration;

/// <summary>
/// Refuses a configuration the credential-move contract cannot honor: a
/// profiles root on another volume than the live directory (a move would be a
/// copy), a profiles root that equals, contains, or sits inside the live
/// directory or the home root, or one under a sync folder (Files On-Demand
/// dehydrates a credential file into a placeholder and a synced folder uploads
/// refresh tokens, a second holder by another name).
/// </summary>
internal static class ConfigurationValidator
{
    private static readonly string[] _syncEnvironmentVariables = ["OneDrive", "OneDriveCommercial", "OneDriveConsumer"];
    private static readonly string[] _syncFolderNames = ["OneDrive", "Dropbox", "Google Drive"];

    public static Result<Unit, string> Validate(
        AccountRotationConfiguration configuration,
        string homeDirectory,
        Func<string, string?> volumeOf,
        Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentNullException.ThrowIfNull(volumeOf);
        ArgumentNullException.ThrowIfNull(environment);

        string live = Normalize(configuration.LiveConfigDirectory);
        string profiles = Normalize(configuration.ProfilesRoot);
        string home = Normalize(homeDirectory);

        if (Same(profiles, live) || Contains(profiles, live) || Contains(live, profiles))
        {
            return Failure("the profiles root " + profiles + " must not equal, contain, or sit inside the live config directory " + live);
        }

        if (Same(profiles, home))
        {
            return Failure("the profiles root must be a folder of its own, not the home directory " + home);
        }

        string? liveVolume = volumeOf(live);
        string? profilesVolume = volumeOf(profiles);
        if (!string.Equals(liveVolume, profilesVolume, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("the profiles root " + profiles + " (volume " + (profilesVolume ?? "?") + ") must sit on the same volume as the live config directory " + live + " (volume " + (liveVolume ?? "?") + "): credential pairs are moved by rename, never copied");
        }

        foreach (string variable in _syncEnvironmentVariables)
        {
            if (environment(variable) is string syncRoot && !string.IsNullOrWhiteSpace(syncRoot) && (Same(profiles, Normalize(syncRoot)) || Contains(Normalize(syncRoot), profiles)))
            {
                return Failure("the profiles root " + profiles + " sits under the " + variable + " sync folder " + syncRoot + "; credentials must never be synced");
            }
        }

        foreach (string folderName in _syncFolderNames)
        {
            string syncRoot = Normalize(Path.Combine(home, folderName));
            if (Same(profiles, syncRoot) || Contains(syncRoot, profiles))
            {
                return Failure("the profiles root " + profiles + " sits under the " + folderName + " folder; credentials must never be synced");
            }
        }

        if (configuration.ListenPort is < 1 or > 65535)
        {
            return Failure("listenPort must be between 1 and 65535");
        }

        return Result<Unit, string>.Success(Unit.Value);
    }

    /// <summary>The mount point that owns a path, so a junction or mapped drive compares by its real volume.</summary>
    public static string? VolumeOf(string path)
    {
        try
        {
            return new DriveInfo(Path.GetFullPath(path)).Name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Same(string first, string second) => string.Equals(first, second, Comparison);

    private static bool Contains(string parent, string child) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, Comparison);

    private static Result<Unit, string> Failure(string reason) => Result<Unit, string>.Failure(reason);
}
