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
