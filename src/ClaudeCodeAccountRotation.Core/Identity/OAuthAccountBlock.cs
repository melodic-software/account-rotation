using System.Text.Json.Nodes;

namespace ClaudeCodeAccountRotation.Core.Identity;

/// <summary>
/// The <c>oauthAccount</c> block of Claude Code's state file. Kept as raw JSON so
/// every key, known or not, round-trips byte-for-byte through park and unpark.
/// </summary>
public sealed record OAuthAccountBlock
{
    private OAuthAccountBlock(JsonObject raw, AccountEmail? email, string? organizationRateLimitTier, DateTimeOffset? profileFetchedAt)
    {
        Raw = raw;
        Email = email;
        OrganizationRateLimitTier = organizationRateLimitTier;
        ProfileFetchedAt = profileFetchedAt;
    }

    public JsonObject Raw { get; }

    public AccountEmail? Email { get; }

    public string? OrganizationRateLimitTier { get; }

    /// <summary>When the CLI last fetched the profile it stamped this block from (<c>profileFetchedAt</c>, epoch milliseconds), or null.</summary>
    public DateTimeOffset? ProfileFetchedAt { get; }

    public static OAuthAccountBlock FromJson(JsonObject raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string? emailAddress = raw["emailAddress"]?.GetValue<string>();
        AccountEmail? email = emailAddress is null
            ? null
            : AccountEmail.Parse(emailAddress).Match(static parsed => (AccountEmail?)parsed, static _ => null);
        DateTimeOffset? profileFetchedAt = raw["profileFetchedAt"] is JsonValue stamp && stamp.TryGetValue(out long epochMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds)
            : null;
        return new OAuthAccountBlock(raw, email, raw["organizationRateLimitTier"]?.GetValue<string>(), profileFetchedAt);
    }

    public override string ToString() => Email?.Value ?? "(no email)";
}
