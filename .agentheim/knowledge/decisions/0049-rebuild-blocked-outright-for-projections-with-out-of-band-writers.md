---
id: 0049
title: Rebuild is blocked outright for projections with out-of-band writers
scope: administration
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [administration-kv7dp]
related_research: []
---

# ADR 0049: Rebuild is blocked outright for projections with out-of-band writers

> Note on ADR numbering: authored as 0043 in a parallel worker (batch 1, 2026-08-01); renumbered to 0049 at its conflict-delayed integration because ADRs 0043-0048 landed first.

## Context

`Projection.rebuildProjection` (`Drop; Init; replay`) assumes a projection's
entire state is derivable from its own event replay — true by construction
for every handler except one. `SeriesRefresh.applyToProjection` writes TMDB
refresh results directly into `series_list`/`series_detail`/
`series_seasons`/`series_episodes`; `Series_refreshed` carries only a
summary (`refreshedAt`, `newEpisodeCount`, `previousStatus`, `newStatus`),
and `SeriesProjection.handleEvent`'s arm for it is an explicit no-op.
ADR-0012's Jellyfin season/episode materialization writes the same tables
the same out-of-band way. Today, pressing "Rebuild" on SeriesProjection (or
"Rebuild all", which drives the same SSE route sequentially per ADR-0024)
permanently destroys 780 refreshes' worth of episode/season metadata across
38 series and 23 Jellyfin-materialized episodes plus 5 Jellyfin seasons —
recoverable only by re-running every TMDB refresh and the Jellyfin sync, and
the button is live in Settings > Projections right now.

The root cause — events that should carry this state but don't — is a
larger refactor tracked separately (`infrastructure-e4kwm`'s event-worthiness
doctrine and the workstream it belongs to, including `series-d5tpn`). This
hazard is entirely separable and must not wait for that refactor: it is a
live footgun today.

## Decision

### Block outright, don't reuse ADR-0034's confirm-modal guardrail

Rebuild is refused server-side for any projection registered in a new
`Administration.lossyRebuildProjections : (string * string) list`
(`(handlerName, reason)` pairs) — one entry, `"SeriesProjection"`, today. The
reason string names both what is destroyed and the recovery path (run the
Series TMDB refresh job from the Jobs section; reload the app to trigger a
Jellyfin sync). `Administration.lossyRebuildRejectionMessage : string ->
string option` is the pure lookup, with a `MEDIATHECA_ALLOW_LOSSY_REBUILD=1`
env override — server-side only, no UI, greppable, self-evidently temporary.

