# machine-setup-followups

Locked 2026-09-10 by `/planning:interview` (ledger: `.work/machine-setup-followups/interview-checklist.md`;
research: `.work/machine-setup-followups/health-endpoint/RESEARCH.md`). Parent topic:
`docs/topics/claude-subscription-rotation/PLAN.md`.

## Brief

### TLDR

- Land the two Codex P2 fixes on PR #51 (`feat/discover-browser-profiles`) so the browser-profile dropdown merges; keep its grouped-by-browser select as-is.
- Add a repo-level `nuget.config` (clear + nuget.org, mirroring `medley/nuget.config`) so `dotnet restore --locked-mode` works on a machine whose user-level NuGet config has other enabled feeds.
- Tick PLAN item 6.6 (tee `account` attribution) and close Phase 6 as DONE; the code and tests already exist on `main`.
- Replace the hand-rolled `/healthz` route with the framework's `AddHealthChecks()` + `MapHealthChecks("/healthz")`, no checks registered, default writer; pin content type and body in the test.
- After merge: rebuild, reinstall the executable into `~/.local/bin`, restart the running instance on this machine.

### Goal

The dashboard on this machine offers a real list of browser profiles per browser instead of a free-text
directory name, the repo builds from a clean clone on any machine regardless of the developer's
user-level NuGet feeds, the plan no longer reports Phase 6 as open work that is already done, and the
health endpoint is the framework's own middleware rather than a bespoke route, so the implementation
matches the official ASP.NET Core 10 guidance.

### Constraints

