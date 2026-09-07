using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

public sealed class RateLimitGuardTeeFileReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _teePath;

    public RateLimitGuardTeeFileReaderTests()
    {
        Directory.CreateDirectory(_directory);
        _teePath = Path.Combine(_directory, "rate-limits.json");
    }

    [Fact]
    public async Task AMissingFileIsAnAbsentSnapshot()
    {
        RateLimitGuardTeeFileReader reader = new(_teePath);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task TornJsonIsAnAbsentSnapshot()
    {
        await WriteAsync("""{"captured_at":"2026-09-07T15:33:52Z","rate_limits":{"five_ho""");
        RateLimitGuardTeeFileReader reader = new(_teePath);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task AFileWithoutRateLimitsIsAnAbsentSnapshot()
    {
        await WriteAsync("""{"captured_at":"2026-09-07T15:33:52Z","session_id":"s-1"}""");
        RateLimitGuardTeeFileReader reader = new(_teePath);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task ParsesBothWindowsAndTheSessionId()
    {
        await WriteAsync(Tee(email: null));
        RateLimitGuardTeeFileReader reader = new(_teePath);

        StatuslineSnapshot snapshot = (await reader.ReadAsync(TestContext.Current.CancellationToken))!;

        snapshot.CapturedAt.ShouldBe(DateTimeOffset.Parse("2026-09-07T15:33:52Z", System.Globalization.CultureInfo.InvariantCulture));
        snapshot.SessionId.ShouldBe("00000000-0000-4000-8000-000000000001");
        snapshot.FiveHourPercent.ShouldBe(69);
        snapshot.FiveHourResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1788800400));
        snapshot.SevenDayPercent.ShouldBe(43);
        snapshot.SevenDayResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1789315200));
    }

    [Fact]
    public async Task ParsesAccountEmailWhenPresent()
    {
        await WriteAsync(Tee("Dev.A@example.com"));
        RateLimitGuardTeeFileReader reader = new(_teePath);

        StatuslineSnapshot snapshot = (await reader.ReadAsync(TestContext.Current.CancellationToken))!;

        snapshot.Account!.Value.Value.ShouldBe("dev.a@example.com");
    }

    [Fact]
    public async Task AnOlderWriterWithoutAnAccountKeyLeavesTheSnapshotUnattributed()
    {
        await WriteAsync(Tee(email: null));
        RateLimitGuardTeeFileReader reader = new(_teePath);

        (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Account.ShouldBeNull();
    }

    [Theory]
    // The reader contract calls every value here untrusted: an object where a
    // string belongs, and an unparsable address, both leave it unattributed.
    [InlineData("""{"uuid":"u-1"}""")]
    [InlineData("\"not-an-email\"")]
    public async Task AnUntrustworthyAccountValueLeavesTheSnapshotUnattributed(string accountValue)
    {
        await WriteAsync("""{"captured_at":"2026-09-07T15:33:52Z","rate_limits":{"five_hour":{"used_percentage":69,"resets_at":1788800400}},"account":"""
            + accountValue
            + "}");
        RateLimitGuardTeeFileReader reader = new(_teePath);

        (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Account.ShouldBeNull();
    }

    [Fact]
    public async Task AnAbsurdPercentageLeavesThatWindowUnknownAndKeepsTheOther()
    {
        await WriteAsync("""
            {"captured_at":"2026-09-07T15:33:52Z","rate_limits":{"five_hour":{"used_percentage":4200,"resets_at":1788800400},"seven_day":{"used_percentage":43,"resets_at":1789315200}}}
            """);
        RateLimitGuardTeeFileReader reader = new(_teePath);

        StatuslineSnapshot snapshot = (await reader.ReadAsync(TestContext.Current.CancellationToken))!;

        snapshot.FiveHourPercent.ShouldBeNull();
        snapshot.SevenDayPercent.ShouldBe(43);
    }

    /// <summary>The tee file as rate-limit-guard 0.8.0 writes it.</summary>
    internal static string Tee(string? email, long fiveHourResetsAt = 1788800400, long sevenDayResetsAt = 1789315200)
    {
        JsonObject tee = new()
        {
            ["captured_at"] = "2026-09-07T15:33:52Z",
            ["session_id"] = "00000000-0000-4000-8000-000000000001",
            ["session_name"] = "a session",
            ["rate_limits"] = new JsonObject
            {
                ["five_hour"] = new JsonObject { ["used_percentage"] = 69, ["resets_at"] = fiveHourResetsAt },
                ["seven_day"] = new JsonObject { ["used_percentage"] = 43, ["resets_at"] = sevenDayResetsAt },
            },
        };
        if (email is not null)
        {
            tee["account"] = new JsonObject { ["email"] = email };
        }

        return tee.ToJsonString();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Task WriteAsync(string content) => File.WriteAllTextAsync(_teePath, content, TestContext.Current.CancellationToken);
}
