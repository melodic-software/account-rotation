namespace AccountRotation.Core.Switching;

/// <summary>
/// Why a switch is not planned. Ordered as the planner checks them: the spike's
/// guards first, then the three the plan review added.
/// </summary>
public enum SwitchRefusal
{
    TargetIsLiveDirectory,
    TargetHasNoCredentials,
    TargetHasNoAccountBlock,
    AlreadyOnTarget,
    SharesLiveRefreshToken,
    RefreshLockPresent,
    TargetLoginExpired,
    SwitchingBlockedByManagedPolicy,
    LiveIdentityUnverified,
}
