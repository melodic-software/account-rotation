using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>Where a card's numbers came from, rendered as "via snapshot", "via refresh", "cached".</summary>
public enum QuotaSource
{
    StatuslineSnapshot,
    OnDemandRefresh,
    Cached,
}

/// <summary>
/// One account's quota as of one moment, from whichever source produced it.
/// Every card states its source and its capture time (AC 4).
/// </summary>
public sealed record UsageSnapshot(
    AccountEmail Account,
    DateTimeOffset CapturedAt,
    QuotaSource Source,
    IReadOnlyList<UsageLimit> Limits,
    ExtraUsageState? ExtraUsage)
{
    public double? FiveHourPercent => Find(LimitKind.Session)?.Percent;

    public DateTimeOffset? SevenDayResetsAt => Find(LimitKind.WeeklyAll)?.ResetsAt;

    private UsageLimit? Find(LimitKind kind) => Limits?.FirstOrDefault(limit => limit.Kind == kind);
}
