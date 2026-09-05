namespace AccountRotation.App.Switching;

/// <summary>The paths and bounds a switch runs under; bound from the configuration.</summary>
internal sealed record SwitchOptions(
    string LiveConfigDirectory,
    string StateFilePath,
    string ProfilesRoot,
    string AppDataDirectory,
    TimeSpan RefreshLockWaitBound,
    TimeSpan MutationGateTimeout,
    bool PatchStateFile);

/// <summary>What startup or pre-plan reconciliation found and did.</summary>
internal sealed record ReconciliationReport(
    IReadOnlyList<string> Quarantined,
    string JournalOutcome,
    bool SwitchingBlocked,
    string? Banner);
