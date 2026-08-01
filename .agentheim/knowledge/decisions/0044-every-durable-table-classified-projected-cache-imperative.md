---
id: 0044
title: Every durable table is classified Projected, Cache, or Imperative in one registry, replacing tribal knowledge encoded as absence
scope: administration
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [administration-t9bzx]
related_research: []
---

# ADR 0044: Every durable table is classified Projected, Cache, or Imperative in one registry, replacing tribal knowledge encoded as absence

> Note on ADR numbering: authored as 0043 in a parallel worker; renumbered to 0044 at integration because ADR-0043 (event-worthiness doctrine) landed first in the same batch.

## Context

`Administration.projectionTables` (ADR-0031) named the fifteen tables owned
by the six checkpoint-tracked projection handlers. Every other durable table
in `mediatheca.db` — `cast_members`/`movie_cast`/`series_cast`/`movie_crew`
(`CastStore.fs`), `game_journal_blocks` (`GameJournal.fs`),
`jellyfin_movie`/`jellyfin_series`/`jellyfin_episode` (`JellyfinStore.fs`),
`settings` (`SettingsStore.fs`), `job_runs` (`Administration.fs`'s own job
recorder), and `game_play_session`/`steam_playtime_snapshot`
(`PlaytimeTracker.fs`) — was classified only **negatively**: scattered
doc-comments explaining why a given check omits them (ADR-0025's
`tableExists` guard comment, ADR-0031's not-dirty guard comment), plus a
defensive existence check at each call site. Nothing in the codebase said
positively what each of these tables *is* or who owns writing it.

That gap is exactly why `game_play_session` could hold 42 rows of
unrebuildable user history for months with nothing flagging it as such: no
registry existed whose absence of an entry would itself be the anomaly.

A parallel workstream (games-p6vkz, captured the same day) plans to retire
`steam_playtime_snapshot` entirely and turn `game_play_session` into a
`Projected` table once play sessions become logged events — a fact this
registry needed to accommodate as a near-term one-line diff, not design
around permanently.

## Decision

### One registry, three classifications

`Administration.fs` gains:

```fsharp
type TableClass =
    | Projected of projectionName: string
    | Cache of refreshedBy: string
    | Imperative of writtenBy: string

let tableRegistry : (string * TableClass) list = [ ... ]
```

covering every durable table in `mediatheca.db` (`events`, `events_fts*`, and
`projection_checkpoints` are event-store/checkpoint schema, owned by
`EventStore.fs`/`Projection.fs` respectively, and are out of scope — they are
not domain tables a classification registry should describe).

- **`Projected`** — checkpoint-tracked, rebuildable from the event log,
  drift-checked by `checkProjectionDrift`. The fifteen tables ADR-0031
  already named.
- **`Cache`** — re-derivable from an external system's own current state via
  a full clear-then-repopulate sync, never checkpoint-tracked, never
  drift-checked. The three `jellyfin_*` tables, refreshed by the Jellyfin
  sync handlers in `Api.fs` (`JellyfinStore.clearAll` followed by a full
  re-populate on every sync run).
- **`Imperative`** — written directly by a named module, outside both the
  projection catch-up path and any external re-sync path. If these rows are
  lost, there is no replay and no re-fetch that brings them back. Covers
  `cast_members`/`movie_cast`/`series_cast`/`movie_crew` (`CastStore`),
  `game_journal_blocks` (`GameJournal`), `settings` (`SettingsStore`),
  `job_runs` (Administration's own recorder), and
  `game_play_session`/`steam_playtime_snapshot` (`PlaytimeTracker`).

### `steam_playtime_snapshot` is classified `Imperative`, with a note that it is really a sync cursor

Strictly, `steam_playtime_snapshot` is neither a projection nor a cache: it
is external state (Steam's own lifetime playtime total) remembered only to
compute the next delta, not derivable from our event log at all today. If a
snapshot row is lost, `PlaytimeTracker.getLastSnapshot` returns `None` and
the entire lifetime total is recorded as one new session — a silent history
distortion, not a recoverable gap. `Imperative "PlaytimeTracker"` is the
closest-fitting classification available today; the registry entry carries a
doc-comment recording that `games-p6vkz` deletes this table outright once
prior playtime and every session are logged events (`ActiveGame.
SteamObservedMinutes` becomes the cursor, derived by replay), closing the
hazard by construction rather than by continuing to guard it. This entry is
expected to disappear in that task's diff — not a stale-registry bug when it
does.

### `projectionTables` is derived, not hand-maintained twice

`projectionTables : (string * string list) list` (the type
`buildProjectionStats`/`checkProjectionDrift` already consume) is now
computed from `tableRegistry` by filtering to `Projected` entries and
grouping by projection name, rather than listed separately. This closes the
gap ADR-0031 deliberately left open in the other direction: ADR-0031 rejected
a hand-maintained PK/column registry because SQLite already declares those
facts (`PRAGMA table_info`) and duplicating them risks drift — but
"which handler owns which table" is *not* a fact SQLite declares anywhere,
so a single hand-maintained source for that fact (now `tableRegistry`,
formerly `projectionTables` directly) is still correct; the fix here is
having exactly one such source instead of a de-facto second one implied by
the "everything else" comments.

### Cache/Imperative row counts get their own surfaced section

`getUnrebuildableTableStats` (`IAdminApi`) is a new, additive Remoting
method returning one row per non-`Projected` `tableRegistry` entry present
in the current schema (`TableName`, `Classification`, `Detail`, `RowCount`),
guarded by the same `tableExists` check `getReferencedImageRefs` uses. It is
deliberately a **separate** method rather than a new field folded into
`getProjectionStats`'s existing `ProjectionStatRow list` return, so the
existing Projections-tab client code needed no changes — client display of
this new data is optional and explicitly deferred to a future task.

## Consequences

### Positive
- A missing table now fails loudly: `TableClassificationTests.fs`'s
  registry-coverage test asserts set-equality between `tableRegistry`'s keys
  and every non-excluded table in a fully-initialized schema, so adding a
  `CREATE TABLE` anywhere without a matching registry entry breaks a test
  instead of silently reproducing the `game_play_session` blind spot.
- `projectionTables` can no longer drift from `tableRegistry` — there is only
  one place that says a table is `Projected` and by whom.
- The near-term `games-p6vkz` diff (retiring `steam_playtime_snapshot`,
  promoting `game_play_session` to `Projected`) is a small, localized edit to
  `tableRegistry` alone; no other module needs to change to reflect it.

### Negative / accepted tradeoff
- `tableRegistry` must be kept in sync whenever a table is added, renamed, or
  reclassified anywhere in the codebase — the same maintenance burden
  `imageRefColumns` (ADR-0025) and the pre-existing `projectionTables`
  already carried, now consolidated into one list instead of scattered
  across several. Mitigated the same way: a doc-comment naming the
  consequence, plus the registry-coverage test.
- `getUnrebuildableTableStats` has no client consumer yet — the data is
  visible only via direct API call or test, not the Projections tab, until a
  follow-up task wires it up.

## Alternatives considered
- **Keep classifying negatively (status quo)** — rejected: this is the
  exact failure mode the task exists to close.
- **Fold Cache/Imperative stats into `getProjectionStats`'s existing return
  type** — rejected: would force a client-side change to every consumer of
  `ProjectionStatRow list` for a feature the task explicitly allows to defer
  client display on.
- **A second hand-maintained registry duplicating `projectionTables`
  alongside a new `tableRegistry`** — rejected: exactly the "two sources of
  schema knowledge that can silently drift out of sync" ADR-0031 already
  ruled out; deriving one from the other is strictly better.

## References
- `src/Server/Administration.fs` — `TableClass`, `tableRegistry`,
  `projectionTables` (now derived), `buildUnrebuildableTableStats`.
- `src/Shared/Shared.fs` — `UnrebuildableTableStat`,
  `IAdminApi.getUnrebuildableTableStats`.
- `tests/Server.Tests/TableClassificationTests.fs` — registry-coverage test,
  derived-`projectionTables` set-equality test, `PlaytimeTracker` explicit
  classification test, `getUnrebuildableTableStats` behavior test.
- ADR-0025 — `imageRefColumns`, the precedent registry style this extends.
- ADR-0031 — `projectionTables`'s original hand-maintained form, and the
  `PRAGMA table_info`-vs-hand-maintained-PK-registry distinction this ADR
  respects rather than blurs.
- ADR-0021 — general posture: admin-owned schema knowledge lives in
  `Administration.fs`, read what the store already materializes.
- games-p6vkz (todo, captured 2026-08-01) — retires `steam_playtime_snapshot`,
  promotes `game_play_session` to `Projected`; the diff this registry's shape
  was chosen to make small.
