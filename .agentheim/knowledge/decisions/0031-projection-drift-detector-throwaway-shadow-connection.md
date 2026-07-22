---
id: 0031
title: Drift detector replays into a throwaway SqliteConnection, not table-name prefixing or ATTACH
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-btvqa]
related_research: []
---

# ADR 0031: Drift detector replays into a throwaway SqliteConnection, not table-name prefixing or ATTACH

## Context

Trust in an event-sourced system (ADR-0002) rests on "the projection is
exactly what the log says." Nothing verified that — a bug in a handler, or a
missed catch-up, went unnoticed until the UI looked wrong. administration-btvqa
adds a "Run check" control on the Projections tab: for every registered
handler, replay the full live event log into a second copy of its tables and
diff row-by-row against the live tables, without ever writing to the live
tables.

Three designs were on the table for where the shadow copy's tables live:

1. **Table-name prefixing** — give the shadow copy of each table a prefixed
   name (e.g. `shadow_movie_list`) in the *same* connection/database as live.
2. **SQLite `ATTACH`** — attach a second database file/`:memory:` handle to
   the live connection under a schema alias, so `shadow.movie_list` and
   `main.movie_list` coexist on one connection object.
3. **A wholly separate, throwaway `SqliteConnection`** — a second connection
   (`:memory:` or temp-file) with its own unprefixed `movie_list`, etc.,
   created by running each handler's own unmodified `Init`.

Every `*Projection.fs` handler's `handleEvent` embeds its owned table names as
literal strings in every `Db.newCommand` call — not just in `Init`/`createTables`.
`MovieProjection.fs` alone has 15+ such literal-table-name SQL statements
across inserts, updates, and selects (e.g. `handleEvent`'s
`Movie_recommended_by` case reads and writes `movie_detail` by literal name,
`FriendProjection.fs`'s `Friend_removed` case reaches directly into
`movie_detail`/`watch_sessions` by literal name). Options 1 and 2 both require
every one of those call sites to resolve to a *different* table name when
running in "shadow mode" — either a rewritten literal (prefixing) or a
qualified `shadow.movie_list` reference (`ATTACH`; SQLite has no schema
search-path, so an unqualified name in a connection with an attached database
always resolves against `main` first, meaning every reference must be
qualified, not just the ones that happen to collide). Both mean rewriting raw
SQL across all six `*Projection.fs` files, and a single missed string breaks
the tool silently — the corrupted-row detector itself becoming an
undetectable source of false negatives.

`ProjectionRebuildTests.fs` already establishes and exercises the alternative:
open a fresh `SqliteConnection("Data Source=:memory:")` and run the exact,
unmodified `handler.Init`/replay loop against it. The mechanism is proven and
requires zero changes to any `*Projection.fs`.

## Decision

### The shadow copy is a second, wholly separate `SqliteConnection`

`Projection.replayIntoShadow (liveConn) (shadowConn) (handler)` is the
`conn`-decoupled sibling of `rebuildProjection`: it calls `handler.Drop
shadowConn; handler.Init shadowConn`, then reads events from `liveConn` via
`EventStore.readAllForward` (in the same 100-row batches `processBatch` uses)
and calls `handler.Handle shadowConn event` for each one — skipping
checkpoint writes entirely, since the shadow database never needs `events` or
`projection_checkpoints`, only each handler's own owned tables. Every table
name inside `handleEvent` stays exactly as written; "read-only against live"
holds **by construction**, not by convention or a naming scheme that could be
violated by a missed edit — `liveConn` is the only connection `readAllForward`
ever touches, and `shadowConn` is the only connection `handler.Handle` ever
writes to. `Administration.driftCheckStreamHandler` opens the shadow
connection as `SqliteConnection("Data Source=:memory:")` (an unshared,
private, page-cache-backed database — never a temp file on disk, since the
whole point is a throwaway copy that vanishes at the end of the request).

### Replay order is load-bearing, and matches live catch-up's own order

`Administration.checkProjectionDrift` replays every handler into the shadow
connection **in `Composition.fs`'s registration order** (Movie → Friend →
ContentBlock → Catalog → Series → Game) before diffing anything.
`FriendProjection`'s `Friend_removed` case scrubs `movie_detail`/
`watch_sessions` directly by table name — it needs those tables to already
exist (created by `MovieProjection`'s earlier `Init` in the *same* shadow
connection), the identical ordering dependency live catch-up
(`Projection.startAllProjections`) already has. An earlier draft diffed each
handler's tables immediately after that handler's own replay — this is
**wrong**: it compares `MovieProjection`'s shadow tables before
`FriendProjection` has had a chance to apply its scrub, reporting the
about-to-be-corrected `recommended_by`/`want_to_watch_with`/`friends` values as
false drift. All handlers must finish replaying before any table is diffed.

### The diff walks tables via `PRAGMA table_info`, not a hand-maintained PK map

Rather than adding a second hard-coded primary-key registry alongside each
`*Projection.fs`'s own `CREATE TABLE ... PRIMARY KEY (...)` declaration,
`Administration.tableColumnInfo` reads SQLite's own `PRAGMA table_info(table)`
to recover the declared primary-key columns (in composite order) and every
other column. This is the same schema fact the `CREATE TABLE` statement
already encodes, read generically rather than duplicated — a table whose PK
changes needs no matching update anywhere in `Administration.fs`. Rows are
then keyed by their PK tuple and diffed into three discrepancy kinds:
`onlyInLive`, `onlyInShadow`, `columnMismatch` (which also names the
differing columns).

