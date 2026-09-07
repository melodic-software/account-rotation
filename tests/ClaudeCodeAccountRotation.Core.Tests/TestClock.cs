namespace ClaudeCodeAccountRotation.Core.Tests;

/// <summary>
/// A clock the test moves by hand. Small enough to own: the alternative is a
/// package reference on a test project that deliberately carries none.
/// </summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}
