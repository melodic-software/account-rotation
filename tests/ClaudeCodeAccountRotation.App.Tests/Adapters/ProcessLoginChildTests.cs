using System.Text;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.Core;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

/// <summary>
/// The one class in the login path that talks to a real process. Everything
/// else is asserted against a scripted child, so these tests are what stand
/// between "the tests pass" and "the operator's first login works": a real
/// pipe, a real npm-shim command line, a prompt printed with no line ending,
/// and a code read back off standard input.
/// </summary>
public sealed class ProcessLoginChildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));

    public ProcessLoginChildTests() => Directory.CreateDirectory(_root);

    public static bool OnWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// A batch file that behaves the way the spike saw the CLI behave: it prints
    /// the URL, then a prompt with no newline, rejects the first code and prompts
    /// again, and exits on the second.
    /// </summary>
    private ClaudeExecutable FakeLoginCli()
    {
        string path = Path.Combine(_root, "claude.cmd");
        File.WriteAllText(
            path,
            """
            @echo off
            echo https://claude.com/cai/oauth/authorize?code=true
            <nul set /p "=Paste code here if prompted > "
            set /p first=
            echo Invalid code. Please make sure the full code was copied.
            <nul set /p "=Paste code here if prompted > "
            set /p second=
            echo accepted [%second%] under %CLAUDE_CONFIG_DIR%
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        return new ClaudeExecutable(Path.Combine(Environment.SystemDirectory, "cmd.exe"), [], Shim: path);
    }

    /// <summary>Reads until the child ends, so the assertions see everything it printed.</summary>
    private static async Task<string> DrainAsync(ILoginChild child, CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        while (await child.ReadAsync(cancellationToken) is string chunk)
        {
            output.Append(chunk);
        }

        return output.ToString();
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "The fake CLI is a Windows batch file")]
    public async Task ThePromptReadsAPipedCodeAndTheRunnerSeesEveryChunkThenNull()
    {
        Result<ILoginChild, string> started = ProcessLoginChild.Factory(FakeLoginCli())(
            ClaudeCliLoginSessionRunner.Arguments(new("a@example.com")),
            _root);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error : "");
        using ILoginChild child = started.Value;

        // The prompt is printed without a line ending, so a reader waiting for one
        // would never see it; this must arrive before anything is written back.
        string greeting = await child.ReadAsync(TestContext.Current.CancellationToken) ?? string.Empty;
        while (!greeting.Contains("Paste code here", StringComparison.Ordinal))
        {
            greeting += await child.ReadAsync(TestContext.Current.CancellationToken);
        }

        greeting.ShouldContain("https://claude.com/cai/oauth/authorize");
        ClaudeCliLoginSessionRunner.ExtractAuthorizeUrl(greeting).ShouldNotBeNull();

        await child.WriteCodeAsync("000000-wrong", TestContext.Current.CancellationToken);
        await child.WriteCodeAsync("123456-right", TestContext.Current.CancellationToken);
        string rest = await DrainAsync(child, TestContext.Current.CancellationToken);

        rest.ShouldContain("Invalid code");
        // The code is read back whole. A CRLF written to the prompt would leave a
        // carriage return inside it, which the brackets would show.
        rest.ShouldContain("accepted [123456-right]");
        // The login ran under the folder it was given and no other config root.
        rest.ShouldContain(_root);
        // Null only once the pipes are drained and the process has exited, which is
        // what the runner treats as the moment to read the folder.
        (await child.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "The fake CLI is a Windows batch file")]
    public async Task StandardErrorIsDrainedRatherThanLeftToFillAndBlockTheChild()
    {
        string path = Path.Combine(_root, "claude.cmd");
        // Enough on the error pipe to fill it if nobody read it; a blocked child is
        // what the operator would see as a hang right after pasting the code.
        await File.WriteAllTextAsync(
            path,
            "@echo off\r\nfor /l %%i in (1,1,400) do echo warning line %%i 1>&2\r\necho done\r\n",
            TestContext.Current.CancellationToken);
        ClaudeExecutable cli = new(Path.Combine(Environment.SystemDirectory, "cmd.exe"), [], Shim: path);

        using ILoginChild child = ProcessLoginChild.Factory(cli)(["auth", "login"], _root).Value;
        string output = await DrainAsync(child, TestContext.Current.CancellationToken);

        output.ShouldContain("warning line 400");
        output.ShouldContain("done");
    }

    [Fact]
    public void AnExecutableThatCannotBeStartedIsAFailureCarryingTheReason()
    {
        ClaudeExecutable missing = new(Path.Combine(_root, "not-here", "claude"), []);

        Result<ILoginChild, string> started = ProcessLoginChild.Factory(missing)(["auth", "login"], _root);

        started.IsFailure.ShouldBeTrue();
        started.Error.ShouldContain("could not start");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