### Gated by the not-dirty guard; its own single-flight guard

The not-dirty guard (`isAnyProjectionDirty`, ADR-0025) is checked before any
replay starts: shadow-at-head vs. live-behind-head would report every lagging
row as false drift, and diffing against a mid-rebuild projection is
meaningless. The check is refused with an operator-facing reason
(`driftCheckRejectionMessage`), not an exception. A second, independent
single-flight `ConcurrentDictionary` guard (`driftCheckInProgress`) — *not*
`rebuildingProjections`, whose meaning ("live tables are being written") is
never true here — refuses a second concurrent drift check, the same
`TryAdd`/`finally TryRemove` shape ADR-0024 established.

### Transport mirrors the rebuild stream's SSE framing

`/api/stream/drift-check` is a raw Giraffe route (not `IAdminApi`/Remoting —
replaying the whole log is the same cost shape as "Rebuild all"), using the
shared `Sse.sseFrame` framing helper and the same `progress`/`complete`/
`rejected` event vocabulary `Administration.projectionRebuildStreamHandler`
established.

## Consequences

### Positive
- Zero changes to any `*Projection.fs` — every handler body runs completely
  unmodified against a different connection object; "read-only against live"
  is a structural guarantee, not a discipline.
- `checkProjectionDrift`/`isAnyProjectionDirty`/`driftCheckRejectionMessage`
  are directly unit-testable without an HTTP context, the same
  test-the-underlying-function shape `ProjectionRebuildTests.fs` established
  for `rebuildProjectionWithProgress` — the SSE route itself stays untested
  (a thin wrapper), consistent with every other SSE handler in this codebase.
- `PRAGMA table_info`-based key detection needs no maintenance when a table's
  schema changes; `projectionTables` (already used by the dashboard's row
  counts) is the only registry reused.

### Negative / accepted tradeoff
- Six full sequential passes over the entire event log per drift check (one
  per handler, unavoidable given the throwaway-connection design) — the same
  cost shape "Rebuild all" already has, and is why this is an explicit
  operator-triggered SSE stream rather than something run automatically.
- The shadow connection's `:memory:` database holds a full second copy of
  every projection table for the duration of one request — bounded by
  ADR-0021's event-count numbers, not architecturally significant at this
  library's scale, but a genuine (if temporary) memory cost.

## Alternatives considered
- **Table-name prefixing** — rejected: requires rewriting 15+ literal SQL
  table references in `MovieProjection.fs` alone (and the equivalent in every
  other `*Projection.fs`), not just `Init`; a single missed string silently
  breaks the tool.
- **SQLite `ATTACH`** — rejected for the same reason: SQLite has no schema
  search-path, so every unqualified reference in a connection with an
  attached database resolves against `main` first, meaning every table
  reference needs qualifying, the same invasive rewrite as prefixing.
- **Diffing a handler's tables immediately after its own shadow replay**
  (interleaved replay-then-diff, one handler at a time) — rejected: proven
  wrong by this task's own cross-BC test (Friend-removes-from-Movie scrub) —
  reports a not-yet-scrubbed `MovieProjection` table as drifted, since
  `FriendProjection` (which performs the scrub) replays later in registration
  order. All handlers must finish replaying before any diffing starts.
- **A hand-maintained primary-key registry per table** (mirroring
  `projectionTables`/`imageRefColumns`'s style) — rejected in favor of
  `PRAGMA table_info`: the primary key is already declared once, in the
  `CREATE TABLE` statement; a second registry would duplicate that fact and
  could silently drift out of sync with a schema change.

## References
- `src/Server/Projection.fs` — `replayIntoShadow` (new), `rebuildProjection`,
  `processBatch`, `ProjectionHandler`.
- `src/Server/Administration.fs` — `checkProjectionDrift`, `diffTable`,
  `tableColumnInfo`, `readRows`, `driftCheckRejectionMessage`,
  `driftCheckStreamHandler`, `isAnyProjectionDirty`, `projectionTables`.
- `src/Server/FriendProjection.fs:78-138` — the `Friend_removed` cross-BC
  write into `movie_detail`/`watch_sessions`, the ordering evidence.
- `tests/Server.Tests/ProjectionRebuildTests.fs` — the proven
  fresh-`:memory:`-connection precedent this design reuses.
- `tests/Server.Tests/ProjectionDriftTests.fs` — the direct-function tests
  (healthy store, corrupted row, cross-BC scrub, live-untouched, dirty
  rejection).
- `src/Server/Sse.fs` — shared `sseFrame` framing helper, reused verbatim.
- ADR-0002 — event sourcing/CQRS: projections as disposable, replayable read
  models, the premise that makes a shadow-replay-and-diff meaningful.
- ADR-0024 — `projectionRebuildStreamHandler`'s SSE framing and
  `rebuildingProjections` single-flight guard shape, mirrored (not reused) here.
- ADR-0025 — `isAnyProjectionDirty`, the not-dirty guard reused verbatim.
