using System.Globalization;
using System.Text.Json;

namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>
/// Reads the usage response's <c>limits[]</c> array and <c>extra_usage</c>
/// block, and nothing else. The response also carries codenamed top-level
/// buckets that are undocumented, mostly null, and free to change; naming one
/// here would make the dashboard break the week Anthropic renames it, so every
/// bucket the cards render comes from the generic array.
/// </summary>
public static class UsageResponseParser
{
    public static Result<IReadOnlyList<UsageLimit>, string> ParseLimits(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return Result<IReadOnlyList<UsageLimit>, string>.Failure("the usage response is not a JSON object");
        }

        if (!body.TryGetProperty("limits", out JsonElement limits) || limits.ValueKind != JsonValueKind.Array)
        {
            return Result<IReadOnlyList<UsageLimit>, string>.Failure("the usage response carries no limits array");
        }

        List<UsageLimit> parsed = [];
        foreach (JsonElement entry in limits.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string rawKind = Text(entry, "kind") ?? string.Empty;
            parsed.Add(new UsageLimit(
                rawKind,
                KindOf(rawKind),
                Text(entry, "group"),
                Number(entry, "percent") ?? 0,
                Text(entry, "severity"),
                Instant(entry, "resets_at"),
                ScopeDisplayName(entry),
                Flag(entry, "is_active") ?? true));
        }

        return Result<IReadOnlyList<UsageLimit>, string>.Success(parsed);
    }

    public static ExtraUsageState? ParseExtraUsage(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("extra_usage", out JsonElement extra)
            || extra.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ExtraUsageState(
            Flag(extra, "is_enabled") ?? false,
            Text(extra, "disabled_reason"),
            Flag(extra, "spend_limit_reached") ?? false);
    }

    private static LimitKind KindOf(string rawKind) => rawKind switch
    {
        "session" => LimitKind.Session,
        "weekly_all" => LimitKind.WeeklyAll,
        "weekly_scoped" => LimitKind.WeeklyScoped,
        _ => LimitKind.Unknown,
    };

    /// <summary>The scoped bucket's label, from <c>scope.model.display_name</c>.</summary>
    private static string? ScopeDisplayName(JsonElement entry) =>
        entry.TryGetProperty("scope", out JsonElement scope) && scope.ValueKind == JsonValueKind.Object
        && scope.TryGetProperty("model", out JsonElement model) && model.ValueKind == JsonValueKind.Object
            ? Text(model, "display_name")
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
            ? number
            : null;

    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>
    /// An instant the endpoint writes either as an ISO-8601 string (as the
    /// top-level buckets did in spike 01) or as epoch seconds; both are read, so
    /// a shape change on one side of the response does not blank a card.
    /// </summary>
    private static DateTimeOffset? Instant(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long epochSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset instant)
                ? instant
                : null;
    }
}
