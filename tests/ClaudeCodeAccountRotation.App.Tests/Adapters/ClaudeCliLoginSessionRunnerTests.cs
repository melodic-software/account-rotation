using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.App.Switching;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

/// <summary>
/// What the runner does when two calls arrive at once, and what it re-reads
/// before it lets a child near a folder. The endpoint tests drive the happy
/// path; these drive the races, so they hold the real runner directly.
/// </summary>
public sealed class ClaudeCliLoginSessionRunnerTests : IDisposable
{
    private const string ParkedEmail = "parked@example.com";
    private const string LiveEmail = "live@example.com";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _profilesRoot;
    private readonly string _stateFilePath;
    private readonly CredentialMutationGate _gate = new();
    private readonly LoginChildScript _script = new();
    private readonly AppFactory.CannedCli _cli = new();

    public ClaudeCliLoginSessionRunnerTests()
    {
        _profilesRoot = Path.Combine(_root, "profiles");
        _stateFilePath = Path.Combine(_root, ".claude.json");
        Directory.CreateDirectory(_profilesRoot);
    }

    private string Folder(string email)
    {
        string folder = Path.Combine(_profilesRoot, email);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static AccountEmail Email(string value) => AccountEmail.Parse(value).Value;

    private ClaudeCliLoginSessionRunner Runner(LoginChildFactory? start = null) => new(
        start ?? _script.Start,
        new ProfileFolderStore(_profilesRoot),
        new ClaudeStateFile(_stateFilePath),
        _gate,
        _cli,
        _cli,
        TimeProvider.System);

    private async Task WriteStateFileAsync(string email) =>
        await File.WriteAllTextAsync(
            _stateFilePath,
            new JsonObject { ["numStartups"] = 1, ["oauthAccount"] = AppFactory.AccountJson(email) }.ToJsonString(),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task TwoStartsAgainstOneFolderSpawnOneChild()
    {
        // A double-submit from the page. The check for a login already running and
        // the registration that makes this one visible to it have to be one step,
        // or both calls pass the check and two CLIs write one CLAUDE_CONFIG_DIR.
        using ManualResetEventSlim insideTheFactory = new();
        using ManualResetEventSlim release = new();
        int started = 0;
        using ClaudeCliLoginSessionRunner runner = Runner((arguments, folder) =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                insideTheFactory.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return _script.Start(arguments, folder);
        });
        string folder = Folder(ParkedEmail);

        Task<Result<LoginSession, string>> first = Task.Run(
            () => runner.StartAsync(Email(ParkedEmail), folder, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        insideTheFactory.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue();
        Task<Result<LoginSession, string>> second = Task.Run(
            () => runner.StartAsync(Email(ParkedEmail), folder, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        release.Set();
        Result<LoginSession, string>[] results = await Task.WhenAll(first, second);

        results.Count(static result => result.IsSuccess).ShouldBe(1);
        results.Single(static result => result.IsFailure).Error.ShouldContain("already running");
        started.ShouldBe(1);
        _script.Children.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheMutationGateIsHeldWhileTheChildIsSpawned()
    {
        // Which is what stops a switch from renaming that folder's pair away while
        // the child is writing a fresh one into it.
        bool gateWasHeld = false;
        using ClaudeCliLoginSessionRunner runner = Runner((arguments, folder) =>
        {
            try
            {
                using IDisposable permit = _gate.AcquireAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                gateWasHeld = true;
            }

            return _script.Start(arguments, folder);
        });

        Result<LoginSession, string> started = await runner.StartAsync(Email(ParkedEmail), Folder(ParkedEmail), TestContext.Current.CancellationToken);

        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error : "");
        gateWasHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task AStartIsRefusedWhenTheStateFileNamesThatAccountLive()
    {
        // The endpoint reads the live account once, before the gate. A switch that
        // completes in between would leave this login writing a second pair for the
        // account already live, so the authority is this read, under the gate.
        await WriteStateFileAsync(ParkedEmail);
        using ClaudeCliLoginSessionRunner runner = Runner();

        Result<LoginSession, string> started = await runner.StartAsync(Email(ParkedEmail), Folder(ParkedEmail), TestContext.Current.CancellationToken);

        started.IsFailure.ShouldBeTrue();
        started.Error.ShouldContain("live on this machine");
        _script.Children.ShouldBeEmpty();
    }

    [Fact]
    public async Task AStartIsAllowedWhenAnotherAccountIsLive()
    {
        await WriteStateFileAsync(LiveEmail);
        using ClaudeCliLoginSessionRunner runner = Runner();

        Result<LoginSession, string> started = await runner.StartAsync(Email(ParkedEmail), Folder(ParkedEmail), TestContext.Current.CancellationToken);

        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Error : "");
        runner.IsRunningAgainst(Folder(ParkedEmail)).ShouldBeTrue();
        runner.IsRunningAgainst(Folder(LiveEmail)).ShouldBeFalse();
    }

    [Fact]
    public async Task ASecondCodeSubmittedWhileOneIsInFlightIsRefusedRatherThanLosingTheAnswer()
    {
        // The pump answers through one field per session. A second submit that
        // replaced it left the first caller waiting out its whole reply budget for
        // a signal that had already been sent to the field it no longer holds.
        using ManualResetEventSlim writing = new();
        using ManualResetEventSlim release = new();
        _script.OnCode = (child, code) =>
        {
            writing.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            child.Emit("Invalid code. Please make sure the full code was copied.\r\n");
            return Task.CompletedTask;
        };
        using ClaudeCliLoginSessionRunner runner = Runner();
        LoginSession session = (await runner.StartAsync(Email(ParkedEmail), Folder(ParkedEmail), TestContext.Current.CancellationToken)).Value;

        Task<Result<LoginSession, string>> first = Task.Run(
            () => runner.SubmitCodeAsync(session.Id, "111111", TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        writing.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue();
        Result<LoginSession, string> second = await runner.SubmitCodeAsync(session.Id, "222222", TestContext.Current.CancellationToken);
        release.Set();
        Result<LoginSession, string> answered = await first;

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldContain("still checking");
        answered.IsSuccess.ShouldBeTrue(answered.IsFailure ? answered.Error : "");
        answered.Value.Message.ShouldNotBeNull().ShouldContain("rejected");
        _script.Last.CodesWritten.ShouldBe(["111111"]);
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
