namespace ClaudeCodeAccountRotation.Core.Switching;

/// <summary>
/// The device-managed login policy in effect: a <c>forceLoginOrgUUID</c> pins
/// every login to one organization, which makes switching personal accounts on
/// that machine a policy violation rather than a choice. A source that exists
/// but could not be read is <see cref="Unreadable"/> and blocks switching too:
/// the pin may be there, and guessing that it is not fails open. The App reads
/// the policy from the platform's managed settings; Core only consults it.
/// </summary>
public sealed record ManagedLoginPolicy(string? ForceLoginOrgUuid, string Source, bool Unreadable = false)
{
    public static ManagedLoginPolicy None { get; } = new(null, "no managed settings found");

    public bool BlocksSwitching => !string.IsNullOrEmpty(ForceLoginOrgUuid) || Unreadable;
}
