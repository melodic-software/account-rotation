using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Tests;

/// <summary>
/// A logger that keeps every formatted line, so a test can assert both what
/// reached the log and what stayed out of the value returned to the caller.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _lines.Enqueue(formatter(state, exception));
    }
}
