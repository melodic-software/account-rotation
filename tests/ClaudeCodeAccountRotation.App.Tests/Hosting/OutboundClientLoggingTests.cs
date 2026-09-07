using System.Collections.Concurrent;
using System.Net;
using ClaudeCodeAccountRotation.App.Hosting;
using ClaudeCodeAccountRotation.App.Tests.Adapters;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Tests.Hosting;

/// <summary>
/// The HTTP factory's own logging handler writes every request header at Trace.
/// An access token in a log file outlives the token, so the composition redacts
/// the header before the handler ever sees its value.
/// </summary>
public sealed class OutboundClientLoggingTests
{
    private const string AccessToken = "an-access-token-that-must-never-be-logged";

    [Fact]
    public async Task TheAccessTokenNeverReachesALogLine()
    {
        using CapturingLoggerProvider logs = new();
        ServiceCollection services = new();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        AppComposition.AddOutboundClients(services, "claude-code-account-rotation/1.2.3 (+https://example.invalid)");
        // Nothing reaches the network: the factory's own handlers stay, only the
        // socket at the bottom of the chain is replaced.
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigurePrimaryHttpMessageHandler(static () => new RecordingHandler(RecordingHandler.Json(HttpStatusCode.OK, """{"limits":[]}"""))));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IUsageEndpointClient client = provider.GetRequiredService<IUsageEndpointClient>();

        (await client.ReadUsageAsync(AccessToken, TestContext.Current.CancellationToken)).Value.Dispose();

        logs.Lines.ShouldNotBeEmpty("the logging handler must have run for this test to mean anything");
        logs.Lines.ShouldAllBe(line => !line.Contains(AccessToken, StringComparison.Ordinal));
        // The header is still named, so the redaction is visible rather than silent.
        logs.Lines.ShouldContain(line => line.Contains("Authorization", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentBag<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                lines.Add(formatter(state, exception));
            }
        }
    }
}
