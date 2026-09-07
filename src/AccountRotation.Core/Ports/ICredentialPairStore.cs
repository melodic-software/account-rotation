using AccountRotation.Core.Identity;

namespace AccountRotation.Core.Ports;

/// <summary>
/// The credential store: the live pair and the parked pairs. Pairs are moved,
/// never copied, so at most one holder of a refresh token exists at any moment.
/// The file adapter serves Windows and Linux; a Keychain adapter is deferred.
/// </summary>
public interface ICredentialPairStore
{
    Task<CredentialPair?> ReadLiveAsync(CancellationToken cancellationToken);

    Task<CredentialPair?> ReadParkedAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>Parks the live pair into <paramref name="folderPath"/> by a rename on one volume.</summary>
    Task MoveLiveToParkedAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Unparks the pair in <paramref name="folderPath"/> into the live directory by a
    /// rename on one volume and sets the live file's mtime to now, since the CLI
    /// reloads credentials on an mtime change.
    /// </summary>
    Task MoveParkedToLiveAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the pair parked in <paramref name="folderPath"/> into
    /// <paramref name="destinationDirectory"/> (created if missing) by the same
    /// guarded rename as every other move: an existing destination or another
    /// volume refuses rather than copies.
    /// </summary>
    Task MoveParkedToQuarantineAsync(string folderPath, string destinationDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a parked pair only when the file still holds the pair whose
    /// fingerprint is <paramref name="expected"/> (compare-and-swap), so a rotated
    /// pair never overwrites one that moved meanwhile.
    /// </summary>
    Task<Result<Unit, string>> WriteParkedAsync(
        string folderPath,
        CredentialPair pair,
        RefreshTokenFingerprint expected,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acquires Claude Code's own refresh mutex, the <c>.oauth_refresh.lock</c>
    /// directory beside the live pair, waiting up to <paramref name="waitBound"/>.
    /// Disposing releases it.
    /// </summary>
    Task<Result<IAsyncDisposable, string>> AcquireRefreshLockAsync(TimeSpan waitBound, CancellationToken cancellationToken);

    /// <summary>
    /// The secondary guard: the name of any <c>*.lock</c> file (other than
    /// <c>daemon.lock</c>) in the live directory modified within
    /// <paramref name="maxAge"/>, or null.
    /// </summary>
    string? FreshLockFileName(TimeSpan maxAge);
}
