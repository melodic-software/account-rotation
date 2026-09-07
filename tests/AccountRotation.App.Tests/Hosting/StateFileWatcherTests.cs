using System.Net;
using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;

namespace AccountRotation.App.Tests.Hosting;

public sealed class StateFileWatcherTests
{
    private static async Task<string?> StateFileEmailAsync(string path, CancellationToken cancellationToken)
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        return node?["oauthAccount"]?["emailAddress"]?.GetValue<string>();
    }

    [Fact]
    public async Task AStaleBlockWrittenBackAfterASwitchIsRepairedWithoutAnyRequest()
    {
        using AppFactory factory = new();
        await CredentialFiles.WriteAsync(factory.LiveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await factory.WriteStateFileAsync("a@example.com", TestContext.Current.CancellationToken);
        await factory.ParkedProfileAsync("b@example.com", "refresh-b", TestContext.Current.CancellationToken);
        factory.Cli.Email = "b@example.com";
        using HttpClient client = factory.CreateMutatingClient();
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/accounts/b%40example.com/switch", UriKind.Relative), content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateFileEmailAsync(factory.StateFilePath, TestContext.Current.CancellationToken)).ShouldBe("b@example.com");

        // A session writes its in-memory block back; no dashboard read follows.
        await factory.WriteStateFileAsync("a@example.com", TestContext.Current.CancellationToken);

        string? email = null;
        for (int attempt = 0; attempt < 40 && email != "b@example.com"; attempt++)
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
            email = await StateFileEmailAsync(factory.StateFilePath, TestContext.Current.CancellationToken);
        }

        email.ShouldBe("b@example.com", "the watcher repairs the block within seconds of the write-back");
    }
}
