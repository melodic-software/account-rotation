namespace AccountRotation.Core.Switching;

/// <summary>
/// The device-managed login policy in effect: a <c>forceLoginOrgUUID</c> pins
/// every login to one organization, which makes switching personal accounts on
/// that machine a policy violation rather than a choice. The App reads it from
/// the platform's managed settings; Core only consults it.
/// </summary>
public sealed record ManagedLoginPolicy(string? ForceLoginOrgUuid, string Source)
{
    public static ManagedLoginPolicy None { get; } = new(null, "no managed settings found");

    public bool BlocksSwitching => !string.IsNullOrEmpty(ForceLoginOrgUuid);
}
