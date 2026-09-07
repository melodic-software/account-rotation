using System.Net;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

/// <summary>
/// The outbound-call test double: it answers from a queued script and keeps
/// every request it was given, so a test can assert both what the adapter sent
/// and how many times it sent it. No test in this repository reaches the
/// network.
/// </summary>
internal sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> Bodies { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("The adapter sent " + (Requests.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) + " requests but the script held fewer responses.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (HttpRequestMessage request in Requests)
            {
                request.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
