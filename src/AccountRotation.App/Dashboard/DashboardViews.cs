using AccountRotation.App.Switching;

namespace AccountRotation.App.Dashboard;

/// <summary>What the page renders. No token field exists on any of these types.</summary>
internal sealed record DashboardView(
    LiveAccountView? LiveAccount,
    IReadOnlyList<AccountCardView> Accounts,
    string? Banner,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CapturedAt);

internal sealed record LiveAccountView(string? Email, bool HasCredentials, string? Fingerprint);

internal sealed record AccountCardView(string Email, bool IsLive, bool HasCredentials, string? Folder);

internal sealed record SwitchOutcomeView(
    string Now,
    string? ParkedAs,
    string? CliEmail,
    string? CliError,
    bool IdentityMismatchWarning,
    DateTimeOffset At);

internal sealed record SwitchRefusalView(string Refusal, string Message);

/// <summary>Held across requests: the last reconciliation report for the banner.</summary>
internal sealed class DashboardState
{
    public ReconciliationReport? LastReconciliation { get; set; }
}
