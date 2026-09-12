using ClaudeCodeAccountRotation.App.Quota;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeCodeAccountRotation.App.Tests.Hosting;

/// <summary>
/// Startup puts back any credential pair a refresh rotated and could not write.
/// The sweep runs before the first request and must never be able to stop the
/// tool: an operator locked out of the page by the very file that explains the
/// problem has no way back in.
/// </summary>
public sealed class StartupReconciliationTests
{
    [Fact]
    public async Task AMalformedRecoveryFileDoesNotStopTheAppAndBecomesAWarning()
    {
        await using AppFactory factory = new();
        string recovery = Path.Combine(factory.AppData, "recovery");
        Directory.CreateDirectory(recovery);
        await File.WriteAllTextAsync(
            Path.Combine(recovery, "a@example.com.credentials.json"),
            "{ this is not a credential envelope",
            TestContext.Current.CancellationToken);

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage health = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        health.IsSuccessStatusCode.ShouldBeTrue();
        factory.Services.GetRequiredService<QuotaState>().RecoveryWarnings.ShouldNotBeEmpty();
        // Moved aside rather than deleted: an unreadable file may still be the
        // only copy of something, so it is kept where the operator can find it.
        File.Exists(Path.Combine(recovery, "a@example.com.credentials.json")).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(recovery, "stale")).Length.ShouldBe(1);
    }
}
