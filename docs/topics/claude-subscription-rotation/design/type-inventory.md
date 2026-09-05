# Type inventory: account-rotation

Light-form design, 2026-09-04. Naming follows the org's `naming.md`: verbose behavior-naming, no
`Manager`/`Helper`/`Util`/`Service` suffixes, interfaces prefixed `I` (the `dotnet-analysis`
component enforces the prefix and PascalCase). File-scoped namespaces `AccountRotation.Core.*` and
`AccountRotation.App.*`. Records are immutable unless noted. Times are `DateTimeOffset` in UTC;
elapsed time and "now" come from an injected `TimeProvider` (org `overlays/dotnet.md`).

## Core: identity and files

| Type | Kind | Members | Notes |
|---|---|---|---|
| `AccountEmail` | readonly record struct | `string Value` (lowercased, trimmed); `static Result<AccountEmail, string> Parse(string)` | Equality is ordinal on the normalized value |
| `ProfileFolderName` | static class | `static string FromEmail(AccountEmail)` | The Brief's sanitizer: forbidden characters and control characters to `_`, trailing dots and spaces trimmed, empty to `unknown` |
| `OAuthAccountBlock` | sealed record | `JsonObject Raw`; `AccountEmail? Email` (from `emailAddress`); `string? OrganizationRateLimitTier` | Kept as raw JSON so unknown keys round-trip byte-for-byte through park and unpark |
| `CredentialPair` | sealed record | `JsonObject Raw`; `string AccessToken`; `string RefreshToken`; `DateTimeOffset AccessTokenExpiresAt`; `DateTimeOffset? LoginExpiresAt`; `RefreshTokenFingerprint Fingerprint`; `IReadOnlyList<string> Scopes` | In-memory only. No `ToString` over token fields. Only `ICredentialPairStore` and `ITokenRefreshClient` read the token strings |
| `RefreshTokenFingerprint` | readonly record struct | `string Sha256Hex` | What tests and the single-holder check compare; safe to log |
| `ParkedProfile` | sealed record | `AccountEmail Email`; `string FolderPath`; `bool HasCredentials`; `OAuthAccountBlock? Account` | One row per folder under the profiles root |
| `LiveAccountState` | sealed record | `string LiveConfigDirectory`; `string StateFilePath`; `OAuthAccountBlock? Account`; `bool HasCredentials`; `RefreshTokenFingerprint? Fingerprint`; `string? FreshLockFileName` | A snapshot of the live dir taken immediately before planning a switch |

## Core: switching

| Type | Kind | Members | Notes |
|---|---|---|---|
| `SwitchPlan` | sealed record | `AccountEmail? Outgoing`; `string? OutgoingFolderPath`; `AccountEmail Incoming`; `string IncomingFolderPath`; `OAuthAccountBlock IncomingAccount` | The pure output; nothing has moved yet |
| `SwitchRefusal` | enum | `TargetIsLiveDirectory`, `TargetHasNoCredentials`, `TargetHasNoAccountBlock`, `AlreadyOnTarget`, `SharesLiveRefreshToken`, `RefreshLockPresent`, `TargetLoginExpired`, `SwitchingBlockedByManagedPolicy`, `LiveIdentityUnverified` | Expected failures are results, not exceptions (org `architecture-and-design.md`); the last three were added by the plan review |
| `SwitchPlanner` | static class | `static Result<SwitchPlan, SwitchRefusal> Plan(LiveAccountState live, ParkedProfile target, CredentialPair? liveCredentials, CredentialPair targetCredentials, ManagedLoginPolicy policy, bool journalOpen, DateTimeOffset now)` | Every guard from `spike-04-swap.py`, in the same order, then the three review-added refusals |
| `SwitchOutcome` | sealed record | `AccountEmail Now`; `AccountEmail? ParkedAs`; `Result<ClaudeAuthStatus, string> CliVerification`; `bool IdentityMismatchWarning`; `DateTimeOffset At` | Returned by the App's `LiveDirectorySwitch` after executing a plan; a CLI-reported email that differs from `Now` is surfaced, never hidden |
| `SwitchJournal` (App, file) | sealed class | `Task WriteAsync(SwitchJournalEntry)`; `Task<SwitchJournalEntry?> ReadOpenAsync()`; `Task ClearAsync()`; entry = intent, outgoing and incoming fingerprints, folder paths, `StepReached { Planned, Parked, Unparked, Patched }` | `<appdata>/state/switch-journal.json`; reconciled at startup and before every plan from the fingerprints on disk |
| `CredentialMutationGate` (App) | sealed class | `Task<IDisposable> AcquireAsync(TimeSpan timeout, CancellationToken)` over one `SemaphoreSlim(1, 1)` | Every switch, refresh write-back, login completion, and remove runs inside it; permit released in `finally` |
| `InstanceLock` (App) | sealed class | `static Result<InstanceLock, string> TryAcquire(appDataDir, listenUrl)` | Owner-only lock file holding the running URL; a second instance refuses and prints it |
| `ManagedLoginPolicy` (App) | sealed record | `string? ForceLoginOrgUuid`; `string Source` | Read from the platform's managed settings file and, on Windows, the HKLM policy key |

