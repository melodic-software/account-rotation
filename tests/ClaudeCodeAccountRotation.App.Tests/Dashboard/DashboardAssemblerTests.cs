using System.Net.Http.Json;
using System.Text.Json;
using ClaudeCodeAccountRotation.App.Tests.Adapters;

namespace ClaudeCodeAccountRotation.App.Tests.Dashboard;

/// <summary>
/// The tee file is last-writer-wins across every session on the machine, so a
/// card shows a snapshot only when the snapshot names that card's account and
/// is not the outgoing account's windows carried across a switch (6.6, AC 10).
/// </summary>
public sealed class DashboardAssemblerTests
{
    private const string LiveEmail = "dev.a@example.com";
    private const string OtherEmail = "dev.b@example.com";

    [Fact]
    public async Task ASnapshotNamingTheLiveAccountIsShownOnItsCard()
    {
        await using AppFactory factory = new();
        await factory.WriteStateFileAsync(LiveEmail, TestContext.Current.CancellationToken);
        await CredentialFiles.WriteAsync(factory.LiveDirectory, "live-token", TestContext.Current.CancellationToken);
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(LiveEmail));

        JsonElement card = await LiveCardAsync(factory);

        card.GetProperty("quota").GetProperty("fiveHourPercent").GetDouble().ShouldBe(69);
        card.GetProperty("quota").GetProperty("sevenDayPercent").GetDouble().ShouldBe(43);
        card.GetProperty("quota").GetProperty("capturedAt").GetDateTimeOffset()
            .ShouldBe(DateTimeOffset.Parse("2026-09-07T15:33:52Z", System.Globalization.CultureInfo.InvariantCulture));
        card.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnAbsentTeeFileLeavesTheCardWithoutNumbersAndWithoutANote()
    {
        await using AppFactory factory = new();
        await factory.WriteStateFileAsync(LiveEmail, TestContext.Current.CancellationToken);

        JsonElement card = await LiveCardAsync(factory);

        card.GetProperty("quota").ValueKind.ShouldBe(JsonValueKind.Null);
        card.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnUnreadableTeeFileLeavesTheCardWithoutNumbers()
    {
        await using AppFactory factory = new();
        await factory.WriteStateFileAsync(LiveEmail, TestContext.Current.CancellationToken);
        await WriteTeeAsync(factory, """{"captured_at":"2026-09-07T15:33:52Z","rate_lim""");

        JsonElement card = await LiveCardAsync(factory);

        card.GetProperty("quota").ValueKind.ShouldBe(JsonValueKind.Null);
        card.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ASnapshotWithNoAccountEmailIsUnattributed()
    {
        await using AppFactory factory = new();
        await factory.WriteStateFileAsync(LiveEmail, TestContext.Current.CancellationToken);
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(email: null));

        JsonElement card = await LiveCardAsync(factory);

        card.GetProperty("quota").ValueKind.ShouldBe(JsonValueKind.Null);
        card.GetProperty("quotaNote").GetString()!.ShouldContain("names no account");
    }

    [Fact]
    public async Task ASnapshotNamingAnotherAccountIsUnattributed()
    {
        await using AppFactory factory = new();
        await factory.WriteStateFileAsync(LiveEmail, TestContext.Current.CancellationToken);
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(OtherEmail));

        JsonElement card = await LiveCardAsync(factory);

        card.GetProperty("quota").ValueKind.ShouldBe(JsonValueKind.Null);
        card.GetProperty("quotaNote").GetString()!.ShouldContain(OtherEmail);
    }

    [Fact]
    public async Task PreSwitchWindowsAreUnattributed()
    {
        await using AppFactory factory = new();
        await CredentialFiles.WriteAsync(factory.LiveDirectory, "outgoing-token", TestContext.Current.CancellationToken);
        await factory.WriteStateFileAsync(OtherEmail, TestContext.Current.CancellationToken);
        await factory.ParkedProfileAsync(LiveEmail, "incoming-token", TestContext.Current.CancellationToken);
        factory.Cli.Email = LiveEmail;
        // The tee as it stood before the switch: the outgoing account's windows.
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(OtherEmail));

        using HttpClient client = factory.CreateMutatingClient();
        using HttpResponseMessage switched = await client.PostAsync(
            new Uri("/api/accounts/" + Uri.EscapeDataString(LiveEmail) + "/switch", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);
        switched.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        // A session that was mid-turn at switch time writes the outgoing
        // account's windows back under the incoming account's name.
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(LiveEmail));
        JsonElement card = await LiveCardAsync(factory, client);

