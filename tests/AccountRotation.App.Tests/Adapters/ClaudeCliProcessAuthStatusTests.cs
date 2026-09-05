using AccountRotation.App.Adapters.Process;
using AccountRotation.Core;
using AccountRotation.Core.Ports;

namespace AccountRotation.App.Tests.Adapters;

public sealed class ClaudeCliProcessAuthStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));

    public ClaudeCliProcessAuthStatusTests() => Directory.CreateDirectory(_root);

    public static bool OnWindows => OperatingSystem.IsWindows();

    private ClaudeExecutable FakeCli(string body)
    {
        string path = Path.Combine(_root, "claude.cmd");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return new ClaudeExecutable("cmd.exe", ["/c", path]);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "The fake CLI is a Windows batch file")]
    public async Task ParsesTheStatusJsonAndPassesTheConfigDirectory()
    {
        // The batch file forward-slashes the directory so the echoed JSON stays valid.
        ClaudeExecutable cli = FakeCli("set \"dir=%CLAUDE_CONFIG_DIR:\\=/%\"\r\necho {\"loggedIn\":true,\"email\":\"a@example.com\",\"authMethod\":\"claude.ai\",\"orgName\":\"Personal\",\"subscriptionType\":\"max\",\"projectsDirectory\":\"%dir%/projects\"}");
        ClaudeCliProcessAuthStatus reader = new(cli, TimeSpan.FromSeconds(30));

        Result<ClaudeAuthStatus, string> result = await reader.ReadAsync(_root, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.Email.ShouldBe("a@example.com");
        result.Value.LoggedIn.ShouldBeTrue();
        result.Value.SubscriptionType.ShouldBe("max");
        result.Value.ProjectsDirectory.ShouldBe(_root.Replace('\\', '/') + "/projects");
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "The fake CLI is a Windows batch file")]
    public async Task ANonZeroExitIsAFailureCarryingTheOutput()
    {
        ClaudeExecutable cli = FakeCli("echo not logged in 1>&2\r\nexit /b 1");
        ClaudeCliProcessAuthStatus reader = new(cli, TimeSpan.FromSeconds(30));

        Result<ClaudeAuthStatus, string> result = await reader.ReadAsync(null, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not logged in");
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "The fake CLI is a Windows batch file")]
    public async Task AHungProcessIsKilledAtTheTimeout()
    {
        ClaudeExecutable cli = FakeCli("ping -n 30 127.0.0.1 >nul");
        ClaudeCliProcessAuthStatus reader = new(cli, TimeSpan.FromMilliseconds(500));

        Result<ClaudeAuthStatus, string> result = await reader.ReadAsync(null, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("timed out");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