## Core: quota

| Type | Kind | Members | Notes |
|---|---|---|---|
| `LimitKind` | enum | `Session`, `WeeklyAll`, `WeeklyScoped`, `Unknown` | Parsed from `kind`; `Unknown` still renders |
| `UsageLimit` | sealed record | `string RawKind`; `LimitKind Kind`; `string? Group`; `double Percent`; `string? Severity`; `DateTimeOffset? ResetsAt`; `string? ScopeDisplayName`; `bool IsActive` | One `limits[]` entry |
| `ExtraUsageState` | sealed record | `bool IsEnabled`; `string? DisabledReason`; `bool SpendLimitReached` | The usage-credits block |
| `QuotaSource` | enum | `StatuslineSnapshot`, `OnDemandRefresh`, `Cached` | Rendered as "via snapshot", "via refresh", "cached" |
| `UsageSnapshot` | sealed record | `AccountEmail Account`; `DateTimeOffset CapturedAt`; `QuotaSource Source`; `IReadOnlyList<UsageLimit> Limits`; `ExtraUsageState? ExtraUsage` | `SevenDayResetsAt` and `FiveHourPercent` are derived helpers over `Limits` |
| `UsageResponseParser` | static class | `static Result<IReadOnlyList<UsageLimit>, string> ParseLimits(JsonElement body)`; `static ExtraUsageState? ParseExtraUsage(JsonElement body)` | Reads `limits[]` and `extra_usage` only |
| `StatuslineSnapshot` | sealed record | `DateTimeOffset CapturedAt`; `string? SessionId`; `AccountEmail? Account`; `double? FiveHourPercent`; `DateTimeOffset? FiveHourResetsAt`; `double? SevenDayPercent`; `DateTimeOffset? SevenDayResetsAt` | The tee file; `Account` populated once C10 lands. Converted to a `UsageSnapshot` with `Source = StatuslineSnapshot` |
| `UsageReadFailure` | sealed record | `UsageReadFailureKind Kind` (`Unauthorized`, `RateLimited`, `Transport`, `MalformedBody`); `TimeSpan? RetryAfter`; `string Detail` | |
| `RefreshBudget` | sealed class | `RefreshBudget(TimeProvider, int maxReadsPerWindow = 6, TimeSpan window = 5 min, TimeSpan minimumGapSinceLastRead = 60 s)`; `bool TryReserve(AccountEmail)`; `TimeSpan? LockedOutUntil(AccountEmail)`; `void RecordLockout(AccountEmail, TimeSpan retryAfter)` | In-memory sliding window per account |

## Core: routing

| Type | Kind | Members | Notes |
|---|---|---|---|
| `RoutingPolicy` | sealed record | `double SwitchThresholdPercent = 90`; `double EligibleFiveHourMaxPercent = 90`; `double EligibleSevenDayMaxPercent = 100`; `TimeSpan SwitchBackCooldown = 30 min`; `TimeSpan UrgencyWindow = 24 h`; `int QueueLength = 3`; `double AutoRefreshTriggerPercent = 80` | Bound from `config.json` |
| `AccountStanding` | sealed record | `AccountEmail Email`; `bool IsLive`; `bool IsPaused`; `bool HasCredentials`; `UsageSnapshot? Latest`; `DateTimeOffset? LoginExpiresAt` | Input row to ranking; assembled by the App |
| `QueueCandidate` | sealed record | `AccountEmail Email`; `DateTimeOffset? WeeklyResetsAt`; `double FiveHourPercent`; `double SevenDayPercent`; `bool UrgentWeeklyReset` | |
| `AccountRanking` | static class | `static IReadOnlyList<QueueCandidate> Rank(IEnumerable<AccountStanding>, RoutingPolicy, DateTimeOffset now)` | Eligibility filter, earliest-weekly-reset order, truncated to `QueueLength` |
| `SwitchProposalKind` | enum | `ActiveTripped`, `ActiveNearLimit`, `SwitchBackAvailable` | |
| `SwitchProposal` | sealed record | `SwitchProposalKind Kind`; `AccountEmail From`; `AccountEmail To`; `string Reason` | |
| `SwitchAdvisor` | static class | `static SwitchProposal? Evaluate(AccountStanding active, IReadOnlyList<QueueCandidate> queue, RoutingPolicy, DateTimeOffset? lastSwitchBackProposalAt, DateTimeOffset now)` | Applies the 90 threshold and the 30-minute cooldown |

