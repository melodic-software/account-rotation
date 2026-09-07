namespace AccountRotation.App.Switching;

/// <summary>The paths and bounds a switch runs under; bound from the configuration.</summary>
internal sealed record SwitchOptions(
    string LiveConfigDirectory,
    string StateFilePath,
    string ProfilesRoot,
    string AppDataDirectory,
    TimeSpan RefreshLockWaitBound,
    TimeSpan MutationGateTimeout);

/// <summary>What a stale-identity repair pass found.</summary>
internal enum IdentityRepair
{
    /// <summary>The state file names the live pair's recorded owner, or there is no record to judge by.</summary>
    NotNeeded,

    /// <summary>A session had written an older block back; the owner's block was patched in again.</summary>
    Repatched,

    /// <summary>A credential mutation was in progress; nothing was read or written.</summary>
    Busy,

    /// <summary>The state file is stale but the owner's block is not on disk to restore it from.</summary>
    NoProfileBlock,
}

/// <summary>What startup or pre-plan reconciliation found and did.</summary>
internal sealed record ReconciliationReport(
    IReadOnlyList<string> Quarantined,
    string JournalOutcome,
    bool SwitchingBlocked,
    string? Banner);
