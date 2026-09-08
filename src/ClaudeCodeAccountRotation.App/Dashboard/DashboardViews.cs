using ClaudeCodeAccountRotation.App.Switching;

namespace ClaudeCodeAccountRotation.App.Dashboard;

/// <summary>What the page renders. No token field exists on any of these types.</summary>
internal sealed record DashboardView(
    LiveAccountView? LiveAccount,
    IReadOnlyList<AccountCardView> Accounts,
    string? Banner,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CapturedAt);

internal sealed record LiveAccountView(string? Email, bool HasCredentials, string? Fingerprint);

internal sealed record AccountCardView(
    string Email,
    bool IsLive,
    bool HasCredentials,
    string? Folder,
    StatuslineQuotaView? Quota = null,
    string? QuotaNote = null,
    RosterEntryView? Roster = null);

/// <summary>
/// The roster entry behind a card, or null when the account is on the machine
/// but not on the roster: that is the state Adopt exists to end.
/// </summary>
internal sealed record RosterEntryView(
    string Email,
    string? Alias,
    string? Browser,
    string? BrowserProfileDirectory,
    bool Paused,
    string? Notes);

/// <summary>
/// The windows a live session observed, as the tee recorded them. A card only
/// ever shows a snapshot that names its own account. The source is not a field
/// because the tee is the only source there is until the on-demand refresh
/// lands and gives it something to vary against.
/// </summary>
internal sealed record StatuslineQuotaView(
    double? FiveHourPercent,
    DateTimeOffset? FiveHourResetsAt,
    double? SevenDayPercent,
    DateTimeOffset? SevenDayResetsAt,
    DateTimeOffset CapturedAt);

internal sealed record SwitchOutcomeView(
    string Now,
    string? ParkedAs,
    string? CliEmail,
    string? CliError,
    bool IdentityMismatchWarning,
    DateTimeOffset At);

internal sealed record SwitchRefusalView(string Refusal, string Message);

/// <summary>
/// A login in flight, as the page sees it. The sign-in URL is an authorize URL
/// and carries no token; the message comes from the runner's fixed vocabulary,
/// so neither the one-time code nor the CLI's own output ever reaches here.
/// </summary>
internal sealed record LoginSessionView(
    string Id,
    string Email,
    string State,
    string? Message,
    string? SignInUrl,
    string? BrowserError,
    DateTimeOffset ExpiresAt);

/// <summary>Held across requests: the last reconciliation report for the banner.</summary>
internal sealed class DashboardState
{
    public ReconciliationReport? LastReconciliation { get; set; }

    /// <summary>
    /// The windows the tee held immediately before the last switch this tool
    /// performed. A session that was mid-turn at switch time bills its response
    /// to the outgoing account and then writes those windows under the incoming
    /// account's name, so a snapshot still carrying these reset times belongs to
    /// the account that just left, whatever it says. Cleared by the first
    /// snapshot whose reset times differ.
    /// ponytail: in memory, like the reconciliation report beside it. A restart
    /// forgets the stash, and the worst case is one stale card until the next
    /// statusline write.
    /// </summary>
    public PreSwitchWindows? PreSwitchWindows { get; set; }
}

/// <summary>The outgoing account's last known reset times, kept only to disown them.</summary>
internal sealed record PreSwitchWindows(DateTimeOffset? FiveHourResetsAt, DateTimeOffset? SevenDayResetsAt);
