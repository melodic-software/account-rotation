using ClaudeCodeAccountRotation.App.Dashboard;
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
    DashboardState state,
    ILogger<StartupReconciliation> logger) : IHostedService
{
    private readonly ILogger<StartupReconciliation> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ReconciliationReport report = await executor.ReconcileAsync(cancellationToken);
        state.LastReconciliation = report;
        LogReconciled(report.JournalOutcome, report.Quarantined.Count, report.SwitchingBlocked);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "startup reconciliation: {JournalOutcome}; quarantined {QuarantinedCount}; switching blocked: {Blocked}")]
    private partial void LogReconciled(string journalOutcome, int quarantinedCount, bool blocked);
}
