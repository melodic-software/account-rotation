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

    // Matched as prefixes of the folder directly under the home directory, since the
    // clients decorate the name: "OneDrive - Contoso", "Dropbox (Personal)", "My Drive".
    private static readonly string[] _syncFolderPrefixes = ["OneDrive", "Dropbox", "Google Drive", "My Drive", "iCloudDrive", "iCloud Drive", "Box"];

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

        // Both roots that ever hold a credential file: the profiles root (parked pairs) and
        // the app data directory (quarantined pairs). Each must share the live volume, since
        // every move is a rename, and neither may sit under a sync folder.
        string appData = Normalize(configuration.AppDataDirectory);
        string? liveVolume = volumeOf(live);
        foreach ((string label, string root) in new[] { ("profiles root", profiles), ("app data directory", appData) })
        {
            string? rootVolume = volumeOf(root);
            if (!string.Equals(liveVolume, rootVolume, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("the " + label + " " + root + " (volume " + (rootVolume ?? "?") + ") must sit on the same volume as the live config directory " + live + " (volume " + (liveVolume ?? "?") + "): credential pairs are moved by rename, never copied");
            }

            foreach (string variable in _syncEnvironmentVariables)
            {
                if (environment(variable) is string syncRoot && !string.IsNullOrWhiteSpace(syncRoot) && (Same(root, Normalize(syncRoot)) || Contains(Normalize(syncRoot), root)))
                {
                    return Failure("the " + label + " " + root + " sits under the " + variable + " sync folder " + syncRoot + "; credentials must never be synced");
                }
            }

            if (SyncFolderUnderHome(root, home) is string syncFolder)
            {
                return Failure("the " + label + " " + root + " sits under the " + syncFolder + " folder; credentials must never be synced");
            }
        }

        if (configuration.ListenPort is < 1 or > 65535)
        {
            return Failure("listenPort must be between 1 and 65535");
        }

        return Result<Unit, string>.Success(Unit.Value);
    }

    /// <summary>
    /// The volume that owns a path: the drive root on Windows, and elsewhere the
    /// longest mount point that contains the path. <see cref="DriveInfo"/> built
    /// from a path reports the path itself as its name on Unix, which would make
    /// every two directories look like two volumes.
    /// </summary>
    public static string? VolumeOf(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows())
            {
                return new DriveInfo(full).Name;
            }

            string? owner = null;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string mount = Path.TrimEndingDirectorySeparator(drive.Name);
                bool owns = mount == "/"
                    || string.Equals(full, mount, StringComparison.Ordinal)
                    || full.StartsWith(mount + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                if (owns && (owner is null || mount.Length > owner.Length))
                {
                    owner = mount;
                }
            }

            return owner;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The name of the sync folder directly under the home directory that owns <paramref name="path"/>, or null.</summary>
    private static string? SyncFolderUnderHome(string path, string home)
    {
        if (!Contains(home, path))
        {
            return null;
        }

        string firstSegment = path[(home.Length + 1)..].Split(Path.DirectorySeparatorChar, 2)[0];
        return _syncFolderPrefixes.Any(prefix => firstSegment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ? firstSegment
            : null;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Same(string first, string second) => string.Equals(first, second, Comparison);

    private static bool Contains(string parent, string child) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, Comparison);

    private static Result<Unit, string> Failure(string reason) => Result<Unit, string>.Failure(reason);
}