## Core: roster and configuration

| Type | Kind | Members | Notes |
|---|---|---|---|
| `BrowserKind` | enum | `Chrome`, `Edge`, `Brave` | |
| `RosterEntry` | sealed record | `AccountEmail Email`; `string? Alias`; `BrowserKind Browser`; `string? BrowserProfileDirectory`; `bool Paused`; `string? Notes` | |
| `Roster` | sealed record | `int Version = 1`; `IReadOnlyList<RosterEntry> Accounts` | `With`-style helpers for add, pause, remove |
| `AccountRotationConfiguration` | sealed record | `string LiveConfigDirectory`; `string ProfilesRoot`; `string StatuslineTeePath`; `int ListenPort`; `RoutingPolicy Routing`; `RefreshBudgetSettings Refresh`; `TimeSpan RefreshLockWaitBound`; `IReadOnlyDictionary<BrowserKind, string> BrowserExecutables`; `string UserAgentProductToken` | Defaults computed by `ConfigurationDefaults.ForCurrentUser()` at runtime; the shipped template holds no literal path |

## Core: ports (six; interfaces only where T11 justifies one)

| Port | Members | Real adapter (App) | Test double |
|---|---|---|---|
| `ICredentialPairStore` | `Task<CredentialPair?> ReadLiveAsync()`; `Task<CredentialPair?> ReadParkedAsync(folder)`; `Task MoveLiveToParkedAsync(folder)`; `Task MoveParkedToLiveAsync(folder)` (sets the unparked file's mtime to now); `Task<Result<Unit, string>> WriteParkedAsync(folder, CredentialPair, RefreshTokenFingerprint expected)` (compare-and-swap); `Task<IAsyncDisposable> AcquireRefreshLockAsync(TimeSpan waitBound, CancellationToken)` (the CLI's `.oauth_refresh.lock` directory, exclusive create, 60 s stale steal); `string? FreshLockFileName(TimeSpan maxAge)` (secondary guard) | `FileSystemCredentialPairStore` (Windows and Linux) with `OAuthRefreshLock`; Keychain adapter deferred (Q26) | the file adapter itself over a temp directory |
| `IUsageEndpointClient` | `Task<Result<JsonDocument, UsageReadFailure>> ReadUsageAsync(string accessToken, CancellationToken)` | `AnthropicUsageEndpointClient` (typed `HttpClient` from the factory, own User-Agent, 20-second timeout) | fake `HttpMessageHandler` |
| `ITokenRefreshClient` | `Task<Result<RefreshedTokens, UsageReadFailure>> RefreshAsync(string refreshToken, CancellationToken)` | `ClaudeOAuthTokenRefreshClient` | fake `HttpMessageHandler` |
| `IClaudeCliAuthStatus` | `Task<ClaudeAuthStatus?> ReadAsync(string? configDirectory, CancellationToken)` | `ClaudeCliProcessAuthStatus` (`claude auth status --json`, 30-second timeout) | canned |
| `IBrowserLauncher` | `Result<Unit, string> Open(BrowserKind, string? profileDirectory, Uri)` | `ChromiumFamilyBrowserLauncher` | recording |
| `ILoginSessionRunner` | `Task<LoginSession> StartAsync(AccountEmail, string folder, CancellationToken)`; `Task SubmitCodeAsync(LoginSessionId, string code)`; `LoginSessionStatus Status(LoginSessionId)` | `ClaudeCliLoginSessionRunner` (mechanism per T8) | scripted |

## App: concrete file classes (managed dependencies; tested against temp directories, no interface)

| Class | Members | Machine surface |
|---|---|---|
| `ClaudeStateFile` | `Task<OAuthAccountBlock?> ReadAccountBlockAsync()`; `Task PatchAccountBlockAsync(OAuthAccountBlock)` (re-reads before patching; every other key byte-preserved) | `~/.claude.json` or `<CLAUDE_CONFIG_DIR>/.claude.json` |
| `ProfileFolderStore` | `Task<IReadOnlyList<ParkedProfile>> ListAsync()`; `Task<ParkedProfile> EnsureFolderAsync(AccountEmail)`; `Task WriteProfileAsync(folder, OAuthAccountBlock)`; `Task DeleteFolderAsync(folder)`; `Task PruneLoginResidueAsync(folder)` | `<profiles>/<folder>/` |
| `RosterFile` | `Task<Roster> LoadAsync()`; `Task SaveAsync(Roster)` | `<appdata>/roster.json` |
| `UsageSnapshotCache` | `Task<IReadOnlyDictionary<AccountEmail, UsageSnapshot>> LoadAsync()`; `Task SaveAsync(...)` | `<appdata>/state/usage-cache.json` |
| `RateLimitGuardTeeFileReader` | `Task<StatuslineSnapshot?> ReadAsync()` | `~/.claude/rate-limit-guard/rate-limits.json` |
| `AtomicJsonFile` | `Task WriteAsync(path, JsonNode)`: temp file in the target directory, `Flush(true)`, `File.Replace` or `File.Move(overwrite: true)` | every JSON write in the App |

