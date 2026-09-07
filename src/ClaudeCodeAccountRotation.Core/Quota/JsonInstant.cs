using System.Globalization;
using System.Text.Json;

namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>
/// Reads an instant out of JSON written by something else. Both the usage
/// endpoint and the statusline tee write one as either epoch seconds or an
/// ISO-8601 string, and both are values this tool does not control:
/// <see cref="DateTimeOffset.FromUnixTimeSeconds"/> throws outside its own
/// range, so a number far enough from now would otherwise turn one line of a
/// file another process wrote into a failed page. Anything unreadable, absent,
/// or out of range is an absent instant.
/// </summary>
public static class JsonInstant
{
    private static readonly long _earliest = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long _latest = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static DateTimeOffset? Read(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt64(out long epochSeconds) && epochSeconds >= _earliest && epochSeconds <= _latest
                ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds)
                : null;
        }

        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTimeOffset instant)
                ? instant
                : null;
    }
}
