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

    /// <summary>
    /// The target folder's rotated credential pair sits in the recovery directory
    /// after a write-back that failed: the file still in the folder holds the
    /// refresh token the token endpoint killed the moment it answered, so moving
    /// it to live would move a dead pair there, and the restore that could still
    /// put the rotated one back compares against the parked file this switch
    /// would have taken away. A restart or a per-card refresh clears it.
    /// </summary>
    TargetStrandedInRecovery,
    TargetLoginExpired,
    SwitchingBlockedByManagedPolicy,
    ManagedPolicyUnreadable,
    LiveIdentityUnverified,
    MutationInProgress,
    LoginInProgress,
}
