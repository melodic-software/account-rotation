using System.Text.Json.Nodes;
using AccountRotation.Core;
using AccountRotation.Core.Identity;
using AccountRotation.Core.Ports;

namespace AccountRotation.App.Adapters.FileSystem;

/// <summary>
/// The file credential store Windows and Linux share: the live pair at
/// <c>&lt;live dir&gt;/.credentials.json</c> and one parked pair per profile
/// folder. Park and unpark are renames on one volume, so at no instant do two
/// files hold the same refresh token, and there is deliberately no copy path.
/// </summary>
internal sealed class FileSystemCredentialPairStore : ICredentialPairStore
{
    public const string FileName = ".credentials.json";
    private const string DaemonLockFileName = "daemon.lock";

    private readonly string _liveConfigDirectory;
    private readonly string _livePath;
    private readonly string _profilesRoot;
    private readonly TimeProvider _timeProvider;
    private readonly OAuthRefreshLock _refreshLock;

    public FileSystemCredentialPairStore(string liveConfigDirectory, string profilesRoot, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveConfigDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _liveConfigDirectory = Path.GetFullPath(liveConfigDirectory);
        _livePath = Path.Combine(_liveConfigDirectory, FileName);
        _profilesRoot = Path.GetFullPath(profilesRoot);
        _timeProvider = timeProvider;
        _refreshLock = new OAuthRefreshLock(_liveConfigDirectory, timeProvider);
    }

    public Task<CredentialPair?> ReadLiveAsync(CancellationToken cancellationToken) =>
        ReadPairAsync(_livePath, cancellationToken);

    public Task<CredentialPair?> ReadParkedAsync(string folderPath, CancellationToken cancellationToken) =>
        ReadPairAsync(Path.Combine(ProfileFolder(folderPath), FileName), cancellationToken);

    public Task MoveLiveToParkedAsync(string folderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string folder = ProfileFolder(folderPath);
        Directory.CreateDirectory(folder);
        Rename(_livePath, Path.Combine(folder, FileName));
        return Task.CompletedTask;
    }

    public Task MoveParkedToLiveAsync(string folderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rename(Path.Combine(ProfileFolder(folderPath), FileName), _livePath);
        // The CLI reloads credentials when the file's mtime differs from the one it
        // cached; a rename keeps the parked file's old mtime, so stamp it now.
        File.SetLastWriteTimeUtc(_livePath, _timeProvider.GetUtcNow().UtcDateTime);
        return Task.CompletedTask;
    }

    public Task MoveParkedToQuarantineAsync(string folderPath, string destinationDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        string destination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destination);
        Rename(Path.Combine(ProfileFolder(folderPath), FileName), Path.Combine(destination, FileName));
        return Task.CompletedTask;
    }

    public async Task<Result<Unit, string>> WriteParkedAsync(
        string folderPath,
        CredentialPair pair,
        RefreshTokenFingerprint expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pair);
        string path = Path.Combine(ProfileFolder(folderPath), FileName);
        CredentialPair? current = await ReadPairAsync(path, cancellationToken);
        if (current is null)
        {
            return Result<Unit, string>.Failure("no parked pair at " + path + "; nothing to replace");
        }

        if (current.Fingerprint != expected)
        {
            return Result<Unit, string>.Failure("the parked pair at " + path + " is no longer the one being replaced (fingerprint " + current.Fingerprint.Sha256Hex[..12] + " vs expected " + expected.Sha256Hex[..12] + ")");
        }

        await AtomicJsonFile.WriteAsync(path, pair.Raw, cancellationToken);
        return Result<Unit, string>.Success(Unit.Value);
    }

    public Task<Result<IAsyncDisposable, string>> AcquireRefreshLockAsync(TimeSpan waitBound, CancellationToken cancellationToken) =>
        _refreshLock.AcquireAsync(waitBound, cancellationToken);

    public string? FreshLockFileName(TimeSpan maxAge)
    {
        if (!Directory.Exists(_liveConfigDirectory))
        {
            return null;
        }

        DateTime threshold = _timeProvider.GetUtcNow().UtcDateTime - maxAge;
        foreach (string path in Directory.EnumerateFiles(_liveConfigDirectory, "*.lock"))
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, DaemonLockFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(path) >= threshold)
            {
                return name;
            }
        }

        return null;
    }

    private static async Task<CredentialPair?> ReadPairAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = await SharedFileReader.ReadAllBytesAsync(path, cancellationToken);
        var node = JsonNode.Parse(bytes);
        if (node is not JsonObject raw)
        {
            throw new InvalidDataException("The credential file at " + path + " is not a JSON object.");
        }

        return CredentialPair.FromJson(raw).Match(
            static pair => pair,
            reason => throw new InvalidDataException("The credential file at " + path + " is unreadable: " + reason));
    }

    /// <summary>
    /// A rename that fails loudly instead of ever leaving two holders: the
    /// destination must not exist, and both paths must sit on one volume, since
    /// File.Move across volumes silently copies then deletes.
    /// </summary>
    private static void Rename(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No credential pair to move.", sourcePath);
        }

        if (File.Exists(destinationPath))
        {
            throw new InvalidOperationException("Refusing to move " + sourcePath + ": " + destinationPath + " already holds a credential pair, and a second holder is never created.");
        }

        if (!SameVolume(sourcePath, destinationPath))
        {
            throw new InvalidOperationException("Refusing to move " + sourcePath + " to " + destinationPath + ": the paths are on different volumes and a move must be a rename, never a copy.");
        }

        File.Move(sourcePath, destinationPath);
    }

    private static bool SameVolume(string first, string second)
    {
        string? leftRoot = Path.GetPathRoot(Path.GetFullPath(first));
        string? rightRoot = Path.GetPathRoot(Path.GetFullPath(second));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(leftRoot, rightRoot, comparison);
    }

    private string ProfileFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string full = Path.GetFullPath(folderPath);
        string relative = Path.GetRelativePath(_profilesRoot, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative == ".")
        {
            throw new ArgumentException("A profile folder must sit directly under the profiles root " + _profilesRoot + "; got " + full, nameof(folderPath));
        }

        return full;
    }
}
