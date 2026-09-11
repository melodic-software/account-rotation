---
outcome: early-exit
tier: C
date: 2026-09-11
---

# Design resolution: judge-tier-after-login

Early exit. The work is a bug fix inside one adapter's finish path plus one static helper's
signature; it introduces no public type, no contract, and no module. The only new state is a
private nullable string on the runner's private `Session` class (the SHA-256 of the credential
file's bytes at start), and the only new signatures are a second public method on the internal
`ParkedFolderAdmission` helper that judges from the CLI status alone and an internal await-finish
hook on the runner for the tests. `MaxTierAdmission` in Core is unchanged, and the roster call
site is as PR #45 left it.
