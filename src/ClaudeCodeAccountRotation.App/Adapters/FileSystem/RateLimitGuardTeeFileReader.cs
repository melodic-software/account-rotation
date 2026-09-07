using System.Globalization;
using System.Text.Json;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Reads the <c>rate-limit-guard</c> tee file that live sessions write, the
/// free tier of the refresh contract. Every failure mode is an absent snapshot
/// rather than an error: the file may not exist (no session has run under the
/// guard), it may be mid-rewrite, it may carry no <c>rate_limits</c> (the
/// session observed no subscription windows), and its values are written by
/// other processes, so they are parsed and range-checked, never trusted.
/// </summary>
internal sealed class RateLimitGuardTeeFileReader(string teeFilePath)
{
    private readonly string _teeFilePath = Path.GetFullPath(teeFilePath);

    public async Task<StatuslineSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(_teeFilePath))
            {
                return null;
            }

            bytes = await SharedFileReader.ReadAllBytesAsync(_teeFilePath, cancellationToken);
        }
        catch (IOException)
        {
            // The writer renames a temp over this path; a read that lands in that
            // window is a missing snapshot, never a failed dashboard.
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return Snapshot(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StatuslineSnapshot? Snapshot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || Instant(root, "captured_at") is not DateTimeOffset capturedAt
            || !root.TryGetProperty("rate_limits", out JsonElement windows)
            || windows.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        (double? fiveHourPercent, DateTimeOffset? fiveHourResetsAt) = Window(windows, "five_hour");
        (double? sevenDayPercent, DateTimeOffset? sevenDayResetsAt) = Window(windows, "seven_day");
        return new StatuslineSnapshot(
            capturedAt,
            Text(root, "session_id"),
            AccountEmailOf(root),
            fiveHourPercent,
            fiveHourResetsAt,
            sevenDayPercent,
            sevenDayResetsAt);
    }

    /// <summary>
    /// The tee's account identity. The reader contract calls every value in this
    /// file untrusted and warns that a future account field may be an object of
    /// arbitrary strings, so the e-mail goes through <see cref="AccountEmail.Parse"/>
    /// and an unparsable one leaves the snapshot unattributed.
    /// </summary>
    private static AccountEmail? AccountEmailOf(JsonElement root)
    {
        if (!root.TryGetProperty("account", out JsonElement account) || account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? email = Text(account, "email");
        return email is null ? null : AccountEmail.Parse(email).Match(static parsed => (AccountEmail?)parsed, static _ => null);
    }

    /// <summary>
    /// One window, fail-open per the reader contract: a percentage outside 0 to
    /// 100, or a non-numeric one, leaves that window unknown rather than making
    /// the whole snapshot unreadable.
    /// </summary>
    private static (double? Percent, DateTimeOffset? ResetsAt) Window(JsonElement windows, string name)
    {
        if (!windows.TryGetProperty(name, out JsonElement window) || window.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        double? percent = window.TryGetProperty("used_percentage", out JsonElement used)
            && used.ValueKind == JsonValueKind.Number
            && used.TryGetDouble(out double value)
            && value is >= 0 and <= 100
                ? value
                : null;
        return (percent, Instant(window, "resets_at"));
    }

    /// <summary>Epoch seconds inside <c>rate_limits</c>, an ISO-8601 string for <c>captured_at</c>.</summary>
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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
