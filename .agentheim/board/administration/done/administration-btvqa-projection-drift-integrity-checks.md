---
id: administration-btvqa
title: Shadow-table replay drift detector — verify projection read models exactly match the event log
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-22
depends_on: [administration-qjcp4, design-system-001]
blocks: []
tags: [admin-console, projections, integrity, drift]
related_adrs: [0002, 0024, 0025, 0031]
related_research: []
prior_art: []
---

## Why
Trust in an event-sourced system rests on "the projection is exactly what the
log says." Nothing verifies that today — a bug in a handler, or a missed
catch-up, goes unnoticed until the UI looks wrong. This gives an operator a
"Run check" button on the Projections tab that answers that question directly,
without risking the live read models.

## What
- A throwaway `SqliteConnection` (temp-file or `:memory:`) is created per run.
  For every handler in `Composition.projectionHandlers`, **in registration
  order** (Movie → Friend → ContentBlock → Catalog → Series → Game,
  `Composition.fs:159-166`): `handler.Drop`/`handler.Init` against the shadow
  connection, then a full replay of the live event log (read via
  `EventStore.readAllForward` against the *live* connection, unmodified) into
  `handler.Handle` against the *shadow* connection. Order is load-bearing:
  `FriendProjection`'s `Friend_removed` case scrubs `movie_detail` /
  `watch_sessions` (`FriendProjection.fs:84-138`) and needs those tables to
  already exist. A worker who reorders or parallelizes the shadow loop
  silently reintroduces false-positive drift.
- **No changes to any `*Projection.fs`** — this is the deliberate outcome of
  the architecture decision (see Notes / ADR draft): table names stay
  hard-coded, unmodified handler code runs verbatim against a different
  connection object. Read-only against live data is then true *by
  construction* (the live connection is only ever read from), a stronger
  guarantee than any table-prefix scheme.
