using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.App.Tests.Endpoints;

public sealed class BrowserProfileEndpointTests
{
    private static readonly Uri _browserProfiles = new("/api/browser-profiles", UriKind.Relative);

    [Fact]
    public async Task TheEndpointReturnsEveryEnumeratedProfileWithItsBrowserLowercased()
    {
        await using AppFactory factory = new();
        factory.BrowserProfiles.Found.AddRange(
        [
            new BrowserProfile(BrowserFamily.Chrome, "Profile 2", "Kyle", AccountEmail.Parse("two@example.com").Value),
            new BrowserProfile(BrowserFamily.Edge, "Default", "Profile 1", null),
        ]);
        using HttpClient client = factory.CreateClient();

        JsonArray profiles = (await client.GetFromJsonAsync<JsonArray>(_browserProfiles, TestContext.Current.CancellationToken))!;

        profiles.Count.ShouldBe(2);
        profiles[0]!["browser"]!.GetValue<string>().ShouldBe("chrome");
        profiles[0]!["directory"]!.GetValue<string>().ShouldBe("Profile 2");
        profiles[0]!["name"]!.GetValue<string>().ShouldBe("Kyle");
        profiles[0]!["email"]!.GetValue<string>().ShouldBe("two@example.com");
        profiles[1]!["browser"]!.GetValue<string>().ShouldBe("edge");
        // The directory and the name disagree here, which is why the page shows both.
        profiles[1]!["directory"]!.GetValue<string>().ShouldBe("Default");
        profiles[1]!["name"]!.GetValue<string>().ShouldBe("Profile 1");
        profiles[1]!["email"].ShouldBeNull();
    }

    [Fact]
    public async Task NoProfilesAnywhereIsAnEmptyListRatherThanAFailure()
    {
        await using AppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        JsonArray profiles = (await client.GetFromJsonAsync<JsonArray>(_browserProfiles, TestContext.Current.CancellationToken))!;

        profiles.ShouldBeEmpty();
    }
}
