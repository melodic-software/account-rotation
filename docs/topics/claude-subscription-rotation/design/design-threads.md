# Design threads: claude-code-account-rotation

Light-form design, 2026-09-04. Status vocabulary: **resolved** (decided by the Brief, a spike, or
an org standard, with the source named), **directional** (a direction chosen here with rationale;
the alternative and its switch condition are recorded so the user can flip it at plan approval),
**deferred** (needs research or a spike; tagged).

Every thread below is resolved, directional, or tagged-deferred. No thread is open and untagged.

## T1. Package topology — directional

Two source projects and two test projects:

- `ClaudeCodeAccountRotation.Core`: domain types, ports (interfaces), and pure operations (parsing, ranking,
  switch planning, sanitization, refresh budget). Depends on nothing but the BCL.
- `ClaudeCodeAccountRotation.App`: the executable. Kestrel minimal API, the embedded static page, every
  adapter (file system, HTTP, process, browser), configuration loading, and the composition root.
- `ClaudeCodeAccountRotation.Core.Tests`, `ClaudeCodeAccountRotation.App.Tests`.

Rationale: the org's `architecture-and-design.md` "Dependency direction" and "Testable by design"
want the logic that decides (ranking, refusals, parsing) separated from the code that touches the
machine, and a project boundary is the cheapest way to make the compiler enforce it. Alternative:
one project with folders. Switch condition: if Core ends up under about 400 lines, collapse into one
project; the boundary is not worth a second assembly at that size.

## T2. Live-dir switch mechanics — resolved (Brief Q18, spike 04)

Model D as proven: park the live pair into the outgoing account's folder with its `oauthAccount`
block saved as `profile.json`, move the incoming pair in, patch only `oauthAccount` in the state
file. State file path: `~/.claude.json` when `CLAUDE_CONFIG_DIR` is unset, `<dir>/.claude.json` when
set. Guards from `spike-04-swap.py` carried verbatim: refuse on target-is-live, same refresh token,
missing pair, missing account block, fresh `*.lock` other than `daemon.lock`. Moves are `File.Move`
on one volume (a rename); JSON writes are temp-file, `Flush(true)`, replace (the org's
`error-handling.md` "atomic-replace idiom" bar; Windows `File.Replace` has no directory fsync, so the
flush before replace is the durability step).

**Refresh lock, resolved from the binary (2026-09-04, Claude Code 2.1.261):** the CLI serializes
token refresh through `<live dir>/.oauth_refresh.lock`, a `proper-lockfile` directory lock
(`stale: 60000`, `update: 5000`, exclusive `mkdir`, cross-process); a contending session gets a
retryable "another process is refreshing" error. Sampling that lock leaves a TOCTOU window in which
a session refreshes and writes the outgoing account's rotated pair over the incoming one, so the
tool **acquires** the same directory exclusively (P/Invoke `CreateDirectoryW` or libc `mkdir`),
steals it only when its mtime is older than 60 s, holds it across park, unpark, and patch, and
removes it in `finally`. Wait bound: configurable, default 10 seconds, then refuse with
`RefreshLockPresent`. The wildcard `*.lock` file scan remains a secondary guard. Recheck trigger:
the lock name or library changes in a CLI release. The CLI reloads credentials when the file's
mtime changes (`lastCredentialsMtimeMs`, inequality compare), so the unparked file's mtime is set
to now as belt and braces.

