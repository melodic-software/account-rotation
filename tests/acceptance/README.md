# Acceptance runbook

The Brief's criteria that need the real CLI, real sessions, and a real browser. Run these by hand on
the desktop with the user present; record each pass in the log at the bottom. Nothing here runs in
CI.

## Before the first real switch: the state-file patch probe (plan item 1.5a)

1. Set `"patchStateFile": false` in `config.json` and start the tool.
2. Open three Claude Code sessions on the live account.
3. Click Switch to a parked account, then send one message in each session.
4. Check `claude auth status --json | jq -r .email` and `/status` in every session.
5. If all four report the incoming account, the CLI re-stamps `oauthAccount` itself: set the default
   to `false` in `ConfigurationDefaults`, record the outcome under 1.5a in `PLAN.md`, and skip the
   state-file assertions below. If any still reports the outgoing account, keep `true` and record that.

## AC 1, switch without a browser, and AC 9, refresh lock respected

1. Three sessions open on account A; note the statusline `acct:` badge in each.
2. On the page, click Switch to account B. Expect the toast `Switched to B; parked A`.
3. Send one message in each of the three sessions; every `/status` shows B, and
   `claude auth status --json | jq -r .email` prints B. No browser opened, no session restarted.
4. `bash tests/acceptance/check-single-holder.sh ~/.claude-profiles ~/.claude` prints `duplicates=0`.
5. Create the lock directory `~/.claude/.oauth_refresh.lock`, click Switch, and expect the 409 toast
   `A session is refreshing its token right now`; remove the directory.

## AC 2, in-flight work survives

1. In one session, start a subagent fan-out (three parallel subagents) on account A.
2. While it runs, click Switch to B.
3. The fan-out completes without an error, and the parent's next message bills B.

## State-file drift probe

After a switch, run several turns in a session opened before the switch, then start and stop a fresh
session. `jq -r .oauthAccount.emailAddress ~/.claude.json` still prints the incoming account.

## Live identity check (once per acceptance pass)

`bash tests/acceptance/check-live-identity.sh` prints `billed_email=<incoming account>`. It makes one
honest-User-Agent read of the OAuth profile with the live access token and prints nothing else.

## Log

| date | machine | criteria | outcome | notes |
|---|---|---|---|---|
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | 1.5a probe | keep the patch | the pair moved and billing followed on the next request; `oauthAccount` and `claude auth status` kept the outgoing account after nine minutes; the next refresh rotated the token so `live-owner.json` went stale; recovered through journal reconciliation with the patch on; `duplicates=0` before and after |
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | AC 9 | pass | with `.oauth_refresh.lock` held by hand the switch waited 10 s and refused 409 `RefreshLockPresent`; nothing moved; a second click during the wait got 409 `MutationInProgress` at once (the page could disable the button while a switch is in flight) |
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | AC 2 | pass | three parallel subagents started in one session; the switch ran 3.6 s later (200, CLI verified the incoming account, no mismatch); all three finished their reads and reported without an error; `duplicates=0`; the live refresh token rotated again within seconds of the unpark, so `live-owner.json` was stale at once |
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | AC 1 | pass | three sessions open; switch through the page's API returned the toast payload in 3.6 s; `/status` in every session showed the incoming account without a restart or a browser, even before the next message (it reads the patched state file); `claude auth status --json` and `.oauthAccount.emailAddress` both named the incoming account five minutes later; `duplicates=0` |
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | live identity | pass | `check-live-identity.sh` printed `billed_email=<incoming account>` after the AC 1 switch |
| 2026-09-06 | desktop, Windows 11, CLI 2.1.263 | state-file drift | pass | after the probe's recovery patch the state file kept the patched account for eight hours while three sessions started before the switch kept working and the CLI rewrote the file repeatedly; five minutes after the AC 1 switch it still named the incoming account; fresh-session start and stop then re-read: see the next row |
