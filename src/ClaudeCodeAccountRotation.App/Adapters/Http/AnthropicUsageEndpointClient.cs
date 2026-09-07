using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Ports;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.App.Adapters.Http;

/// <summary>
/// One honest GET of the usage endpoint per read. The tool names itself in the
/// User-Agent, sends the account's own access token, and makes no other request
/// to any Anthropic service: nothing here touches the model API.
/// </summary>
internal sealed class AnthropicUsageEndpointClient : IUsageEndpointClient
{
    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly TimeProvider _timeProvider;

    public AnthropicUsageEndpointClient(HttpClient http, string userAgent, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        http.Timeout = AnthropicEndpoints.RequestTimeout;
        _http = http;
        _userAgent = userAgent;
        _timeProvider = timeProvider;
    }

    public async Task<Result<JsonDocument, UsageReadFailure>> ReadUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using HttpRequestMessage request = new(HttpMethod.Get, AnthropicEndpoints.UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("anthropic-beta", AnthropicEndpoints.OAuthBeta);
        // The comment form "(+url)" is a valid User-Agent product comment, but the
        // typed header parser is stricter than the grammar, so it goes on as written.
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return Failure(UsageReadFailureKind.Transport, "the usage endpoint could not be reached: " + exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(UsageReadFailureKind.Transport, "the usage read timed out after " + AnthropicEndpoints.RequestTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<JsonDocument, UsageReadFailure>.Failure(AnthropicEndpoints.Failure(response, _timeProvider));
            }

            try
            {
                var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
                return Result<JsonDocument, UsageReadFailure>.Success(body);
            }
            catch (JsonException exception)
            {
                return Failure(UsageReadFailureKind.MalformedBody, "the usage response did not parse: " + exception.Message);
            }
        }
    }

    private static Result<JsonDocument, UsageReadFailure> Failure(UsageReadFailureKind kind, string detail) =>
        Result<JsonDocument, UsageReadFailure>.Failure(new UsageReadFailure(kind, detail));
}
