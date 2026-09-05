using AccountRotation.Core.Identity;

namespace AccountRotation.Core.Switching;

/// <summary>
/// Everything the planner consults, gathered by the App immediately before
/// planning. <paramref name="LiveFingerprintOwner"/> is the account the tool
/// last recorded as holding the live pair's lineage (null when unknown);
/// <paramref name="JournalOpen"/> is true while an earlier switch is still
/// unreconciled.
/// </summary>
public sealed record SwitchPlanningInput(
    LiveAccountState Live,
    ParkedProfile Target,
    CredentialPair? LiveCredentials,
    CredentialPair? TargetCredentials,
    ManagedLoginPolicy Policy,
    bool JournalOpen,
    AccountEmail? LiveFingerprintOwner,
    string ProfilesRoot,
    DateTimeOffset Now);
