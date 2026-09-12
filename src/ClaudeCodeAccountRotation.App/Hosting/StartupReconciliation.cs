using ClaudeCodeAccountRotation.App.Dashboard;
using ClaudeCodeAccountRotation.App.Quota;
using ClaudeCodeAccountRotation.App.Switching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Hosting;

/// <summary>
/// Runs the switch executor's reconciliation once at startup, before the
/// first request, and keeps its report for the page's banner.
/// </summary>
internal sealed partial class StartupReconciliation(
    LiveDirectorySwitch executor,
    RecoveryFiles recovery,
    DashboardState state,
    ILogger<StartupReconciliation> logger) : IHostedService
{
    private readonly ILogger<StartupReconciliation> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ReconciliationReport report = await executor.ReconcileAsync(cancellationToken);
        state.LastReconciliation = report;
        LogReconciled(report.JournalOutcome, report.Quarantined.Count, report.SwitchingBlocked);
        await RestoreStrandedPairsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Puts back any credential pair a refresh rotated and could not write, now
    /// that nothing else is running. Every file is already attempted inside its
    /// own catch; this outer guard covers everything else, because a tool that
    /// refuses to start over a recovery file would leave the operator with no way
    /// to reach the very page that explains it.
    /// </summary>
    private async Task RestoreStrandedPairsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await recovery.RestoreAllAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogRestoreSweepFailed(exception.ToString());
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "startup reconciliation: {JournalOutcome}; quarantined {QuarantinedCount}; switching blocked: {Blocked}")]
    private partial void LogReconciled(string journalOutcome, int quarantinedCount, bool blocked);

    [LoggerMessage(Level = LogLevel.Error, Message = "the recovery sweep could not run: {Failure}")]
    private partial void LogRestoreSweepFailed(string failure);
}
