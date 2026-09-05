namespace AccountRotation.Core.Identity;

/// <summary>
/// A snapshot of the live config directory taken immediately before a switch is
/// planned: which account the state file names, whether a pair is present, and
/// its fingerprint.
/// </summary>
public sealed record LiveAccountState(
    string LiveConfigDirectory,
    string StateFilePath,
    OAuthAccountBlock? Account,
    bool HasCredentials,
    RefreshTokenFingerprint? Fingerprint,
    string? FreshLockFileName);
