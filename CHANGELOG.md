# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Renamed the project to `claude-code-account-rotation` (#5): the repository, the executable, the
  User-Agent product token, the per-user app data directory, the mutation header
  (`X-Claude-Code-Account-Rotation`), the solution and project names, and the
  `ClaudeCodeAccountRotation.*` namespaces. An existing app data directory named `account-rotation`
  is moved by hand once; see the README's upgrading note.

### Added

- The browser profiles this machine already has are discovered rather than typed. Each
  Chromium-family browser publishes its own profiles in its `Local State` file: the profile
  directory, the name the browser shows for it, and usually the address signed into it. A new port
  and adapter read that for Chrome, Edge and Brave, `GET /api/browser-profiles` returns it, and the
  roster's browser-profile field is a grouped select instead of a free-text box asking for a
  directory name nobody knows. Typing an account's e-mail into the Add form pre-selects the profile
  already signed into that address, and its browser with it, which is the mapping for almost every
  account an operator adds; the selection stays overridable, and a profile the enumeration missed
  is still typeable. Each option names the display name, the signed-in address, and the directory,
  because the first two are what the operator recognizes and only the last one starts a browser:
  Edge writes a profile whose directory is `Default` and whose display name is `Profile 1`. The
  read is read-only and forgiving in every direction: an absent file, an unreadable one, malformed
  JSON, and a file with no profile cache all mean "no profiles for that browser", so a browser the
  operator never installed cannot take down the page for the ones they did.
- Browser-assisted login, so a parked account is logged in from the page instead of from a hand-run
  `/login` in a terminal. Login runs the unmodified CLI under that account's own folder as its
  `CLAUDE_CONFIG_DIR`, captures the sign-in URL it prints, and opens that URL in the browser profile
  the roster maps to the account, with the address already filled in. The operator pastes the
  one-time code into the card; a rejected code is a retry inside the session's ten-minute expiry
  rather than a dead session, and the child process is killed on expiry, on cancel, and at shutdown.
  A login the browser could not be opened for still hands back the URL to open by hand.
- Completing a login rewrites the folder's `profile.json` from the state file that login just wrote,
  and only then prunes the residue, so a folder logged in a second time as a different account can
  never keep the first account's name. The rewrite happens once the CLI has ended, not while it is
  running, and a login whose state file names no account leaves the folder exactly as it is rather
  than pruning away the only copy of its identity.
- The account roster and its page controls: Add, Pause, Remove, and Adopt, so getting the other
  accounts onto a machine never means editing a file or reaching for curl. Add creates the profile
  folder and the card says "needs login"; Pause takes an account out of the ranked queue without
  taking it off the page; Adopt puts the account the machine is already logged in as on the roster;
  Edit remaps an account's alias, browser, and browser profile directory. Only Max accounts join: a
  Team or Enterprise seat is refused with its reason, because that seat exposes no usage buckets and
  is never rotated. An account with no login to read yet joins as "needs login" rather than being
  refused. The roster lives in `roster.json` under app data and is written through the same atomic
  path as every other file the tool owns.
- Removing an account revokes its login by default (`DELETE /api/accounts/{email}?logout=`). A
  deleted folder leaves recoverable bytes holding a refresh token valid for the rest of its 28 days,
  so revocation is what removal means: a failed logout refuses the delete rather than stranding a
  live token, and `logout=false` is the operator's deliberate override, with the response saying
  plainly that the token was not revoked. A folder holding no credential pair skips the logout,
  since there is nothing to revoke. Removing the live account is refused.
- A browser launcher for the Chromium family: the executable resolves from the new
  `browserExecutables` configuration overrides first, then from the platform's known install
  locations for Chrome, Edge, and Brave, and the profile directory and the URL are passed as an
  argument array rather than a command line, so ten accounts get ten browser profiles and no URL is
  ever parsed by a shell.
- Quota reads: the usage response's generic `limits[]` array and `extra_usage` block are parsed into
  card-ready types, so the five-hour, weekly all-models, and any weekly scoped bucket render without
  code changes when Anthropic adds or renames one.
- Live-session quota from the `rate-limit-guard` tee file, the free tier of the refresh contract. A
  card shows a snapshot only when the snapshot names that card's account, so the outgoing account's
  windows are never read as the incoming account's after a switch. The tee path is derived from the
  live config directory, so it follows wherever that is set.