`Result<TValue, TError>` is a small readonly struct in Core (`IsSuccess`, `Value`, `Error`,
`Match`); it is not named `Result<T>` to avoid colliding with framework types (terminology pass).

## App: composition and endpoints

| Type | Kind | Responsibility |
|---|---|---|
| `LiveDirectorySwitch` | sealed class | Takes a `SwitchPlan`, executes park then unpark then patch through the ports, verifies with `IClaudeCliAuthStatus`, returns `SwitchOutcome`. Logs one event |
| `QuotaRefresh` | sealed class | Tier 2 and tier 3: iterates accounts under `RefreshBudget`, handles 401 via `ITokenRefreshClient` once, records lockouts, updates the snapshot cache |
| `DashboardAssembler` | sealed class | Builds `DashboardView` (cards, queue, proposal, live account, adopt-live offer) from roster, profiles, live state, cache, and the tee snapshot |
| `DashboardView`, `AccountCardView`, `LoginSessionView` | records | Response models. No token fields exist on these types |
| `SameOriginMutationFilter` | endpoint filter | T9 origin and custom-header check on every mutating route |
| `ConfigurationDefaults`, `ConfigurationFile` | static / sealed | Compute defaults from the user profile; load, create-from-template, validate |
| `Program` | entry | Composition root: config, DI registrations with explicit service types, Kestrel on loopback, static page from embedded resources, routes |

### Routes

| Method and path | Handler | Notes |
|---|---|---|
| `GET /` | static | embedded `index.html`, `app.js`, `app.css` |
| `GET /api/dashboard` | `DashboardAssembler` | cards, queue, proposal, live account |
| `POST /api/accounts/{email}/switch` | `LiveDirectorySwitch` | 200 with `SwitchOutcome`; 409 with `SwitchRefusal` |
| `POST /api/refresh` and `POST /api/accounts/{email}/refresh` | `QuotaRefresh` | 200 with per-account results including lockouts |
| `POST /api/accounts` | roster add | body `{ email, alias?, browser, browserProfileDirectory? }`; creates the folder |
| `PATCH /api/accounts/{email}` | roster update | pause, alias, browser mapping, notes |
| `DELETE /api/accounts/{email}?logout=true` | roster remove | optional `claude auth logout` under the folder, then delete |
| `POST /api/accounts/{email}/adopt-live` | roster add from the live state file | the post-`/login` case |
| `POST /api/accounts/{email}/login` | `ILoginSessionRunner.StartAsync` | returns session id and the sign-in URL; opens the browser |
| `POST /api/login-sessions/{id}/code` | `ILoginSessionRunner.SubmitCodeAsync` | mechanism (a) only |
| `GET /api/login-sessions/{id}` | status | pending, completed, failed |

## Terminology table

| Term | Chosen | Rejected synonyms | Why |
|---|---|---|---|
| parked profile | `ParkedProfile` | `Profile`, `Slot`, `Account` | "Profile" alone collides with browser profiles; "parked" names the state |
| credential pair | `CredentialPair` | `Credentials`, `Token`, `Auth` | The Brief's word; a pair is two tokens plus expiries |
| switch | `SwitchPlan` / `LiveDirectorySwitch` | `Swap`, `Rotate` | "Rotate" is the product name and implies automation; the Brief says switch |
| live dir | `LiveConfigDirectory` | `ActiveDir`, `Home` | The Brief's word |
| snapshot | `StatuslineSnapshot` (tee) vs `UsageSnapshot` (any source) | `Reading`, `Quota` | Two words for two shapes; "snapshot" alone was ambiguous in the interview |
| queue | `QueueCandidate` / `AccountRanking.Rank` | `Rotation`, `Order` | Brief's word |
| proposal | `SwitchProposal` | `Recommendation`, `Suggestion` | Brief's word |
| refresh | `QuotaRefresh` (usage read) vs `ITokenRefreshClient` (credential refresh) | | Two operations the interview kept distinct |
| browser profile | `BrowserProfileDirectory` | `Profile` | Disambiguated from parked profile |
