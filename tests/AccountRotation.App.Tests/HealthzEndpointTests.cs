using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AccountRotation.App.Tests;

public sealed class HealthzEndpointTests
{
    [Fact]
    public async Task HealthzRespondsOk()
    {
        await using WebApplicationFactory<Program> factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/healthz", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
