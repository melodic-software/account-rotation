namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>
/// The bucket a <see cref="UsageLimit"/> measures. Parsed from the response's
/// <c>kind</c>; anything the endpoint adds later arrives as
/// <see cref="Unknown"/> and still renders, because the card model is driven by
/// the generic <c>limits[]</c> array and never by a bucket's name.
/// </summary>
public enum LimitKind
{
    Unknown,
    Session,
    WeeklyAll,
    WeeklyScoped,
}

/// <summary>
/// One entry of the usage response's <c>limits[]</c> array: the five-hour
/// session window, the weekly all-models window, or a weekly window scoped to
/// one model.
/// </summary>
public sealed record UsageLimit(
    string RawKind,
    LimitKind Kind,
    string? Group,
    double Percent,
    string? Severity,
    DateTimeOffset? ResetsAt,
    string? ScopeDisplayName,
    bool IsActive);

/// <summary>The usage-credits block: whether extra usage is on and why not.</summary>
public sealed record ExtraUsageState(bool IsEnabled, string? DisabledReason, bool SpendLimitReached);
