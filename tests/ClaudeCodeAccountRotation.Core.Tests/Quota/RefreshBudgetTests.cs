using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.Core.Tests.Quota;

public sealed class RefreshBudgetTests
{
    private static readonly AccountEmail _accountA = new("dev.a@example.com");
    private static readonly AccountEmail _accountB = new("dev.b@example.com");

    [Fact]
    public void TheSeventhReserveInsideTheWindowIsDenied()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        // The gap is set aside here so the window rule is what the test measures;
        // under the shipped defaults the 60-second gap binds first.
        RefreshBudget budget = new(clock, minimumGapSinceLastRead: TimeSpan.Zero);

        for (int read = 0; read < 6; read++)
        {
            budget.TryReserve(_accountA).ShouldBeTrue("reserve " + read.ToString(System.Globalization.CultureInfo.InvariantCulture) + " should be allowed");
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        budget.TryReserve(_accountA).ShouldBeFalse();
    }

    [Fact]
    public void TheWindowSlidesSoTheOldestReadStopsCounting()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock, minimumGapSinceLastRead: TimeSpan.Zero);
        for (int read = 0; read < 6; read++)
        {
            budget.TryReserve(_accountA);
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        budget.TryReserve(_accountA).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMinutes(5));

        budget.TryReserve(_accountA).ShouldBeTrue();
    }

    [Fact]
    public void AReserveWithinSixtySecondsOfTheLastReadIsDenied()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock);

        budget.TryReserve(_accountA).ShouldBeTrue();
        clock.Advance(TimeSpan.FromSeconds(59));
        budget.TryReserve(_accountA).ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        budget.TryReserve(_accountA).ShouldBeTrue();
    }

    [Fact]
    public void UnauthorizedResponseDoesNotStartTheGapClock()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock);

        budget.TryReserve(_accountA).ShouldBeTrue();
        budget.RecordUnauthorized(_accountA);
        clock.Advance(TimeSpan.FromSeconds(2));

        // The retry after the credential refresh rides the original reservation.
        budget.TryReserve(_accountA).ShouldBeTrue();
    }

    [Fact]
    public void UnauthorizedResponsesConsumeNoBudget()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock, minimumGapSinceLastRead: TimeSpan.Zero);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            budget.TryReserve(_accountA).ShouldBeTrue();
            budget.RecordUnauthorized(_accountA);
        }

        budget.TryReserve(_accountA).ShouldBeTrue();
    }

    [Fact]
    public void ALockoutRefusesEveryReserveUntilRetryAfterHasPassed()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock, minimumGapSinceLastRead: TimeSpan.Zero);

        budget.RecordLockout(_accountA, TimeSpan.FromSeconds(300));

        budget.LockedOutFor(_accountA)!.Value.ShouldBe(TimeSpan.FromSeconds(300));
        budget.TryReserve(_accountA).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(299));
        budget.TryReserve(_accountA).ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        budget.LockedOutFor(_accountA).ShouldBeNull();
        budget.TryReserve(_accountA).ShouldBeTrue();
    }

    [Fact]
    public void EachAccountCarriesItsOwnBudget()
    {
        TestClock clock = new(DateTimeOffset.Parse("2026-09-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RefreshBudget budget = new(clock);

        budget.TryReserve(_accountA).ShouldBeTrue();
        budget.RecordLockout(_accountA, TimeSpan.FromSeconds(300));

        budget.TryReserve(_accountB).ShouldBeTrue();
        budget.LockedOutFor(_accountB).ShouldBeNull();
    }
}