- **Diff:** for each table in `Administration.projectionTables`
  (`Administration.fs:219-226`, the existing projection→table-names map
  already used by the Projections tab's row counts), compare shadow vs. live
  rows keyed on each table's known primary key. Report per-projection,
  per-table: rows only-in-live, rows only-in-shadow, and rows present in both
  with differing columns.
- **Gated by the not-dirty guard** (`Administration.isAnyProjectionDirty`,
  ADR-0025): refuse to run — surfaced as a rejection reason, not an exception —
  if any projection is mid-rebuild or lagging, since shadow-at-head vs.
  live-behind-head would report false drift.
- **Transport:** a Giraffe SSE route (`/api/stream/drift-check`, mirroring
  `Administration.projectionRebuildStreamHandler`'s `progress`/`complete`/
  `rejected` framing, ADR-0024) rather than a plain `IAdminApi` call — this
  replays the whole log, the same cost shape as "Rebuild all." Uses its **own**
  single-flight guard (a fresh `ConcurrentDictionary`/flag with the `TryAdd` /
  `finally TryRemove` shape ADR-0024 established) — **not**
  `rebuildingProjections`, whose meaning ("live tables are being written") is
  never true here.
- Results are **display-only**: rendered on the Projections tab, not persisted
  to any table.

## Acceptance criteria
- [ ] Drift check on a healthy in-memory store (seeded via real
      `EventStore.appendToStream` calls, same pattern as
      `ProjectionRebuildTests.fs`) reports zero discrepancies across every
      registered projection.
- [ ] A deliberately corrupted live projection row (test setup: mutate one row
      directly after normal catch-up, bypassing the event log) is detected and
      reported with the correct table / primary-key / column identified.
- [ ] The cross-BC write case is exercised directly: seed a movie
      recommendation from a friend, remove the friend, run drift check — zero
      discrepancies (proves shadow replay reproduces the Friend-removes-from-
      Movie scrub, not just single-projection cases).
- [ ] Live-tables-untouched assertion: after a drift run (including one that
      finds real discrepancies), every live projection table's row count and
      checkpoint position are byte-identical to their pre-run values.
- [ ] Running drift check while a projection is dirty (lagging / rebuilding) is
      rejected with an operator-facing reason, not silently run.
- [ ] The Projections-tab "Run check" control and its result rendering are
      visually consistent with existing Admin console patterns (paper overlay,
      DaisyUI, existing table/list styling on that tab). [human-eye]

## Notes
- **ADR needed before implementation.** Decision (settled during this
  refinement): the shadow replay runs into a throwaway `SqliteConnection`
  (temp-file/`:memory:`), **not** table-name prefixing and **not** literal
  `ATTACH`. Rejected alternatives, with source-grounded reasons:
  - *Table-name prefix (option a):* every `*Projection.fs`'s `handleEvent`
    embeds the table name as a literal string in every `Db.newCommand`
    (`MovieProjection.fs` alone has 15+), not just in `Init` — parameterizing
    "the Init" doesn't touch the handler bodies, so a prefix scheme means
    rewriting all raw SQL across all six files; a single missed string breaks
    the tool silently.
  - *Literal `ATTACH` (option b):* SQLite has no schema search-path;
    unqualified table names in a connection with an attached DB resolve against
    `main` first, so `ATTACH` needs every reference qualified — the same
    invasive rewrite.
  - *Chosen:* `ProjectionRebuildTests.fs` already opens a fresh
    `SqliteConnection("Data Source=:memory:")` and runs the exact, unmodified
    `handler.Init` / replay against it — the mechanism exists and is proven,
    with zero projection-source changes.
  Write as **ADR-0030** — confirm the number is still free at write time (0029
  is the latest as of this refinement). Suggested title: "Drift detector
  replays into a throwaway SqliteConnection, not table-name prefixing or
  ATTACH." Cite `Projection.fs`, the `FriendProjection.fs` cross-write as the
  ordering evidence, `ProjectionRebuildTests.fs` as precedent, and ADR-0024 /
  ADR-0025 as the reused guard patterns.
- **New `Projection.fs` function needed:** a `conn`-decoupled sibling of
  `rebuildProjection` that reads events from one connection
  (`EventStore.readAllForward liveConn …`) and writes via the handler into
  another (`handler.Handle shadowConn event`), skipping checkpoint writes
  entirely — the shadow DB never needs `events` / `projection_checkpoints`,
  only each handler's own owned tables.
- `:memory:` vs. temp-file for the shadow connection is left to the worker
  (not architecturally significant given ADR-0021's event-count numbers).
- **Independent** of the unknown-event report (administration-gxd6e) — no
  shared code or ordering dependency. The drift detector needs no knowledge of
  which event types a handler recognizes; it runs the real, unmodified `Handle`
  function, which does its own internal filtering.

## Outcome

Shipped as designed, with one corrected number: written as **ADR-0031**, not
ADR-0030 (0030 was taken earlier this session by administration-cx92m's
request-connection semaphore gate).

- `src/Server/Projection.fs` — new `replayIntoShadow (liveConn) (shadowConn)
  (handler)`, the `conn`-decoupled sibling of `rebuildProjection`: drop+init
  against `shadowConn`, full replay reading from `liveConn` via
  `EventStore.readAllForward`, no checkpoint writes. Zero changes to any
  `*Projection.fs`.
- `src/Server/Administration.fs` — new drift-detector section:
  `checkProjectionDrift` (the public test seam), `diffTable`/`readRows`/
  `tableColumnInfo` (generic `PRAGMA table_info`-based key detection, no
  hand-maintained PK registry), `DriftDiscrepancy`/`ProjectionDrift` types,
  `driftCheckRejectionMessage` (the not-dirty guard's operator-facing
  wording, also directly unit-tested), `driftCheckStreamHandler` (the SSE
  route, its own single-flight `driftCheckInProgress` guard).
- **Found and fixed a design bug during TDD**: the first draft diffed each
  handler's tables immediately after that handler's own shadow replay —
  wrong, since `FriendProjection` (which scrubs `movie_detail` via a
  cross-BC write) replays *after* `MovieProjection` in registration order.
  The cross-BC acceptance-criterion test caught this: `checkProjectionDrift`
  now replays every handler fully before diffing any table. Recorded in
  ADR-0031's Decision/Alternatives-considered sections.
- `src/Server/Composition.fs` — mounted `/api/stream/drift-check` in the
  `choose` list alongside the other raw SSE routes.
- `tests/Server.Tests/ProjectionDriftTests.fs` (new, 5 tests, all green) —
  healthy store (zero discrepancies), corrupted-row detection (table/PK/
  column identified), cross-BC Friend-removes-from-Movie scrub (zero
  discrepancies), live-tables-and-checkpoints-untouched assertion, dirty-
  projection rejection message.
- `src/Client/Pages/AdminProjections/{Types,State,Views}.fs` — "Run check"
  control + results rendering on the Projections tab: `DriftDiscrepancy`/
  `ProjectionDrift`/`DriftCheckResult` client types, an SSE consumer
  (`runDriftCheckStream`) mirroring the existing rebuild/import stream
  readers, and a `driftCheckSection` card (paper/velvet-card chrome,
  DaisyUI badges for discrepancy kind, matching existing Projections-tab
  patterns).
- `.agentheim/knowledge/decisions/0031-projection-drift-detector-throwaway-shadow-connection.md` (new).
- `.agentheim/contexts/administration/README.md` — new "Drift check" bullet;
  removed a now-stale "feeds the future drift report" forward-reference in
  the Stream drill-in bullet.

Test status: `dotnet run --project tests/Server.Tests/Server.Tests.fsproj
-- --sequenced` — 383/383 passing. The default parallel run intermittently
errors 1-3 pre-existing `JobRunsTests.fs` cases (timing-sensitive under
Expecto's parallel execution) — confirmed pre-existing and unrelated: these
tests pass consistently in isolation, and this task touches no job-related
file. `npm run build` (Fable client compile) green.
