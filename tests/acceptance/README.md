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
