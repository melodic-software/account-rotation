namespace AccountRotation.Core.Identity;

/// <summary>One folder under the profiles root: a parked account.</summary>
public sealed record ParkedProfile(
    AccountEmail Email,
    string FolderPath,
    bool HasCredentials,
    OAuthAccountBlock? Account);
