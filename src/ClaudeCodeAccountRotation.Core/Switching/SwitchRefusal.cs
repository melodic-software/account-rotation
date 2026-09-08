namespace ClaudeCodeAccountRotation.Core.Switching;

/// <summary>
/// Why a switch is not planned or not executed. Ordered as the planner checks
/// them: the spike's guards first, then the three the plan review added; the
/// last two are the executor's own, for a second mutation arriving while one
/// runs and for a login that owns one of the folders the switch would move.
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
    LoginInProgress,
}
