namespace ClaudeCodeAccountRotation.App.Tests;

/// <summary>
/// A clock the test moves by hand, so a ten-minute expiry is asserted in
/// microseconds and no test ever sleeps.
/// </summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}
