# Deviations: usage-cards

Append-only log of where implementation departed from `PLAN.md`, one decision per entry, each
with its evidence. Types: plan-confirmed, discovery, deviation (plan said / found / chose /
revisit), human-decision.

## Phase 1, sub-brief (a): harness and Core additions

- discovery: `SwitchEndpointTests.AnExpiredParkedLoginYieldsAConflict` built its expired login from
  the wall clock, so registering the test clock as the DI `TimeProvider` made it pass a switch it
  should refuse. Fixed by deriving the instant from the factory clock (`d0f6171`). Every later test
  derives host-visible instants from the clock; `CredentialFiles.Shape` defaults still come from
  the wall clock and read as "not expired" under the frozen host.
- discovery: `FileSystemCredentialPairStore` stays on its explicit `TimeProvider.System`; its
  refresh lock polls `Task.Delay` against `GetUtcNow` and never terminates under a frozen clock.
- plan-confirmed: `SwitchPlanningInput` has one construction site, not two; the defaulted trailing
  parameter compiles unchanged (`f5e8288`).

## Phase 1, sub-brief (b): engine, worker, recovery, DI

- deviation: plan said NSubstitute fakes for the two ports / found NSubstitute is pinned centrally
  but referenced by no test project, so adding it needs a project-file edit and regenerated lock
  files / chose hand-rolled scriptable doubles in
  `tests/ClaudeCodeAccountRotation.App.Tests/Quota/RefreshDoubles.cs` (`448d927`) / revisit: none.
- deviation: plan said `RecoveryFiles` takes the store and the app-data path / found a restore
  warning would otherwise have to name a folder path / chose to inject `ProfileFolderStore` so the
  warning names the account from the folder's identity file (`9359279`) / revisit: none.
- deviation: plan said `ReadFailed` "with the status only" / found the usage adapter's transport
  detail interpolates the exception message, which can carry a host / chose curated constants on
  the card and the detail on the log line only (`9359279`) / revisit: none.
- discovery: two cases the plan did not name. A token-host 401 maps to `TokenRefreshFailed`; a
  second 401 after a successful rotation maps to `ReadFailed` with a fixed "log this account in
  again" message and no further retry (`QuotaRefreshTests`, `448d927`).
- deviation: plan said the adapter factories resolve `TimeProvider` from the provider / found
  `OutboundClientLoggingTests` builds a container from `AddOutboundClients` alone and had no
  `TimeProvider` / chose `services.TryAddSingleton(TimeProvider.System)` inside the helper; the
  composition root's registration and the test factory's replacement still win (`9359279`) /
  revisit: none.
- deviation: plan said one commit per green checkpoint (scaffold, engine, recovery, DI) / found
  the pieces only compile together / chose two commits, engine then tests (`9359279`, `448d927`)
  / revisit: none.
- deviation: plan said TDD one test at a time / found the worker wrote the engine first and the
  tests after, iterating to green / chose to substitute a mutation spot-check on four load-bearing
  facts (restore, lockout floor, under-gate fingerprint compare, spacing), each turning its named
  test red when broken / revisit: the phase verifier and the review lanes check the tests for
  vacuity rather than trusting the red step.
- deviation: plan said `BudgetRefused` reports "read N s ago" from `GapRemaining` / found
  `RefreshBudget` exposes no last-read instant / chose to derive it from the account's latest
  snapshot capture time, and a window-cap refusal (no gap) reports "the read budget for this
  account is spent for now" (`9359279`) / revisit: none.
- discovery: a 401 on the live pair took a budget reservation and never refunded it, so the live
  account's next read waited the 60-second gap for a read that never happened. Sub-brief (c)
  refunds it through `RecordUnauthorized`, the same accounting the parked path uses; the plan's
  per-account sequence named the refund only for the parked case.

## Phase 1, sub-brief (c): views, assembler, routes

- deviation: plan said register the worker as a hosted service / found `AddHostedService<T>` alone
  leaves the worker unresolvable for the route's `TryStart` / chose one singleton plus a hosted
  registration over that instance (`007e75e`) / revisit: none.
- deviation: plan said `AccountCardView.Refresh` defaults to idle / found a record parameter
  default must be a compile-time constant / chose a required positional with both roster call
  sites passing `RefreshStateView.Idle` (`007e75e`) / revisit: none.
- plan-confirmed: `RosterEndpoints` constructs a card at two sites, not three; PATCH returns a
  bare roster view.
- deviation: plan said credits come from the latest snapshot / found the tee never carries
  credits, so the literal rule would blank the line on every statusline write / chose the newest
  source that carries `ExtraUsage`, the same rule as the per-bucket merge (`007e75e`) / revisit:
  none.
- deviation: plan said the endpoint test uses the spike-01 fixture file / found adding a content
  include means a project-file edit outside the fence / chose the fixture's `limits[]` and
  `extra_usage` blocks copied verbatim as a constant in `RefreshEndpointTests` (`007e75e`) /
  revisit: none.
- deviation: plan said TDD one test at a time / found the view-shape change breaks compilation
  repo-wide so no test could run red against the old shape / chose implement-then-test with six
  mutation spot-checks, each killing only its named tests (live-401 refund, per-bucket precedence,
  window-reset rule, stranded override, lockout refusal, same-origin filter) / revisit: the phase
  verifier and review lanes check for vacuity.
- discovery: the overlap 409 is driven by `QuotaState.TryBeginRun()` directly, the exact predicate
  `TryStart` refuses on, because a started pass finishes before a second request can arrive.
- discovery: one same-origin theory started a real pass with nothing scripted; the unscripted
  read threw inside the handler and the engine's per-account catch recorded a read failure, so
  the theory passed while spending the no-network guarantee. Fixed by scripting the read and
  asserting one outbound request (`48515fd`). Worth knowing: the engine's catch-all means a
  test's harness error surfaces as `ReadFailed`, not as a test failure.

## Phase 1, sub-brief (d): page, acceptance script, changelog

- discovery: `check-single-holder.sh` reported SC2310 at baseline too (the rule is enabled in
  `.shellcheckrc`; CI runs shellcheck on `eng/*.sh` only). A per-line disable with its reason now
  sits on the intended `set -e` suppression (`60f7e1c`).
- plan-confirmed: no statusline-tee config key exists; the tee path derives from the live
  directory, so a throwaway instance's tee follows its temp live directory.
- deviation: plan said "refreshing…" for a card whose outcome is not from this pass / found the
  payload carries no per-outcome timestamp / chose to show it only while a pass runs and the
  card's state is `idle`, so during a single-card refresh every other never-read card also reads
  "refreshing..." (`84863cb`) / revisit: expose the outcome's `recordedAt` on the wire if that
  reads wrong in use.
- discovery: the script's default app-data branch was exercised read-only on this machine
  (`recovery=0`); nothing under the real app data was written.