        card.GetProperty("email").GetString().ShouldBe(LiveEmail);
        card.GetProperty("quota").ValueKind.ShouldBe(JsonValueKind.Null);
        card.GetProperty("quotaNote").GetString()!.ShouldContain("Pre-switch windows");

        // The first snapshot whose reset times differ is the incoming account's own.
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(LiveEmail, fiveHourResetsAt: 1788900400, sevenDayResetsAt: 1789415200));
        JsonElement fresh = await LiveCardAsync(factory, client);

        fresh.GetProperty("quota").GetProperty("fiveHourPercent").GetDouble().ShouldBe(69);
        fresh.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the stash is spent, not merely stepped over: the same reset times
        // again are now this account's own, because the windows have moved on once.
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(LiveEmail));
        JsonElement later = await LiveCardAsync(factory, client);

        later.GetProperty("quota").GetProperty("sevenDayPercent").GetDouble().ShouldBe(43);
        later.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ASwitchWithNoReadableTeeClearsAnEarlierStash()
    {
        // Two switches. The first stashes the outgoing windows; the second cannot
        // read the tee, and must not leave the first switch's values behind to
        // disown a later account's own snapshot whose reset times happen to match.
        await using AppFactory factory = new();
        await CredentialFiles.WriteAsync(factory.LiveDirectory, "outgoing-token", TestContext.Current.CancellationToken);
        await factory.WriteStateFileAsync(OtherEmail, TestContext.Current.CancellationToken);
        await factory.ParkedProfileAsync(LiveEmail, "incoming-token", TestContext.Current.CancellationToken);
        factory.Cli.Email = LiveEmail;
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(OtherEmail));

        using HttpClient client = factory.CreateMutatingClient();
        await SwitchAsync(client, LiveEmail);

        // Back the other way, with the tee gone at switch time.
        File.Delete(Path.Combine(factory.LiveDirectory, "rate-limit-guard", "rate-limits.json"));
        factory.Cli.Email = OtherEmail;
        (await SwitchAsync(client, OtherEmail)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        // The stash is gone, so these windows belong to whoever the tee names.
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(OtherEmail));
        JsonElement card = await LiveCardAsync(factory, client);

        card.GetProperty("email").GetString().ShouldBe(OtherEmail);
        card.GetProperty("quota").GetProperty("fiveHourPercent").GetDouble().ShouldBe(69);
        card.GetProperty("quotaNote").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient client, string email) =>
        client.PostAsync(
            new Uri("/api/accounts/" + Uri.EscapeDataString(email) + "/switch", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnAbsurdResetTimeInTheTeeBreaksNeitherEndpoint()
    {
        // The tee is written by other processes. A number outside DateTimeOffset's
        // range used to throw out of the read, and both endpoints read the tee, so
        // one line of that file could 500 the page and the switch alike.
        await using AppFactory factory = new();
        await CredentialFiles.WriteAsync(factory.LiveDirectory, "outgoing-token", TestContext.Current.CancellationToken);
        await factory.WriteStateFileAsync(OtherEmail, TestContext.Current.CancellationToken);
        await factory.ParkedProfileAsync(LiveEmail, "incoming-token", TestContext.Current.CancellationToken);
        factory.Cli.Email = LiveEmail;
        await WriteTeeAsync(factory, RateLimitGuardTeeFileReaderTests.Tee(OtherEmail, fiveHourResetsAt: 1000000000000000000L));

        using HttpClient client = factory.CreateMutatingClient();
        using HttpResponseMessage dashboard = await client.GetAsync(new Uri("/api/dashboard", UriKind.Relative), TestContext.Current.CancellationToken);
        using HttpResponseMessage switched = await client.PostAsync(
            new Uri("/api/accounts/" + Uri.EscapeDataString(LiveEmail) + "/switch", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        dashboard.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        // The switch reached the planner and ran rather than failing on the tee read.
        switched.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    private static async Task WriteTeeAsync(AppFactory factory, string content)
    {
        string path = Path.Combine(factory.LiveDirectory, "rate-limit-guard", "rate-limits.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> LiveCardAsync(AppFactory factory, HttpClient? existing = null)
    {
        HttpClient client = existing ?? factory.CreateClient();
        JsonElement dashboard = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/dashboard", UriKind.Relative), TestContext.Current.CancellationToken);
        return dashboard.GetProperty("accounts").EnumerateArray().Single(card => card.GetProperty("isLive").GetBoolean());
    }
}
