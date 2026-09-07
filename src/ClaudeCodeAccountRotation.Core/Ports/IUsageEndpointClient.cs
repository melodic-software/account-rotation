using System.Text.Json;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// The one read the tool makes of Anthropic's usage endpoint, on demand and
/// never on a timer. The body comes back unparsed so
/// <see cref="UsageResponseParser"/> stays a pure function over it.
/// </summary>
public interface IUsageEndpointClient
{
    Task<Result<JsonDocument, UsageReadFailure>> ReadUsageAsync(string accessToken, CancellationToken cancellationToken);
}
