namespace AccountRotation.Core.Switching;

/// <summary>
/// Why a switch is not planned or not executed. Ordered as the planner checks
/// them: the spike's guards first, then the three the plan review added; the
/// last is the executor's own, for a second mutation arriving while one runs.
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
    ManagedPolicyUnreadable,
    LiveIdentityUnverified,
    MutationInProgress,
}
