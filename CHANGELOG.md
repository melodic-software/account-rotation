# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Solution skeleton: `AccountRotation.Core` (BCL only), `AccountRotation.App` (Kestrel on loopback),
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