Added by the plan review (2026-09-04): every credential mutation (switch, refresh write-back, login
completion, remove) runs under one in-process `CredentialMutationGate`, a second instance is refused
by an `InstanceLock`, the switch writes a `SwitchJournal` before the first move and reconciles it at
startup and before every plan (a crash between unpark and patch is otherwise undetectable, because
`claude auth status` reads the same state file), three more refusals exist (`TargetLoginExpired`,
`SwitchingBlockedByManagedPolicy`, `LiveIdentityUnverified`), a parked pair whose access token has
expired is refreshed while still parked before unparking, a profiles root on another volume is
refused at configuration time (the Brief's "moved, never copied" leaves no room for a copy path),
and every dashboard read re-checks that the state file's account matches the live fingerprint's
known owner.

## T3. Quota data model — resolved (Brief, spike 01)

`UsageSnapshot { CapturedAt, Source, Limits[], ExtraUsage }` where each `UsageLimit` is one
`limits[]` entry (`kind`, `group`, `percent`, `severity`, `resets_at`, `scope.model.display_name`,
`is_active`). `kind` is kept as the raw string plus a parsed `LimitKind { Session, WeeklyAll,
WeeklyScoped, Unknown }`; unknown kinds still render. Nothing reads `five_hour`, `seven_day`, or any
codenamed top-level bucket. Login expiry comes from the credential store's `refreshTokenExpiresAt`.

## T4. Three-tier refresh and the request budget — resolved (Brief Q19, spike 02)

Tier 1: statusline tee file (`captured_at`, `rate_limits`, and `account` once C10 lands). Tier 2:
Refresh all, one read per non-paused account, one-second spacing, `Retry-After` honored, card shows
"rate limited, retry in N s" with last known values. Tier 3: at a decision point (active account
crosses the auto-refresh threshold, default 80, or a switch is about to be proposed) refresh the top
three queue candidates, skipping any account read within the last 60 seconds. Budget: at most 6
reads per account per rolling 5 minutes, tracked in memory per account. No timer touches the
endpoint. A per-account snapshot cache under app data survives restarts so cards keep their "as of"
line. Directional detail: the cache; switch condition: if the cache invites stale-data confusion,
drop it and show "not read since start".

## T5. Credential refresh on 401 — resolved (Brief Q19, spike 03)

Refresh only a parked pair, once, then retry the usage read once. Write back the rotated
`refreshToken`, the new `accessToken`, `expiresAt` from `expires_in`, and `refreshTokenExpiresAt`
recomputed from `refresh_token_expires_in` (spike 03 named the stale-field defect). Any HTTP error
leaves the file untouched and surfaces on the card. The refresh request carries Claude Code's
public OAuth client id as the spike did; the tool never mints tokens by any other flow.

## T6. Routing policy and switch proposals — resolved (Brief Q4, Q11)

Pure functions over snapshots: eligibility, earliest-weekly-reset ordering, queue of three, switch
proposal at trip or 90, switch-back proposal at most once per 30 minutes, urgency flag within 24
hours. Thresholds, cooldown, queue length, and the auto-refresh trigger are settings with the Brief's
defaults. A proposal is rendered; it never executes. "Never mid-turn" is honored by construction:
the tool has no turn awareness and takes no action without a click.

## T7. Roster, profile folders, and identity — resolved (Brief Q17)

`roster.json` (version 1) under the per-user app-data dir: entries `{ email, alias?, browser,
browserProfileDirectory?, paused, notes? }`. Folder name from the Brief's sanitizer. Identity from
`profile.json` beside the pair, or from the live state file for the live account; folder names are
labels. **Adopt-live**: when the live state file names an account absent from the roster (the case
after a manual `/login`, which happened again on 2026-09-04), the dashboard offers to add it. A
roster entry with no pair and not live shows "needs login".

## T8. Browser-assisted login flow — directional, with a spike gate

Two candidate mechanisms, both running the unmodified binary under `CLAUDE_CONFIG_DIR=<folder>`:

- **(a) Piped `claude auth login --email <email>`.** The tool spawns the command with stdin and
  stdout piped, extracts the printed sign-in URL, opens it in the mapped browser profile, and when
  the user pastes the one-time code into the dashboard, writes it to the child's stdin. No terminal
  emulation, fully driven from the page. Unverified: spike 03 saw terminal pastes rejected four
  times on a loaded machine; a pipe avoids terminal paste handling, which may be the cause, but
  whether the prompt reads a piped line at all is untested.
- **(b) Visible terminal running interactive `claude`,** where the user types `/login`, presses `c`
  to copy the URL, and the tool (watching the clipboard or asking the user to paste the URL) opens
  it in the mapped profile; the localhost callback completes the login with no code. Proven by hand
  on 2026-09-04; semi-manual.

Direction: build (a) as primary if **spike 05b** passes (the first work item of the plan's login
phase: run the piped flow once on this machine and record whether the code is accepted), else ship
(b) as primary. Either way
the other stays as the documented fallback. Switch condition: 05b fails, or the CLI removes the
code prompt. The handoff recorded (b) as the settled primary; this thread reopens it because (a)
removes the manual step the goal statement calls out, and the spike is cheap.

## T9. Local web app security — directional

Kestrel binds `127.0.0.1` on the configured port. Mutating endpoints (`POST`, `PATCH`, `DELETE`)
require the request's `Origin` header to equal the app's own origin, or be absent, and require a
custom header (`X-Claude-Code-Account-Rotation: 1`), so a cross-site form post and a cross-origin `fetch` are
both refused before any handler runs. No CORS. The page never receives token material; responses
carry emails, percentages, times, and fingerprints only. Logs carry no tokens (the credential types
have no `ToString` over secret fields). Alternative: a per-launch bearer token embedded in the page.
Switch condition: a documented browser behavior that lets a cross-origin page pass a custom header
without preflight.

## T10. Configuration and defaults — resolved (Brief Q16, Q22)

One `config.json` under the per-user app-data dir (`%LOCALAPPDATA%\claude-code-account-rotation` on Windows;
XDG config dir elsewhere), created on first run from the shipped `config.template.json`. Keys: live
config dir (default `CLAUDE_CONFIG_DIR` or `~/.claude`), profiles root (default `~/.claude-profiles`),
tee path, listen port, routing policy, refresh budget, browser executable overrides, lock wait bound,
User-Agent product token. Defaults are computed from the user profile at runtime; the template
contains no path literal. `--config <path>` and `--port <n>` override at launch. Default port:
directional (`48211`); switch condition: a collision on any of the three machines.

## T11. Platform abstractions — resolved (Brief Q16, Q22, Q26; Khorikov via `/tdd:principles`)

An interface exists only where a second implementation is real or the dependency is out of process
and must be substituted in tests (Khorikov: interfaces for unmanaged dependencies, concrete classes
for managed ones; the org's `code-design.md` flags one-implementation interfaces as speculative
generality). Six ports:

- `ICredentialPairStore`: the credential store, whose second implementation (macOS Keychain, Q26)
  is planned. Windows and Linux share the file adapter.
- `IUsageEndpointClient`, `ITokenRefreshClient`: Anthropic's endpoints, out of process.
- `IClaudeCliAuthStatus`, `ILoginSessionRunner`: the `claude` process, out of process.
- `IBrowserLauncher`: a browser process.

Everything else that touches the machine is a concrete class rooted at a configurable path and
tested against a temp directory: `ClaudeStateFile`, `ProfileFolderStore`, `RosterFile`,
`RateLimitGuardTeeFileReader`, `AtomicJsonFile`. Time comes from `TimeProvider`. Browser launching:
Chrome, Edge, Brave via `--profile-directory=<dir> <url>`, executable resolved from the config
override, then the platform's known install locations. Switch condition for adding a port: a real
second implementation appears (for example a non-file roster store), never "for testability"
alone, since the file classes are already testable through their root path.

## T12. Test-seam posture — resolved (org `testing.md`, `overlays/dotnet.md`)

Seams, fewest that cover the surface:

1. **Core public functions** (parser, ranking, advisor, planner, sanitizer, budget): unit tests with
   fixture JSON (spike 01 shape, including codenamed buckets that must be ignored) and clock control
   through `TimeProvider`.
2. **File classes against real temp directories** (state-based tests; the file system is a managed
   dependency the app owns, so no double): move semantics, single-holder fingerprint check after
   ten switches, byte-preservation of every non-`oauthAccount` key, atomic replace leaving no temp
   residue. **Out-of-process ports against doubles** (communication-based only where the side
   effect is externally visible): HTTP adapters run against a fake `HttpMessageHandler` (200,
   401-then-refresh-then-200, 429 with `Retry-After`); process and browser ports against recording
   doubles.
3. **HTTP surface**: endpoint tests through `WebApplicationFactory` with the out-of-process ports
   doubled and the file classes rooted at a temp directory, covering the origin check and the
   switch and refresh flows end to end inside the process.
4. **Acceptance runbook** (not CI): the Brief's criteria that need the real CLI, real sessions, and a
   real browser (1, 2, 4, 6, 8, 9) as a scripted checklist under `tests/acceptance/`.

Stack per org precedent (medley): xunit v3 on Microsoft.Testing.Platform, Shouldly, NSubstitute
where a double needs call assertions. No mocks of the file system: temp directories are the real
thing and cheap.

## T13. Observability — resolved (light)

`Microsoft.Extensions.Logging` to console at Information; one structured event per switch, refresh,
login step, and refusal, each carrying the email and outcome, never a token. No metrics or tracing
in V1. Switch condition for adding OpenTelemetry: the tool runs unattended (Q27 hook), which V1 does
not.

## T14. Tee `account` field (C10, separate repo) — directional

Writer side only, in `plugins/rate-limit-guard/scripts/statusline-tee.sh`, on the **drain path**
(one elected render every 30 seconds), never on the render path, which stays fork-free. Mechanism
chosen by measurement on this desktop (2026-09-04, 88 KB state file): bash `$(<file)` plus
parameter-expansion extraction took 3.6 to 4.0 s and is rejected; `jq -r '.oauthAccount.emailAddress'`
over stdin took 35 ms and is the choice; `claude auth status --json` took 175 ms and is the
documented alternative. The state file is `$CLAUDE_CONFIG_DIR/.claude.json` when set, else
`$HOME/.claude.json`; bash opens it and jq reads stdin, so the Windows MSYS-path limitation does
not apply. Emitted as a top-level `account: { "email": "<email>" }` object only when the chosen
record's stdin payload carried no `account*` key (a passthrough key always wins) and the state file
is not newer than the chosen record's spool file (`-nt`, a builtin), since `captured_at` is the
observation time of the chosen record and an identity read after a later switch would mislabel the
outgoing account's windows. The reader contract's floor bullet currently says the file carries no
account-identifier field; it is amended at the source and in every registered carrier in the same
PR, with the drift gate proving the copies moved together. Reader-side invalidation of latched
state stays with #1218's follow-up; this change makes the switch visible, which is what the
dashboard needs (AC 10), and the dashboard additionally treats post-switch snapshots whose reset
times still match the outgoing account as unattributed. Switch condition: the
`oauthAccount.emailAddress` key disappears from the state file in a CLI release (then the
`claude auth status --json` alternative takes over).

## T15. Packaging — directional

`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` for V1; the
project also compiles for `linux-x64` and `osx-arm64` in CI without being tested there. No native
AOT in V1 (minimal API plus `System.Text.Json` source generation would allow it later; the switch
condition is startup time or executable size becoming a complaint). Release asset published by a tag
workflow; install is "download, put on PATH, run once".

## Deferred (tagged)

- **D1 [spike 02b]** Is the usage rate bucket per token or per client? Run
  `spike-02-rate-bucket-probe.py` against two parked tokens in one window. Until known, Refresh all
  paces at one read per second and the UI shows lockouts per card.
- **D2 [closed 2026-09-04]** The refresh lock is `<live dir>/.oauth_refresh.lock`, a directory,
  stale at 60 s (read from the installed binary; see T2). Whether the state-file patch is needed at
  all is a new probe (the CLI re-stamps `oauthAccount` itself after a profile fetch); the plan's
  Phase 1.5a settles it before the first real switch.
- **D3 [spike 05b]** Whether the piped code prompt accepts a code (T8). Gates the login mechanism.
- **D4 [Brief Q25, Q26, Q27, Q28]** post-V1, user-reserved or planner-owned as the Brief records.
