namespace ClaudeCodeAccountRotation.Core.Quota;

/// <summary>Why an outbound call did not produce an answer.</summary>
public enum UsageReadFailureKind
{
    /// <summary>The network, DNS, TLS, or the 20-second timeout.</summary>
    Transport,

    /// <summary>401: the access token expired. A parked pair may be refreshed once and retried.</summary>
    Unauthorized,

    /// <summary>429: honored by waiting out <see cref="UsageReadFailure.RetryAfter"/>, never by retrying.</summary>
    RateLimited,

    /// <summary>A 2xx whose body did not parse, or any other status.</summary>
    MalformedBody,
}

/// <summary>
/// The typed failure of either outbound call, the usage read and the token
/// refresh. Expected failures are results, not exceptions.
/// </summary>
public sealed record UsageReadFailure(UsageReadFailureKind Kind, string Detail, TimeSpan? RetryAfter = null);
