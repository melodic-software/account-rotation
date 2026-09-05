using System.Text.Json.Nodes;

namespace AccountRotation.Core.Identity;

/// <summary>
/// The <c>oauthAccount</c> block of Claude Code's state file. Kept as raw JSON so
/// every key, known or not, round-trips byte-for-byte through park and unpark.
/// </summary>
public sealed record OAuthAccountBlock
{
    private OAuthAccountBlock(JsonObject raw, AccountEmail? email, string? organizationRateLimitTier)
    {
        Raw = raw;
        Email = email;
        OrganizationRateLimitTier = organizationRateLimitTier;
    }

    public JsonObject Raw { get; }

    public AccountEmail? Email { get; }

    public string? OrganizationRateLimitTier { get; }

    public static OAuthAccountBlock FromJson(JsonObject raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string? emailAddress = raw["emailAddress"]?.GetValue<string>();
        AccountEmail? email = emailAddress is null
            ? null
            : AccountEmail.Parse(emailAddress).Match(static parsed => (AccountEmail?)parsed, static _ => null);
        return new OAuthAccountBlock(raw, email, raw["organizationRateLimitTier"]?.GetValue<string>());
    }

    public override string ToString() => Email?.Value ?? "(no email)";
}