ADR-0034's `VACUUM INTO` backup + preview + explicit-confirm-modal protocol
(reused as-is by ADR-0038's wipe-import) is deliberately **not** reused here.
That protocol protects the *event store* — a `VACUUM INTO` backup and a
confirm dialog make sense when the artifact at risk is the append-only log
itself, restorable from the backup file. Here the loss is entirely in
*projection tables* — read models by definition disposable and rebuildable
(ADR-0002) for every OTHER handler — and hand-restoring a `.db` file is not a
recovery path a single-user app's operator reaches for. A confirm modal
would also be a **frontend** task: it would inherit the design-system
styleguide gate (ADR-0015, this BC's frontend-task rule), and it would be
permanent UI built to serve a condition this ADR commits to deleting (see
Retirement below). An outright, no-UI, server-side block needs none of that
— the client's existing non-fatal `Rebuild_rejected` handler
(`AdminProjections/State.fs`) already renders any `rejected` SSE event as a
transient toast, so this ships with **zero client changes**.

### Ordering: checked before the wipe-import and single-flight guards

`projectionRebuildStreamHandler`'s guard checks now run in this order for a
known projection name:

1. **Lossy-rebuild guard** (this ADR) — claims nothing on either guard
   dictionary, so it must run first: it would be wrong for a lossy-blocked
   request to still claim `RebuildingProjections` or read
   `wipeImportInFlight` first.
2. Wipe-import-in-flight (ADR-0038).
3. Single-flight `RebuildingProjections.TryAdd` (ADR-0024).

Factored out as `Administration.decideAndClaimRebuildGuard : AdminGuards ->
string -> RebuildRejection option`, mirroring `decideAndClaimWipeImportGuard`
— the same "test the underlying function, not the SSE route" seam ADR-0031
and ADR-0038 established, so the order (load-bearing) is unit-testable
without spinning up SSE/HTTP.

### The second call site: `CinemarcoImport.fs`'s post-import rebuild loop

`CinemarcoImport.runImport`'s step 6 (`for handler in projectionHandlers do
Projection.rebuildProjection conn handler`) is an easily-missed second call
site with the identical hazard, reached only when importing into a
fresh/empty event store (checked earlier in the same function) — so falling
back to `Projection.runProjection` (incremental catch-up from checkpoint 0)
still lands every just-imported event; it simply never drops tables an
out-of-band writer may already have populated. The rejection reason is
`eprintfn`'d to the server console (this path has no SSE/HTTP transport of
its own).

Referencing `Administration.lossyRebuildRejectionMessage` from
`CinemarcoImport.fs` required reordering `Server.fsproj`'s `<Compile>` list:
`Administration.fs` now compiles before `CinemarcoImport.fs` (previously
after), which in turn still compiles before `Api.fs` (which calls
`CinemarcoImport.runImport`). Verified safe: neither `Administration.fs` nor
`Api.fs` has any real (non-comment) dependency on the other.

### `Projection.fs` stays free of admin-console knowledge

The guard is **not** inside `Projection.rebuildProjection` itself.
`Projection.fs` has no admin-console-specific knowledge today (it doesn't
know what "SeriesProjection" out-of-band writers exist) and must not gain
any — the Expecto call sites across the test suite (e.g.
`ProjectionRebuildTests.fs`'s own direct exercises of
`rebuildProjectionWithProgress`) must keep rebuilding freely, unblocked, the
same way `Projection.fs` stays ignorant of `projectionTables`,
`boundedContextPrefixes`, and every other admin-console-only registry in
this BC.

### Retirement criterion (this mechanism is built to be deleted)

Remove a projection's entry from `lossyRebuildProjections` when
`checkProjectionDrift` reports 0 discrepancies for it **and** no module
outside its own `*Projection.fs` writes any table classified `Projected` for
it. When the list empties, delete this mechanism entirely and mark this ADR
superseded. `series-d5tpn` is the task that executes that retirement for the
one entry above.

## Alternatives considered

- **Reuse ADR-0034's `VACUUM INTO` + confirm-modal protocol.** Rejected —
  see "Block outright, don't reuse ADR-0034's confirm-modal guardrail" above:
  wrong artifact (event store vs. projection tables), wrong recovery path
  (file restore vs. re-run the refresh job), and it would be permanent UI
  built to serve a condition this ADR is committing to delete.
- **Guard inside `Projection.rebuildProjection`.** Rejected — would leak
  admin-console-only knowledge into the generic replay engine and would
  block the Expecto suite's own direct rebuild exercises.
- **Leave `CinemarcoImport.fs`'s call site unguarded** (it's a fresh-database
  import, so "loss" is less obviously live). Rejected by the task's explicit
  scope — an out-of-band writer having already populated a table before this
  loop runs is exactly the scenario the guard exists for, and skipping this
  call site would leave the identical hazard reachable from a second,
  easily-missed path.
- **Inline the guard check in `projectionRebuildStreamHandler` without
  extracting `decideAndClaimRebuildGuard`.** Rejected for testability — the
  ordering relative to the wipe-import and single-flight checks is
  load-bearing, and the project's established "no SSE-handler-level test"
  convention (ADR-0029/ADR-0031/ADR-0038 precedent) means the only way to
  verify that ordering is to extract it as a plain function, exactly as
  ADR-0038 did for `decideAndClaimWipeImportGuard`.

## Consequences

### Positive
- The live data-loss hazard (the button already shipped) is closed with a
  server-side change and zero client changes, verified by `git diff --stat
  src/Client/` staying empty.
- The guard's ordering relative to the two pre-existing rejection arms is
  directly unit-tested, not left as an untested inline `if/elif` chain.
- The mechanism names its own deletion condition in the Decision section, so
  a future maintainer doesn't need to infer whether it's safe to remove.

### Negative / accepted tradeoffs
- `MEDIATHECA_ALLOW_LOSSY_REBUILD=1` is a deliberately un-discoverable escape
  hatch (no UI, no documented operator-facing setting) — acceptable because
  its only legitimate use is a developer deliberately overriding the guard
  during the future refactor that removes the root cause, not a normal
  operator action.
- `Server.fsproj`'s compile order for three files (`Administration.fs`,
  `CinemarcoImport.fs`, `Api.fs`) is now load-bearing on a cross-module
  function call that didn't exist before — a future edit adding a real
  dependency in the other direction would need to re-thread this ordering
  again.

### Neutral
- `lossyRebuildProjections` sits next to `projectionTables` as one more
  "admin-console-only knowledge of the schema" registry (ADR-0025's
  precedent) — a missed entry silently under-guards rather than
  over-guards, the same dangerous-failure-mode shape ADR-0025 already
  accepted for its own registry, mitigated the same way: a doc comment plus
  the retirement criterion keeping the list's contents actively reviewed
  rather than write-once-forget.

## References

- `src/Server/Administration.fs` — `lossyRebuildProjections`,
  `lossyRebuildRejectionMessage`, `RebuildRejection`,
  `decideAndClaimRebuildGuard`; `projectionRebuildStreamHandler`'s
  three-rejection-arm dispatch.
- `src/Server/CinemarcoImport.fs` — `runImport`'s step 6 fallback to
  `Projection.runProjection` for lossy-guarded handlers.
- `src/Server/Server.fsproj` — `Administration.fs` now compiles before
  `CinemarcoImport.fs` (before `Api.fs`).
- `tests/Server.Tests/ProjectionRebuildTests.fs` — `lossyRebuildRejectionMessage`
  coverage (default block, unguarded projection, env override), the rejected
  request leaving `series_episodes` untouched, and the six-handler
  "Rebuild all skips SeriesProjection" sequencing test.
- ADR-0002 — projections as disposable, rebuildable read models; the
  invariant this ADR documents as broken for SeriesProjection specifically.
- ADR-0012 — Jellyfin materializes missing seasons/episodes as a
  projection-only supplement; the second out-of-band writer this ADR
  guards against.
- ADR-0015 — the frontend design-system gate a confirm-modal alternative
  would have inherited.
- ADR-0024 — `projectionRebuildStreamHandler`'s existing single-flight guard
  and "Rebuild all" client-side sequencing this ADR's guard is ordered
  ahead of.
- ADR-0025 — `isAnyProjectionDirty`'s not-dirty guard and the
  admin-console-only registry precedent (`projectionTables`) this ADR's
  registry follows.
- ADR-0031 — `checkProjectionDrift`'s "test the underlying function, not
  the SSE route" seam, and the retirement criterion's dependency on it
  reporting 0 discrepancies.
- ADR-0034 — the event-surgery guardrail protocol this ADR explicitly does
  not reuse, and the reasoning why.
- ADR-0038 — `decideAndClaimWipeImportGuard`/`wipeImportInFlight`, the
  precedent this ADR's `decideAndClaimRebuildGuard` extraction follows.
- `series-d5tpn` — the task that executes this mechanism's retirement.
