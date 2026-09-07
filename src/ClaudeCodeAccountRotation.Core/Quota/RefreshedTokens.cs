namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>
/// What the OAuth token endpoint returned for one refresh, in memory and
/// nowhere else. The refresh token rotates on every 200, so the caller owns
/// writing this back to the pair it came from before anything else can lose it;
/// the client that produced it persists nothing.
/// </summary>
public sealed record RefreshedTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset? LoginExpiresAt,
    IReadOnlyList<string> Scopes);