- Two branches, both worked in this checkout (no worktree): the P2 fixes go on the existing
  `feat/discover-browser-profiles` (PR #51); everything else goes on one new chore branch from `main`.
- PR #45 (`fix/judge-tier-after-login`) is not touched; its P1 and P2 remain for a separate session.
- No new NuGet package: the health-check middleware comes from the `Microsoft.AspNetCore.App`
  framework reference the App project already has, so the committed lock files do not change.
- No JavaScript test harness is added; the `app.js` fixes are verified by a real-browser drive against
  an isolated instance (temp config, spare port) recorded in the PR body.
- Repo rules hold: PR bodies follow `.claude/rules/pr-body-contract.md`; commits are Conventional
  Commits; `bash eng/check-no-machine-paths.sh` stays clean; no real address or machine path enters
  tracked files.
- Outward actions (push, thread replies, thread resolution, merge) are authorized for both PRs on
  green CI; each is reported as it happens.
- The tee `account` field itself is rate-limit-guard's (closed at 0.8.0 in claude-code-plugins); nothing
  in that repo is touched here.

### Acceptance criteria

- `dotnet restore --locked-mode` exits 0 on this machine with the user-level NuGet config unchanged (its extra enabled feeds present), and CI's `dotnet restore ClaudeCodeAccountRotation.slnx --locked-mode` stays green.
- `docs/topics/claude-subscription-rotation/PLAN.md` shows 6.6 as `[x]` with a dated note, and Phase 6's heading reads `[DONE]`; `DashboardAssemblerTests.PreSwitchWindowsAreUnattributed` and `RateLimitGuardTeeFileReaderTests.ParsesAccountEmailWhenPresent` pass unchanged.
- On the Edit panel of an account whose saved directory is not in the profile catalog, choosing "no browser profile mapped" and saving stores `browserProfileDirectory: null` (verified through `GET /api/dashboard`); the typed directory is not sent.
- On the Add form, after typing an address that auto-selects a profile, choosing "no browser profile mapped" and then editing the address again leaves the selection at "no browser profile mapped".
- Both Codex threads on PR #51 carry a reply naming the fix and are resolved; PR #51's five checks are green; PR #51 is merged.
- `GET /healthz` returns 200, `Content-Type: text/plain`, body `Healthy`, and a `Cache-Control` header containing `no-store`; the health test asserts all four; no `PackageReference` is added and `git status` shows no lock-file change after the build.
- `dotnet build -c Release` has 0 warnings; `dotnet test -c Release` passes with no test lost (baseline 297 total; the health test grows, the #51 branch adds 14).
- On this machine, after reinstall, `claude-code-account-rotation --version` prints the merged commit's version and the dashboard's Add form shows the grouped profile select.

### Captured assumptions

- The tee attribution follow-up is closed by observation: the local `rate-limits.json` now carries `account.email` after a drain, and the live card attributes it. Revisit if a card ever shows "names no account" while sessions are active.
- PR #51 rebases onto `main` with no conflict (one commit behind, `git merge-tree` clean today). Revisit if `main` moves before the merge.
- The health endpoint has no consumer other than the test and manual curl; nothing parses its JSON body (grep of `wwwroot/`, `tests/acceptance/`, `eng/`, `.github/` is empty). Revisit if a launcher script is later written against it.
- Research verification (independence and confidence rows) was dispatched to a fresh-context verifier; its verdict is written back into `RESEARCH.md` when it returns. Revisit if it downgrades a claim the recommendation rests on.

### Out-of-scope

- PR #45's P1 (stale Max block overriding an Enterprise verdict) and P2 (failed re-login deleting an older working pair).
- A dependent dropdown that filters profiles by the chosen browser; a duplicate-address warning across browsers.
- A JavaScript test harness for `wwwroot/app.js`.
- Renaming the health path to `/health`, or adding registered health checks.
- Running the app at logon (#35), the release workflow (#31), and the README install section.

### Deferred questions

- None.

## Plan

Drafted 2026-09-10 by `/planning:plan`. Scale: small (nine files across two branches, every change
surgical). Standards grounding: no standards index exists (rung 4 inference); the surfaces touched are
governed by `REVIEW.md`, `.claude/rules/pr-body-contract.md`, the `eng/dotnet-analysis/` analyzer
posture, and the CI steps in `.github/workflows/ci.yml` (restore `--locked-mode`, build, test, `dotnet
format whitespace --verify-no-changes`, shellcheck, `eng/check-no-machine-paths.sh`). Design:
`design/design-resolution.md` (Tier B early-exit, no new types). Research: the health-endpoint
findings in the topic's memory slice (verified; the decision rests on the HIGH claims E1 to E4, E10a,
E11).

Build technique: none needed. Every piece is a kept slice of committed scope with no viability
unknown; the one contract change (the health response shape) had its consumer check done up front and
found no consumer.

### Phase 1: Land the two Codex fixes on PR #51 and merge it [DONE]

Done 2026-09-10: fix commit `eb443b7` (both threads plus a sibling bug: an empty selection was
looked up as catalog index 0 and flipped the browser select); both threads replied to and resolved;
five checks green; squash-merged as `d615d9d`. Sanity checks: `handPicked` 4, `autoPicked` 0,
read expression 1, `addFields.reset()` 1; browser drive stored `null` on the Edit save and kept the
browser, the cleared Add selection survived retyping, and the next address auto-matched after an
add; `dotnet test`: 311 total, 0 failed, 1 skipped.

Branch: `feat/discover-browser-profiles` (existing; checked out in this checkout, tracking origin).
Review: code (the Codex threads are the review).

Work items, in order:

0. Every `gh` call in this plan runs with `GH_CONFIG_DIR=~/.config/gh-melodic` (the default gh
   identity on this machine is the work account; a local guard rejects the call otherwise).
1. `git checkout feat/discover-browser-profiles` (created from `origin/` on first checkout) and
   confirm `git status` is clean apart from the untracked `docs/topics/machine-setup-followups/`,
   which stays untracked through this phase (surgical staging only, never `git add -A`) and is
   committed on the chore branch in Phase 2's `docs(plan)` commit. The branch is at `e8f4b60`.
2. `src/ClaudeCodeAccountRotation.App/wwwroot/app.js`, inside `accountFields`:
   - **Thread 1 (`read()`):** send the typed directory only when the select is on the escape hatch.
     `browserProfileDirectory: found ? found.directory : (profile.value === OTHER ? (typed.value.trim() || null) : null)`.
     Choosing "no browser profile mapped" on an account whose saved directory was off-catalog then
     sends `null`, so the mapping can be cleared.
   - **Thread 2 (`autoMatch`):** a hand-made choice on the profile select, including "no browser
     profile mapped", is final. Replace the `autoPicked` guess-tracking with one boolean
     `handPicked`, set `true` in the profile `change` handler and consulted first in `autoMatch`
     (`if (handPicked) { return; }`). Every other `autoPicked` reference goes too: the guard line
     becomes the `handPicked` check alone, and the two assignment lines become
     `profile.value = index < 0 ? "" : String(index);` (the file runs under `"use strict"`, so a
     leftover reference would throw on the first typed address). Auto-match still fills an empty
     selection or replaces its own previous guess.
   - **Add-form reset:** `addForm.reset()` clears the DOM but not the closure, so a `handPicked`
     left over from one account would suppress auto-match for the next. Expose
     `reset: function () { handPicked = false; sync(); }` on the returned object and call
     `addFields.reset()` right after `addForm.reset()` in the submit handler's success branch.
   - Keep the comment above `autoMatch` truthful: it now reads that a hand-picked value, empty or
     not, is never replaced.
3. Verify in a real browser against an isolated instance built from this branch. **Seed the config
   first**: a missing `--config` file is created from the real user defaults (`~/.claude`,
   `~/.claude.json`, `~/.claude-profiles`, the real app data), and `--port` overrides only the port,
   so an unseeded run would either collide with the 48211 instance lock or, worse, run against the
   real live directory. Write `<scratchpad>/isolated/config.json` with all four paths under
   `<scratchpad>/isolated/` (`liveConfigDirectory` = `live/`, `stateFilePath` = `.claude.json`,
   `profilesRoot` = `profiles/`, `appDataDirectory` = `appdata/`), mirroring the shape in
   `tests/ClaudeCodeAccountRotation.App.Tests/AppFactory.cs`, and create the three directories. The
   scratchpad sits on the same volume as the home directory and under no sync folder, so the
   validator accepts it. Then
   `dotnet run --project src/ClaudeCodeAccountRotation.App -c Release --no-restore -- --config <scratchpad>/isolated/config.json --port 48222`,
   and via claude-in-chrome on `http://127.0.0.1:48222/`:
   - Edit panel: seed an entry with one `POST /api/accounts` carrying `email`, `browser: "edge"`, and
     an off-catalog `browserProfileDirectory` (mutations need the `X-Claude-Code-Account-Rotation: 1`
     header; a `PATCH` on an unknown address is 404). Open Edit (select shows "another profile" with
     the directory typed), choose "no browser profile mapped", Save; `GET /api/dashboard` shows
     `browserProfileDirectory: null` for that entry.
   - Add form: type an address the catalog knows (auto-selects a profile), choose "no browser profile
     mapped", change the address casing; the select stays on "no browser profile mapped". Submit;
     type a second known address; auto-match fires again (the reset worked).
   Nothing is written under `~/.claude-profiles` or the real app data; the seeded state file names no
   live account, which is fine for these two flows. The same seeded config serves Phase 2's local
   `/healthz` curl.
4. Commit: `fix(dashboard): honor an explicit "no browser profile" choice in the roster form`
   (body names both threads). Run `typos .` and `editorconfig-checker` on the changed file first. No
   lefthook is installed in this checkout and there is no `node_modules`, so the `gitleaks` and
   `markdownlint-cli2` lanes from `.lefthook/base.yml` do not run locally; the PR body says so.
5. Push; reply on both threads (comment ids 3959365655 and 3959365664) naming the fix; resolve both
   through the GraphQL `resolveReviewThread` mutation (thread ids `PRRT_kwDOUPhjfc6gS-qd`,
   `PRRT_kwDOUPhjfc6gS-ql`); append a "Verification (2026-09-10)" paragraph to the PR body with the
   browser walkthrough, naming the flow ("a catalog address"), never the real address typed.
6. Wait for the five checks; `gh pr merge 51 --squash` (the repo's history shows squash merges,
   `(#44)`-style subjects). Report the merge.

**Sanity Check:**

- `grep -c 'handPicked' src/ClaudeCodeAccountRotation.App/wwwroot/app.js` ≥ 3, `grep -c 'autoPicked' src/ClaudeCodeAccountRotation.App/wwwroot/app.js` prints `0`, and `grep -c 'profile.value === OTHER ? (typed.value.trim() || null) : null' src/ClaudeCodeAccountRotation.App/wwwroot/app.js` prints `1`.
- `grep -c 'addFields.reset()' src/ClaudeCodeAccountRotation.App/wwwroot/app.js` prints `1`.
- `jq -r '.profilesRoot' <scratchpad>/isolated/config.json` prints a path under the scratchpad before the isolated run starts.
- Browser drive: `curl -s http://127.0.0.1:48222/api/dashboard | jq '.accounts[] | select(.email=="<seeded>") | .roster.browserProfileDirectory'` prints `null` after the Edit save; `ls ~/.claude-profiles` is unchanged from before the drive.
- `gh api graphql` listing PR 51's review threads shows `isResolved: true` for both; `gh pr view 51 --json state -q .state` prints `MERGED`.
- `dotnet test -c Release` on the branch before push: `failed: 0`, total ≥ 311 (the PR body's count; recorded once as the pin when observed).

### Phase 2: The chore branch: nuget.config, PLAN 6.6, and the framework health endpoint [DONE]

Done 2026-09-11 as PR #53 (`chore/machine-setup-followups`, four commits: build, docs(plan),
refactor(hosting), and a one-line docs(hosting) comment from the architecture review). Sanity checks:
`dotnet restore --locked-mode` with no `--source` exit 0 and one enabled source in the repo config;
6.6 `[x]` 1, `Phase 6: … [DONE]` 1, open `6.x` 0; `MapHealthChecks("/healthz")` 1, `MapGet("/healthz"`
0, `AddHealthChecks` 1, `PackageReference` 0, no lock-file drift; a real Kestrel instance on 48222
answered `200`, `Content-Type: text/plain`, `Cache-Control: no-store, no-cache`, `Healthy`;
`dotnet test -c Release`: 311 total, 0 failed, 1 skipped; build 0 warnings; format, typos,
editorconfig, and the machine-path gate clean. Review: code and architecture lanes, no findings; the
ubuntu CI restore answered the lowercase-filename question; five checks green; Codex completed with
no findings.

Branch: `chore/machine-setup-followups` from `main` (after Phase 1's merge, so it already carries #51
and the one shared file, `AppComposition.cs`, is edited once on top of the merged line).
Review: code + architecture (composition root touched).

Work items, in order:

1. `git checkout main && git pull --ff-only && git checkout -b chore/machine-setup-followups`.
2. **`nuget.config`** (new, repo root, lowercase like `medley/nuget.config`): `<packageSources>` clear +
   `nuget.org`; `<auditSources>` clear + `nuget.org`; `<packageSourceMapping>` clear + `nuget.org`
   with `<package pattern="*" />`; `<disabledPackageSources>` clear. Medley's two-space bytes
   verbatim, LF, final newline (`.editorconfig-checker.json` disables the indent-width check, so the
   gate is on line endings and the final newline). Prove it: `dotnet restore --locked-mode` with no
   `--source` flag exits 0 on this machine (the user-level config's other feeds are still enabled).
3. **PLAN 6.6:** in `docs/topics/claude-subscription-rotation/PLAN.md`, tick 6.6 with a dated note
   (landed with Phase 2's `DashboardAssembler`; the two named tests pass on `main`; on 2026-09-10 the
   work machine's card showed the snapshot attributed to the tee's `account.email` after
   rate-limit-guard 0.8.9's drain), and change the Phase 6 heading tag `[DOING]` to `[DONE]`. Confirm
   6.1 to 6.5 are already `[x]` so the phase is whole.
4. **Health endpoint**, test first:
   - `tests/ClaudeCodeAccountRotation.App.Tests/HealthzEndpointTests.cs`: extend the one test to
     assert `Content.Headers.ContentType.MediaType == "text/plain"`, body `Healthy`, and
     `Headers.CacheControl.NoStore == true`. Run it: red (today's body is JSON).
   - `src/ClaudeCodeAccountRotation.App/Hosting/AppComposition.cs`: `services.AddHealthChecks();`
     beside the other service registrations in `ComposeAsync`; in `MapRoutes` replace
     `app.MapGet("/healthz", …)` with `app.MapHealthChecks("/healthz");` and a two-line comment
     saying it is the framework's liveness probe with no checks registered, so the body is the
     status word and the middleware writes the no-store headers. Run the test: green.
   - No `PackageReference`; `git status` shows no lock-file change after `dotnet build`.
5. **CHANGELOG.md** under Unreleased: a `Changed` bullet for the health endpoint (body and headers)
   and an `Added` bullet for `nuget.config`. Match the existing bullet voice.
6. Local gates: `dotnet build -c Release --no-restore` (0 warnings), `dotnet test -c Release
   --no-build`, `dotnet format whitespace ClaudeCodeAccountRotation.slnx --verify-no-changes`, `typos
   .`, `editorconfig-checker` on the changed files, `bash eng/check-no-machine-paths.sh`.
7. Three commits: `build: pin nuget.org as the only package source`, `docs(plan): close Phase 6, the
   tee account field is live`, `refactor(hosting): serve /healthz through the framework's health-check
   middleware`. Push; open the PR with `/source-control:pull-request` (title `chore: nuget.config,
   Phase 6 close-out, and the framework health endpoint`; body per the contract with `No related
   issue:` and the four sections). Wait for green; squash-merge; report.

**Sanity Check:**

- `dotnet restore --locked-mode` (no `--source`) exit 0 on this machine; `dotnet nuget list source --configfile nuget.config` lists exactly one enabled source.
- `grep -c '^\- \[x\] \*\*6.6\*\*' docs/topics/claude-subscription-rotation/PLAN.md` prints `1`; `grep -c 'Phase 6: .*\[DONE\]' docs/topics/claude-subscription-rotation/PLAN.md` prints `1`; `grep -c '\[ \] \*\*6\.' docs/topics/claude-subscription-rotation/PLAN.md` prints `0`.
- `grep -c 'MapHealthChecks("/healthz")' src/ClaudeCodeAccountRotation.App/Hosting/AppComposition.cs` prints `1`; `grep -c 'MapGet("/healthz"' src/ClaudeCodeAccountRotation.App/Hosting/AppComposition.cs` prints `0`; `grep -c AddHealthChecks src/ClaudeCodeAccountRotation.App/Hosting/AppComposition.cs` prints `1`.
- `grep -c PackageReference src/ClaudeCodeAccountRotation.App/ClaudeCodeAccountRotation.App.csproj` prints `0`; `git status --short -- '**/packages.lock.json'` is empty.
- `curl -si http://127.0.0.1:48222/healthz` on a run with the Phase 1 seeded config prints `200`, `Content-Type: text/plain`, a `Cache-Control` line matching `grep -i 'cache-control:.*no-store'`, and body `Healthy`.
- `dotnet test -c Release`: `failed: 0`, total ≥ 311; `gh pr view <n> --json state -q .state` prints `MERGED`.

### Phase 3: Reinstall the merged build on this machine [DONE]

Done 2026-09-11 on `main` at `e8b5096`: `dotnet publish … -r win-x64` with no `--source` restored
cleanly (no NU1507/NU1801); the RID restore drifted only the two `packages.lock.json` files, reverted
with `git checkout --`; the old instance (built from `03e6d78`) was stopped through PowerShell
`Stop-Process -Force`, `instance.lock` vanished with its handle (delete-on-close), the exe was copied
over `~/.local/bin`, and the new instance started detached from the scratchpad script with an empty
`stderr.log` and `instance.url` rewritten. Sanity checks: `--version | grep -c "$(git rev-parse
HEAD)"` 1 (the bare name resolves through PATH to `~/.local/bin`); `/healthz` 200, `text/plain`,
`Cache-Control: no-store, no-cache`, body `Healthy`; `/api/browser-profiles | jq length` 5 at the
first check and 7 at the fresh-context re-check ten minutes later (two profile directories appeared
on the machine in between; three families, chrome, edge, brave, both times); `git status --short`
empty after the lock-file revert (observed before this mark was written). Browser: the Add form's
"Browser profile" select carries one `optgroup` per family (chrome, edge, brave) around the
discovered profiles plus the ungrouped "no profile" and "another profile" options, read from the DOM
and captured in a screenshot; a fresh-context verifier confirmed the served `app.js` is byte-identical
to the tree's. The ruleset on `main` carries a `pull_request` rule, so this mark's commit home is the
user's call.

Work items, in order:

1. `git checkout main && git pull --ff-only`; confirm both merge commits are present.
2. `dotnet publish src/ClaudeCodeAccountRotation.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64` (no `--source` needed once `nuget.config` is on `main`); then `git checkout -- '**/packages.lock.json'` to drop the runtime-specific lock drift the RID restore writes.
3. Stop the running instance: confirm the PID with `tasklist /FI "PID eq <pid>" /FO CSV` (the
   default table view truncates the image name to 25 characters, so a literal name match fails), then
   `taskkill /PID <pid> /F` (the process is windowless, so a non-forced kill is refused). Copy the new
   exe over `~/.local/bin/claude-code-account-rotation.exe`, start it detached again with the
   scratchpad `start-dashboard.ps1`.
4. Verify: `claude-code-account-rotation --version` prints the new commit hash; `curl -si
   http://127.0.0.1:48211/healthz` shows `Healthy`; `GET /api/browser-profiles` returns this machine's
   profiles; the page's Add form shows the grouped select (screenshot via claude-in-chrome).

**Sanity Check:**

- `claude-code-account-rotation --version | grep -c "$(git rev-parse HEAD)"` prints `1`.
- `curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:48211/healthz` prints `200` and `curl -s http://127.0.0.1:48211/healthz` prints `Healthy`.
- `curl -s http://127.0.0.1:48211/api/browser-profiles | jq length` ≥ 1.
- `git status --short` is empty after the lock-file revert.

## Alternatives considered

| Alternative | Why rejected | Switch condition |
|---|---|---|
| One fresh branch carrying all three pieces, cherry-picking #51 | Discards #51's green CI and review trail; re-reviews 659 lines | PR #51 turns out not to rebase cleanly on `main` |
| Fold nuget.config and the PLAN tick into the #51 branch | A feature PR carrying build config and plan bookkeeping | The user asks for one PR |
| Chore branch from `main` before #51 merges, in parallel | Both branches touch `AppComposition.cs`; a second PR would need a re-run of CI after the first merge anyway | #51's merge is delayed by CI for more than a session |
| Dependent profile dropdown filtered by the browser select | More state to keep consistent with auto-match, exactly where the P2 findings live | A machine with enough profiles that the grouped list becomes hard to scan |
| Node test harness for `app.js` | New toolchain in a repo with none, for two state fixes | A third `app.js` regression |
| Keep the hand-rolled `/healthz` and rename to `/health` | Still a bespoke route the framework already provides; no primary ranks `/health` above `/healthz` | The org adopts Aspire service defaults (`/health` + `/alive`) |
| Remove `/healthz` | Loses the operator's curl-able liveness ping; the instance-lock URL answers "where", not "alive" | A launcher never needs to wait for readiness |
| `packageSourceMapping` omitted from `nuget.config` | Drifts from the org precedent and leaves NU1507 reachable if a second source is ever added | The org precedent changes |

## Risks and mitigations

- **Both PRs edit `AppComposition.cs`.** Mitigated by ordering: the chore branch starts from `main`
  after #51 merges, so there is one edit on top of the merged file and no second CI re-run.
- **`nuget.config` casing on Linux CI.** NuGet probes `nuget.config`, `NuGet.config`, and
  `NuGet.Config`; medley ships lowercase and its CI is green on ubuntu. The CI restore is the check.
- **`--locked-mode` with a new `nuget.config`.** The lock files record package ids and versions, not
  source URLs, so pinning the source does not invalidate them; Phase 2's first sanity check proves it
  before anything else is committed.
- **`AddHealthChecks` namespace.** `AppComposition.cs` already imports
  `Microsoft.Extensions.DependencyInjection`-family types for `AddSingleton`; `MapHealthChecks` lives
  in `Microsoft.AspNetCore.Builder`, already imported. A build error here is a one-line using.
- **Analyzer posture.** The org's strict analyzers may flag the test's header assertions
  (`CacheControl` null-forgiveness); use `ShouldNotBeNull()` before member access.
- **The add-form reset regression** (a `handPicked` flag surviving `form.reset()`) is designed out in
  Phase 1 item 2 and exercised by the second add in item 3.
- **Thread resolution needs the GraphQL mutation**; the branch ruleset blocks merge until both are
  resolved (Kyle's note on #45 records this). Thread ids are captured above.
- **Stopping the running instance** kills a process this session started; `tasklist` confirms the
  image name before `taskkill`, and the restart uses the same script that started it.

## Blast radius

MEDIUM by file count (nine files, two branches), LOW by consequence: every change is `git revert`-able,
CI gates restore, build, test, and format on two operating systems, the health contract change has no
consumer, and `nuget.config` mirrors an org precedent that is green in the medley repository. The
"build configuration" stress-test trigger is matched in letter; the "config tweak with well-understood
behavior" exclusion applies in substance. Formal stress-test: not run; the fresh-context plan review
(Step 3) and the verified research stand in.

## Stress-test summary

Fresh-context plan review (2026-09-10): 1 critical, 3 important, 7 suggestions; all eleven verified
against the code and folded into the phases above. The critical one: the isolated verification
instance must have its config seeded with scratchpad paths first, because a missing `--config` file is
created from the real user defaults (`ConfigurationFile.LoadOrCreateAsync`) and `--port` overrides only
the port. The important ones: every `autoPicked` reference must go (strict mode), the untracked topic
directory's commit home is named (Phase 2's `docs(plan)` commit), and the Edit-panel seed uses `POST`
with the mutation header rather than a `PATCH` on an unknown address. Formal `/planning:devils-advocate`
run: skipped; blast radius assessed LOW by consequence (see above).

## Execution shape

Fully sequential: Phase 1 gates Phase 2 (the chore branch starts from the merged `main` so the shared
`AppComposition.cs` is edited once), and Phase 2 gates Phase 3 (the reinstall needs both merges). Both
branches live in this one checkout by the user's instruction, so there is no parallel surface anyway.

| Phase | Surface | Basis |
|---|---|---|
| 1 | main session | judgment-heavy: browser drive, thread replies, merge |
| 2 | main session | small, TDD, one composition-root edit |
| 3 | main session | stops and restarts a process on the user's machine |

## Open questions

None at approval time.

## Handoff to implementation

### User-approval gates

- None beyond this plan's approval: push, thread replies, thread resolution, and merge were
  authorized in the interview (Q5) for both PRs on green CI; the reinstall and restart in Q6.

### Execution shape ([EXEC-SHAPE] tagged)

- [EXEC-SHAPE] Sequential phases 1 → 2 → 3, all main session (table above).
- [EXEC-SHAPE] Phase 2 branches from `main` after #51 merges rather than in parallel, so
  `AppComposition.cs` is edited once.
- [EXEC-SHAPE] Three commits on the chore branch (build / docs / refactor), one squash-merged PR.
- [EXEC-SHAPE] The `handPicked` boolean plus a `reset()` hook is the shape of the Thread 2 fix.
- [EXEC-SHAPE] `nuget.config` copies medley's four sections verbatim, lowercase filename.
- Approval-round change (2026-09-10): the health test file and class are renamed to
  `HealthEndpointTests` (`git mv`), and the assertions grow. Path: the user chose to follow the
  latest Microsoft guidance for this stack, the ASP.NET Core 10 health-checks article, whose every
  example uses `/healthz`; the path stays `/healthz`. (Aspire's `/health` + `/alive` defaults apply
  to Aspire-hosted apps, which this is not.)

### Mechanical work

- Commit boundaries as listed per phase; the topic's `PLAN.md` phase tags advance `[TODO]` →
  `[DOING]` → `[DONE]` in the same commits as the work.
- Verification checkpoints: each phase's Sanity Check runs green before the next phase starts.
- Sequential fallback: not applicable (already sequential).
