# Design resolution: machine-setup-followups

outcome: early-exit
tier: B (2 to 5 files per piece, no new type, one localized contract tweak)
date: 2026-09-10

## Reason

No piece of this topic introduces a type, a module, a package, or a cross-module integration:

- The `app.js` fixes change two branches of existing form state inside `accountFields` (what
  `read()` sends when the select is empty, and whether a hand-made "no profile" choice survives a
  later auto-match). No new DOM structure, no new endpoint.
- `nuget.config` is a copy of the org's `medley/nuget.config` shape (clear + nuget.org for sources,
  audit sources, and source mapping). No project file changes.
- The PLAN 6.6 tick is documentation.
- The health endpoint swap replaces one `MapGet` with the framework's `AddHealthChecks()` +
  `MapHealthChecks("/healthz")`. The only contract change is the response shape (JSON
  `{"status":"ok"}` becomes `text/plain` `Healthy` with no-store headers), and the pre-flight consumer
  check found no consumer of the body (grep over `wwwroot/`, `tests/acceptance/`, `eng/`, `.github/`
  is empty; the one test asserts the status code only).

## Type sketch

None. Every type touched already exists: `AppComposition` (static composition root), the anonymous
form-field closure in `app.js`, and `HealthzEndpointTests`.
