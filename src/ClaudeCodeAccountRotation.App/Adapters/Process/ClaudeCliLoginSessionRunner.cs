using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Switching;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.App.Adapters.Process;

/// <summary>
/// Drives <c>claude auth login --email &lt;address&gt;</c> under one account's
/// config directory with stdin and stdout piped, per the spike that settled the
/// mechanism on this machine.
/// <para>
/// What the spike settled and this file relies on: the sign-in URL is printed
/// wrapped in OSC 8 hyperlink escapes, so the URL is matched as a substring on
/// its authorize path rather than read as a line; the code prompt reads a piped
/// line; a rejected code leaves the prompt open for another attempt rather than
/// ending the process; and a non-zero exit says nothing on its own, so the
/// completion signal is the credential file appearing in the folder.
/// </para>
/// <para>
/// Sessions live in memory for ten minutes. The child is killed on expiry, on
/// cancel, and at shutdown. Nothing here writes the code anywhere but the
/// child's standard input, and no message returned to the page is built from
/// the child's own output.
/// </para>
/// </summary>
internal sealed partial class ClaudeCliLoginSessionRunner : ILoginSessionRunner, IDisposable
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a start waits for the URL, and a code for the CLI's answer.</summary>
    private static readonly TimeSpan _replyBudget = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan _gateWait = TimeSpan.FromSeconds(30);
    private const int MaximumCodeLength = 256;

    private const string RejectedMessage = "That code was rejected. Copy the whole code from the browser and paste it again.";
    private const string ExpiredMessage = "This login expired after ten minutes. Start it again.";
    private const string EndedMessage = "The login ended without writing a credential file. Start it again.";
    private const string ResidueKeptMessage = "The login wrote credentials but named no account, so the folder was left exactly as it is.";

    private readonly LoginChildFactory _start;
    private readonly ProfileFolderStore _profiles;
    private readonly ClaudeStateFile _stateFile;
    private readonly CredentialMutationGate _gate;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public ClaudeCliLoginSessionRunner(
        LoginChildFactory start,
        ProfileFolderStore profiles,
        ClaudeStateFile stateFile,
        CredentialMutationGate gate,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(stateFile);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        _start = start;
        _profiles = profiles;
        _stateFile = stateFile;
        _gate = gate;
        _clock = clock;
    }

    /// <summary>
    /// The argument list, never a command line. <c>--email</c> is what puts
    /// <c>login_hint</c> on the authorize URL, which is what pre-fills the
    /// address in the mapped browser profile.
    /// </summary>
    public static string[] Arguments(AccountEmail email) => ["auth", "login", "--email", email.Value];

    /// <summary>
    /// Admits a login and starts its child, all of it under the one mutation
    /// gate, which is what makes starting a login safe beside a switch.
    /// <para>
    /// Three things happen inside the gate and none of them is safe outside it.
    /// The scan for a login already running against the folder, and the
    /// registration that makes this one visible to that scan, are one step, so
    /// a double-submit cannot put two children on one <c>CLAUDE_CONFIG_DIR</c>.
    /// The live account is read again here, not just at the endpoint, because a
    /// switch can complete between the endpoint's read and this call, and a
    /// login into the live account's own parked folder is the second holder the
    /// tool exists to prevent. And because the switch reads the registration
    /// under the same gate, a switch either sees this login and refuses or
    /// completes before this one is admitted; the two can never interleave.
    /// </para>
    /// </summary>
    public async Task<Result<LoginSession, string>> StartAsync(AccountEmail email, string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string folder = Path.GetFullPath(folderPath);
        Session session;
        IDisposable? permit = null;
        try
        {
            try
            {
                permit = await _gate.AcquireAsync(_gateWait, cancellationToken);
            }
            catch (TimeoutException)
            {
                return Result<LoginSession, string>.Failure("another credential change is in progress; try the login again in a moment");
            }

            if (RunningAgainst(folder) is Session running)
            {
                return Result<LoginSession, string>.Failure(
                    "a login for " + running.Email.Value + " is already running against that folder; finish it or let it expire first");
            }

            if ((await _stateFile.ReadAccountBlockAsync(cancellationToken))?.Email == email)
            {
                return Result<LoginSession, string>.Failure(
                    "that account is live on this machine; switch away from it before logging it in again");
            }

            Result<ILoginChild, string> child = _start(Arguments(email), folder);
            if (child.IsFailure)
            {
                return Result<LoginSession, string>.Failure(child.Error);
            }

            session = new Session(LoginSessionId.New(), email, folder, child.Value, _clock.GetUtcNow() + SessionLifetime, SessionLifetime, _clock);
            _sessions[session.Id.Value] = session;
        }
        finally
        {
            permit?.Dispose();
        }

        // Outside the gate: the pump's own finish takes it, and the wait below can
        // outlast the child.
        session.Pump = Task.Run(() => PumpAsync(session), CancellationToken.None);

        await WaitAsync(session.Started.Task, cancellationToken);
        if (session.SignInUrl is null)
        {
            Cancel(session);
            return Result<LoginSession, string>.Failure(
                "the CLI printed no sign-in URL; run `claude auth login` by hand once to see what it says");
        }

        return Result<LoginSession, string>.Success(Snapshot(session));
    }

    public async Task<Result<LoginSession, string>> SubmitCodeAsync(LoginSessionId id, string code, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(id.Value, out Session? session))
        {
            return Result<LoginSession, string>.Failure("no login session with that id is running");
        }

        EnforceExpiry(session);
        if (session.State != LoginSessionState.Pending)
        {
            return Result<LoginSession, string>.Failure("that login session is no longer running; start a new one");
        }

        // Everything this refuses is refused by shape alone; no branch here ever
        // puts the code itself into the reason.
        string trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Result<LoginSession, string>.Failure("paste the code from the browser first");
        }

        if (trimmed.Length > MaximumCodeLength)
        {
            return Result<LoginSession, string>.Failure("that is longer than any login code; paste just the code");
        }

        if (trimmed.Any(char.IsControl))
        {
            // A newline inside the code would be two lines at the prompt, and the
            // second would answer whatever the CLI asks next.
            return Result<LoginSession, string>.Failure("a login code holds no line breaks or control characters");
        }

        TaskCompletionSource echo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (session.Sync)
        {
            // One code in flight per session. The pump answers through the field, so
            // a second submit that replaced it would leave the first caller's own
            // task uncompleted until its reply budget ran out, and the CLI's answer
            // to the first code would be read as the answer to the second.
            if (session.Echo is not null)
            {
                return Result<LoginSession, string>.Failure("that login is still checking the last code; wait for its answer before pasting again");
            }

            session.Message = null;
            session.Output.Clear();
            session.Echo = echo;
        }

        try
        {
            await session.Child.WriteCodeAsync(trimmed, cancellationToken);
            await WaitAsync(echo.Task, cancellationToken);
        }
        finally
        {
            lock (session.Sync)
            {
                if (ReferenceEquals(session.Echo, echo))
                {
                    session.Echo = null;
                }
            }
        }

        return Result<LoginSession, string>.Success(Snapshot(session));
    }

    public bool IsRunningAgainst(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return RunningAgainst(Path.GetFullPath(folderPath)) is not null;
    }

    public LoginSession? Status(LoginSessionId id)
    {
        if (!_sessions.TryGetValue(id.Value, out Session? session))
        {
            return null;
        }

        EnforceExpiry(session);
        return Snapshot(session);
    }

    public void Dispose()
    {
        foreach (Session session in _sessions.Values)
        {
            Cancel(session);
            session.Dispose();
        }

        _sessions.Clear();
    }

    /// <summary>
    /// The first URL in the text whose path is the OAuth authorize path. The
    /// pattern stops at every control character, so the OSC 8 escape bytes the
    /// URL is wrapped in are never part of the match, and it anchors on the
    /// path rather than on the prompt's wording, which is likelier to change.
    /// </summary>
    internal static Uri? ExtractAuthorizeUrl(string text)
    {
        foreach (Match match in UrlPattern().Matches(text))
        {
            if (Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? url)
                && url.AbsolutePath.EndsWith("/oauth/authorize", StringComparison.Ordinal))
            {
                return url;
            }
        }

        return null;
    }

    [GeneratedRegex(@"https://[^\s\p{Cc}""'<>]+", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex UrlPattern();

    private async Task PumpAsync(Session session)
    {
        try
        {
            while (true)
            {
                string? chunk = await session.Child.ReadAsync(session.Lifetime.Token);
                if (chunk is null)
                {
                    break;
                }

                string seen;
                lock (session.Sync)
                {
                    session.Output.Append(chunk);
                    seen = session.Output.ToString();
                }

                if (session.SignInUrl is null && ExtractAuthorizeUrl(seen) is Uri url)
                {
                    session.SignInUrl = url;
                    session.Started.TrySetResult();
                }

                // A rejection is only ever a nicety: the completion signal is the
                // credential file, so a CLI that reworded this line leaves the
                // session pending and the operator free to paste again.
                if (seen.Contains("invalid code", StringComparison.OrdinalIgnoreCase))
                {
                    Settle(session, LoginSessionState.Pending, RejectedMessage);
                }

                if (_clock.GetUtcNow() >= session.ExpiresAt)
                {
                    EnforceExpiry(session);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expiry or cancel; the finish below records where that left the folder.
        }
        catch (IOException)
        {
            // The child's pipes closed under us; same.
        }
        finally
        {
            session.Child.Kill();
            await FinishAsync(session);
            session.Started.TrySetResult();
        }
    }

    /// <summary>
    /// The single point where a finished login is taken at its word, and only
    /// once the child has ended. Reading the folder mid-run could see a
    /// credential file written before the state file names its account, and
    /// pruning on that reading would delete the fresh identity and leave the
    /// stale one.
    /// </summary>
    private async Task FinishAsync(Session session)
    {
        if (!File.Exists(Path.Combine(session.Folder, FileSystemCredentialPairStore.FileName)))
        {
            bool expired = _clock.GetUtcNow() >= session.ExpiresAt;
            Settle(
                session,
                expired ? LoginSessionState.Expired : LoginSessionState.Failed,
                expired ? ExpiredMessage : EndedMessage,
                onlyWhilePending: true);
            return;
        }

        // The folder holds a pair, so the login worked whatever happens next. Every
        // way the tidy-up can fail is caught here: a fault escaping would leave the
        // session pending behind a killed child until its expiry, and the operator
        // watching a completed login say "still waiting" for ten minutes.
        bool adopted;
        try
        {
            using IDisposable permit = await _gate.AcquireAsync(_gateWait, CancellationToken.None);
            adopted = await _profiles.AdoptFreshLoginAsync(session.Folder, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            adopted = false;
        }
        catch (IOException)
        {
            // A file the CLI wrote seconds ago is still held by something else.
            adopted = false;
        }
        catch (UnauthorizedAccessException)
        {
            adopted = false;
        }

        Settle(
            session,
            LoginSessionState.Completed,
            adopted ? "Logged in as " + session.Email.Value + "." : ResidueKeptMessage);
    }

    /// <summary>
    /// The pending session that owns <paramref name="folder"/>, or null. Expiry
    /// is enforced first, so a session whose ten minutes ran out while nothing
    /// asked releases its folder here rather than holding it until something
    /// polls the session itself.
    /// </summary>
    private Session? RunningAgainst(string folder)
    {
        foreach (Session running in _sessions.Values)
        {
            EnforceExpiry(running);
            if (running.State == LoginSessionState.Pending
                && string.Equals(running.Folder, folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return running;
            }
        }

        return null;
    }

    private void EnforceExpiry(Session session)
    {
        if (session.State != LoginSessionState.Pending || _clock.GetUtcNow() < session.ExpiresAt)
        {
            return;
        }

        Settle(session, LoginSessionState.Expired, ExpiredMessage);
        Cancel(session);
    }

    private static void Cancel(Session session)
    {
        session.Child.Kill();
        session.Lifetime.Cancel();
    }

    private static void Settle(Session session, LoginSessionState state, string? message, bool onlyWhilePending = false)
    {
        TaskCompletionSource? echo;
        lock (session.Sync)
        {
            if (onlyWhilePending && session.State != LoginSessionState.Pending)
            {
                return;
            }

            session.State = state;
            session.Message = message;
            echo = session.Echo;
        }

        echo?.TrySetResult();
    }

    private static LoginSession Snapshot(Session session)
    {
        lock (session.Sync)
        {
            return new LoginSession(session.Id, session.Email, session.SignInUrl, session.State, session.Message, session.ExpiresAt);
        }
    }

    /// <summary>
    /// Waits on the CLI's answer, bounded by the reply budget on the injected
    /// clock and by the caller's own cancellation. A budget that runs out is
    /// not a failure: the session is still pending and the page reports it as
    /// such.
    /// </summary>
    private async Task WaitAsync(Task signal, CancellationToken cancellationToken)
    {
        using CancellationTokenSource budget = new(_replyBudget, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            await signal.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The CLI has not answered yet; the caller reports the session as it stands.
        }
    }

    /// <summary>One login in flight, with the child it drives and the buffer it reads.</summary>
    private sealed class Session : IDisposable
    {
        public Session(LoginSessionId id, AccountEmail email, string folder, ILoginChild child, DateTimeOffset expiresAt, TimeSpan lifetime, TimeProvider clock)
        {
            Id = id;
            Email = email;
            Folder = folder;
            Child = child;
            ExpiresAt = expiresAt;
            Lifetime = new CancellationTokenSource(lifetime, clock);
        }

        public LoginSessionId Id { get; }

        public AccountEmail Email { get; }

        public string Folder { get; }

        public ILoginChild Child { get; }

        public DateTimeOffset ExpiresAt { get; }

        /// <summary>Cancelled at expiry, at cancel, and at shutdown; unblocks the pump's read.</summary>
        public CancellationTokenSource Lifetime { get; }

        public object Sync { get; } = new();

        /// <summary>The child's output since the last code was submitted, scanned for the URL and the rejection.</summary>
        public StringBuilder Output { get; } = new();

        public Uri? SignInUrl { get; set; }

        public LoginSessionState State { get; set; } = LoginSessionState.Pending;

        public string? Message { get; set; }

        /// <summary>Completed once the URL is captured or the session ends.</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed by the pump when the CLI answers the code being waited on.</summary>
        public TaskCompletionSource? Echo { get; set; }

        /// <summary>The reader task; awaited by the tests that assert on a finished login.</summary>
        public Task Pump { get; set; } = Task.CompletedTask;

        public void Dispose()
        {
            Child.Dispose();
            Lifetime.Dispose();
        }
    }
}
