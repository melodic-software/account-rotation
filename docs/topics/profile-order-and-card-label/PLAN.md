# profile-order-and-card-label

Decided 2026-09-11 in a decisions interview after the machine-setup follow-ups closed (discovery:
`.work/machine-setup-followups/browser-profile-numbering/EXPLORE.md` and `RESEARCH.md`, memory
tier). Parent topic: `docs/topics/machine-setup-followups/PLAN.md`.

## Brief

### TLDR

- Order discovered browser profiles the way an operator reads them: `Default` first, then `Profile N`
  by the number, then anything else, within each browser; the browser order stays Chrome, Edge, Brave.
- Show the profile's display name on the roster card beside the raw directory, when the catalog the
  page already holds can resolve the pair; the raw directory alone otherwise.
- Close #43: a `browserProfileDirectory` must be a single path segment, and the read-only login-session
  status route says in code why it sits outside the mutation filter.

### Goal

The dashboard stops echoing Chromium's profile-directory counter as if it were an order. Research
settled that the counter gaps (`Profile 4, 6, 7, 8, 9`) are the browser's designed output, that the
app's only coupling to the directory name is the persisted string it hands to `--profile-directory`,
and that vendors themselves decouple the display name from the directory; so the fix is display and
order in the app, with no browser-side change. The `--profile-directory` operand is also fenced to one
path segment so a roster entry cannot point a login at a directory outside `User Data`.

### Constraints

- One branch in this checkout (no worktree), `feat/profile-order-and-card-label` from `main`.
- No JavaScript test harness; `app.js` is verified by a real-browser drive against an isolated
  instance (temp config, spare port), recorded in the PR body.
- The persisted roster contract is unchanged: `browserProfileDirectory` stays the raw directory
  string, and the launcher still passes it verbatim.
- Repo rules hold: Conventional Commits, `.claude/rules/pr-body-contract.md`, `bash
  eng/check-no-machine-paths.sh` clean, no real address or machine path in tracked files.

### Acceptance criteria

- `ChromiumLocalStateProfileReader.ReadAsync` returns, for a `Local State` whose `info_cache` lists
  `Profile 10, Profile 2, Default, Work, Profile 1` in that document order, the sequence `Default,
  Profile 1, Profile 2, Profile 10, Work`; a reader test pins it.
- `POST /api/accounts` and `PATCH /api/accounts/{email}` return 400 with an explanatory `error` for a
  `browserProfileDirectory` containing `/` or `\`, equal to `.` or `..`, or carrying leading or
  trailing whitespace, and store nothing; an endpoint test pins each shape. A plain `Profile 3` and a
  blank value behave as before.
- `LoginEndpoints.cs` carries a comment beside the `GET /api/login-sessions/{id}` route stating why it
  is mapped on `routes` rather than the mutation group.
- On the roster card, an account mapped to a catalog profile shows `browser / name (directory)`; one
  mapped to a directory the catalog does not carry shows `browser / directory`; read from the DOM of
  an isolated instance seeded with a synthetic `Local State`.
- `dotnet build -c Release` 0 warnings; `dotnet test -c Release` passes with no test lost (baseline
  311 total); `CHANGELOG.md` carries the change under Unreleased.

### Captured assumptions

- Chromium serialises `info_cache` keys in an order the app must not depend on (research left it
  open); the sort makes the picker independent of it either way.
- A directory name outside the `Profile N` / `Default` pattern is rare (Brave names none; Edge and
  Chrome name only those) and sorting it last by ordinal is enough.

### Out-of-scope

- Renaming, renumbering, or recreating browser profile directories on the machine.
- A dependent dropdown, a duplicate-address warning, or an ordering of the roster cards (#47).
- PR #45's findings.

## Plan

### Phase 1: Order, card label, and the #43 fence [TODO]

Work items, in order:

1. Reader test for the natural order (red), then the comparer in `ChromiumLocalStateProfileReader.Profiles` (green).
2. Endpoint Theory for the refused directory shapes (red), then the single-segment check shared by POST and PATCH in `RosterEndpoints` (green); the why-comment on the login-session status route.
3. `app.js`: the card line resolves the pair through `indexOfProfile` and prints `name (directory)` when found.
4. `CHANGELOG.md` entries; browser drive against an isolated instance for the card line; `/verification:confirm`.

Sanity check: `dotnet test -c Release` green with the new tests counted; the DOM read shows both card shapes.