- A per-account refresh budget: six usage reads per five minutes with a sixty-second minimum gap and
  a lockout honored from the endpoint's own `Retry-After`, driven by an injectable clock.
- The two outbound adapters, for the usage endpoint and the OAuth token endpoint, each sending an
  honest User-Agent naming the tool, its version, and its home, with a twenty-second timeout and
  typed failures. The token refresh returns the rotated tokens to its caller and writes nothing.
- Solution skeleton: `ClaudeCodeAccountRotation.Core` (BCL only), `ClaudeCodeAccountRotation.App` (Kestrel on loopback),
  and their test projects under the org's strict analyzer posture.
- Machine-wide account switch: the loopback page lists the live account and every parked profile,
  and Switch parks the live credential pair, unparks the chosen one under Claude Code's own refresh
  lock, patches only the state file's account block, and verifies with `claude auth status`. Every
  open Claude Code session follows on its next request.
- Startup reconciliation: duplicate credential lineages are quarantined under app data, and a switch
  a crash left half done is finished or unwound from the journal.
- Configuration under the per-user app data directory with every path derived at runtime, refusing
  a profiles root on another volume, inside the live directory, or under a sync folder.
- Acceptance runbook and scripts under `tests/acceptance/` for the criteria that need real sessions.

### Fixed

- A swept temporary file is classified by the name it was going to take rather than by parsing it, so
  a write a crash truncated is quarantined rather than deleted: those bytes can be the only copy of a
  rotated refresh token. A 401 refund is remembered for the rest of the window, so a rejected token
  cannot loop against the endpoint without bound. A `Retry-After` is clamped to an hour.
- Files this tool creates are readable by their owner alone on Windows as well as on Unix, and a
  temporary file a crash left behind is swept at startup: one holding a credential pair moves to
  quarantine, where the lineage scan and the operator can both see it, and any other is deleted
  (#10).
- `check-single-holder.sh` fingerprints every dotfile temp beside a credential or state file, not
  just the settled `.credentials.json`, so a crash temp holding a duplicate refresh token can no
  longer hide behind its filename and print a false `duplicates=0`; a temp too damaged to parse is
  counted as `unreadable` and fails the run instead of being silently skipped (#20). The state-temp
  scan follows `CLAUDE_CONFIG_DIR` the same way the app does, so a stale temp left at the home root
  by an old default-root install no longer fails a clean run under a custom config root.
- Two accounts can no longer normalise onto one profile folder. `a<b@x.com` and `a>b@x.com` both
  became `a_b@x.com`, and `user@example.com.` was trimmed onto `user@example.com`, so the second
  account's login overwrote the first account's pair and removing either revoked the other's token.
  The e-mail rule refuses every character that collapsed, and a dot at either end.
- A removal takes the one credential mutation gate, re-reads the live account under it rather than
  before it, and runs the logout and the delete under a cancellation-proof token, so it can no
  longer land inside a switch and leave the pair in neither place. It also refuses while a login is
  running against that folder, which used to leave the "removed" account reappearing with a fresh,
  unrevoked token as soon as the operator pasted the code.
- Two logins can no longer start against one profile folder: the check and the registration are one
  step under the gate rather than a scan followed by a spawn. A second code pasted while the first
  is still being checked is refused instead of overwriting the first caller's completion signal and
  leaving it to wait out its whole reply budget.
- A switch refuses, rather than races, while a login is running against either folder it would
  move. The login's live-account check is also re-read under the gate at the moment the child is
  spawned, so a switch completing in between can no longer leave one account holding two valid
  credential pairs.
- A failing `claude auth status` or `claude auth logout` reports its exit code to the caller and
  sends what the child printed to the log, instead of returning that output in the message the page
  renders.

### Security

- Command injection through the `cmd.exe` shim used for an npm-installed CLI. Arguments were joined
  with a space and no quoting, so an account e-mail carrying `&` was read as a command separator and
  the rest of it ran as a second command. Every argument is now one quoted operand at the shared
  construction point, and the two characters quoting cannot neutralise, a quote and a percent sign,
  are refused there. The account e-mail rule is an allowlist rather than a blocklist as well:
  letters, digits, and `. _ - + @`, with no dot at either end.
- The same-origin check on every mutating route compares the `Origin` header against this instance's
  configured listen address rather than against the request's own `Host`, which the same request
  supplies. `AllowedHosts` is set to the three loopback names, so the framework's host filter refuses
  a rebound name before the pipeline reaches a route.
