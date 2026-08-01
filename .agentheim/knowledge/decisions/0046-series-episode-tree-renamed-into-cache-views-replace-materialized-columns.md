---
id: 0046
title: Series season/episode tree renamed into the cache tier; SQL views replace materialized next-up/count columns
scope: series
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [series-m7fdk]
related_research: []
---

# ADR 0046: Series season/episode tree renamed into the cache tier; SQL views replace materialized next-up/count columns

## Context

`series_episodes`/`series_seasons` held 161 and 21 rows respectively that exist **only** in the live
tables — produced by TMDB refreshes (`SeriesRefresh.fs`) and Jellyfin materialization
(`integration-m4k7p`), never by the event log. The log's `Series_added_to_library` snapshot and the
live tables' post-refresh/post-materialize state are two independent sources for the same rows, which
guarantees permanent drift the instant either source moves without the other (ADR-0043's doctrine, and
the tier ADR-0045 built to hold exactly this kind of row).

`series-m7fdk` is the Series-chain first mover in that workstream: rename the season/episode tree into
the cache tier ADR-0045 built, with zero data movement, and replace the two materialized columns
(`series_list.next_up_*`, `*.episode_count`/`season_count`) whose upkeep depended on `SeriesProjection`
owning these tables directly — a `ProjectionHandler` may never read `MetadataCache` (ADR-0045's hard
constraint), so once these tables leave the `Projected` set, nothing can materialize those columns the
same way going forward. This task performs the rename and adds read-time views; it does **not** cut
`SeriesProjection`'s write path over (`series-r2xhv`, blocked on this task, does that) or compose reads
from the cache (`series-q8jwc`).

## Decision

### `ALTER TABLE ... RENAME`, not a copy-and-drop

`series_episodes` → `series_episode_cache`, `series_seasons` → `series_season_cache`, via
`MetadataCache.initialize`. A rename preserves every row, every `source` provenance value
(ADR-0012/integration-m4k7p) and every `still_ref`/`poster_ref` with zero data movement — a
`CREATE ... SELECT` copy followed by a `DROP` was rejected because it doubles the write volume for no
benefit and introduces a window where both tables exist with the same rows, which is exactly the kind
of ambiguity a single-owner rename avoids by construction.

**Statement order is load-bearing, in two places:**

1. Within `MetadataCache.initialize` itself: the `ALTER TABLE ... RENAME` statements run first, before
   any `CREATE TABLE IF NOT EXISTS` in the same function could claim the target name.
2. Across the startup sequence: `Composition.buildApp` already calls `MetadataCache.initialize` (which
   performs the rename) before `Projection.startAllProjections` (which reaches
   `SeriesProjection.createTables`'s own `CREATE TABLE IF NOT EXISTS series_episode_cache`/
   `series_season_cache` — declared under the **new** names now, as a fresh-install fallback with
   unchanged shape). Reversing this order would let `createTables` claim the new name as an empty table
   first, making the rename attempt fail (target already exists) and stranding the real ~180 structural
   rows under the old name, invisible to every reader that has since moved to the new one. No change to
   `Composition.fs` was needed — the existing order already satisfies this.

Idempotent via `try/with`, the same `ALTER TABLE ... ADD COLUMN` migration idiom
`SeriesProjection.createTables` already uses for the `source` provenance column: a second run finds no
table named `series_episodes`/`series_seasons` (already renamed, or never existed on a fresh install)
and swallows the "no such table" error.

### Every literal SQL reference to the old names was mechanically renamed

`SeriesProjection.fs`, `SeriesRefresh.fs`, `CatalogProjection.fs`, and `Api.fs` all had direct SQL
references to `series_episodes`/`series_seasons` beyond the two ADR-0040 functions called out in the
task (`getJellyfinEpisodesMissingStill`, `backfillEpisodeStill`) — `createTables`, `dropTables`, every
INSERT/SELECT/UPDATE/DELETE, and Jellyfin materialization (`materializeSeason`, `materializeEpisode`,
`getExistingEpisodeKeys`, `getExistingSeasonNumbers`). Leaving any of these on the old names would have
broken at runtime the instant the rename ran (querying a table that no longer exists under that name),
so the rename had to be exhaustive across every literal reference, not just the two functions the task
text named explicitly as needing judgment calls.

Read-only query functions that join directly against the renamed tables via raw SQL (e.g.
`CatalogProjection.getEntries`, `SeriesProjection.getSeriesDetail`) are unaffected by ADR-0045's hard
constraint — that constraint is about a `ProjectionHandler`'s replay path (`handleEvent`, reached by
`checkProjectionDrift`'s shadow reconstruction) never reading the `MetadataCache` module, not about
live, read-only SQL joins against a table now classified `Cache`. `SeriesProjection.handleEvent` still
writes `series_episode_cache`/`series_season_cache` directly today — unchanged in this task —
so the shadow connection still creates and populates them during a drift check; they are simply excluded
from the diffed set now that they're `Cache`, not `Projected`.

### Two SQL views replace the materialized next-up/count columns

`series_list.next_up_season/episode/title` and `*.episode_count`/`watched_episode_count` were
maintained by `SeriesProjection.recalculateProgress`, which could do so only because it owned
`series_episodes` directly. Two views in `MetadataCache.initialize` replace that upkeep at read time:

- **`series_next_up`** — `ROW_NUMBER() OVER (PARTITION BY series_slug ORDER BY season_number,
  episode_number)` over `series_episode_cache LEFT JOIN series_episode_progress ... WHERE
  p.series_slug IS NULL`, filtered to `rn = 1`. This mirrors `recalculateProgress`'s existing semantics
  exactly: "watched" means *any* row in `series_episode_progress` matches `(series_slug, season_number,
  episode_number)` — not scoped to a single rewatch session — so an episode watched under one rewatch
  session and unwatched under another is still excluded as a next-up candidate. The `ROW_NUMBER`/`rn = 1`
  step is what prevents every unwatched episode from surfacing at once; without it the `LEFT JOIN ...
  WHERE p.series_slug IS NULL` alone returns every unwatched row, not just the next one.
- **`series_episode_counts`** — `COUNT(DISTINCT season_number)`/`COUNT(*)` grouped by `series_slug` over
  `series_episode_cache`.

Views were chosen over a re-materialization trigger or a scheduled recompute job: they are computed on
read, so they are structurally incapable of drifting from their source tables (there is no second write
path to fall out of sync), and they are invisible to `PRAGMA table_info` — ADR-0031's shadow-replay diff
never sees them, so no `tableRegistry` entry or drift-check change was needed for the views themselves
(only for the base tables they read from).

### A supporting index, invisible to the drift diff for the same reason

`series_episode_progress`'s primary key is `(series_slug, rewatch_id, season_number, episode_number)` —
`series_next_up`'s join deliberately does not constrain on `rewatch_id` (see above), so it cannot use
that PK as an index. `CREATE INDEX IF NOT EXISTS idx_series_progress_slug_episode ON
series_episode_progress (series_slug, season_number, episode_number)` was added to
`SeriesProjection.createTables`. Indexes, like views, don't appear in `PRAGMA table_info`, so this is
invisible to the ADR-0031 diff and exists identically in the shadow connection used there.

### Registry retargeting, in the same commit

- `Administration.tableRegistry`: `series_season_cache`/`series_episode_cache` reclassified `Cache
  "MetadataCache"` (were `Projected "SeriesProjection"`); `series_metadata_cache` added as a new `Cache`
  entry (see below). `TableClassificationTests.fs`'s registry-coverage test and its
  `SeriesProjection`-derived-table-set test were updated to match — `SeriesProjection`'s own `Projected`
  set shrinks to `series_list`, `series_detail`, `series_rewatch_sessions`, `series_episode_progress`.
- `Administration.imageRefColumns`: `("series_seasons", "poster_ref")` → `("series_season_cache",
  "poster_ref")`, `("series_episodes", "still_ref")` → `("series_episode_cache", "still_ref")`. Missing
  this retarget would make `getReferencedImageRefs` silently return an empty set for every episode still
  and season poster (their table no longer exists under the old name that `tableExists` would check),
  and the ADR-0025 orphan purge reads "referenced by nothing" as license to hard-delete the file — the
  exact data-loss scenario the task's dedicated regression test (`AdministrationTests.fs`) guards,
  deliberately as its own standalone assertion rather than folded into the pre-existing "is never flagged
  orphan" test.
- `Administration.getReferencedImageRefs` changed from `private` to a plain (non-private) binding — the
  same "not private, it's the direct test seam" shape already established for `checkProjectionDrift` in
  this file — so the regression test above can call it directly rather than only exercising it
  indirectly through `listOrphanedImages`.

### `series_metadata_cache` created and seeded now, even though nothing reads it yet

A fourth cache table, `series_metadata_cache (series_slug PK, overview, backdrop_ref, tmdb_rating,
episode_runtime, fetched_at)`, was added and seeded from `series_detail` in the same
`seedFromProjections` batch as `game_metadata_cache` — the task's What section called for it explicitly,
following the same rationale ADR-0045 gave for `game_metadata_cache`: give the eventual cutover
(`series-q8jwc`) a concrete, already-seeded table rather than requiring that task to also do schema work.
It ships seeded but unread by any query path today, same accepted-tradeoff shape as ADR-0045's
`movie_metadata_cache`.

## Consequences

### Positive
- Zero data loss: the rename preserves every row, and the dedicated data-loss regression test
  (`getReferencedImageRefs` returning non-empty post-rename) closes the specific hazard ADR-0025's purge
  would otherwise trigger.
- `series_next_up`/`series_episode_counts` are structurally incapable of drifting — there is no second
  write path to a materialized column to fall out of sync with the source tables.
- Unblocks `series-r2xhv` (cut `SeriesProjection`'s write path to a command-time cache seed) and
  `series-q8jwc` (compose reads from the cache), both of which depended on this rename landing first.

### Negative / accepted tradeoff
- Until `series-r2xhv` lands, `SeriesProjection` still writes `series_episode_cache`/
  `series_season_cache` directly during event replay — the tables are cache-classified for
  drift-check/rebuild purposes, but not yet cache-*written* in the ADR-0043 sense (a command-time seed,
  never touched by replay again). This is a deliberately incomplete intermediate state, called out
  explicitly in the task's own Notes, not an oversight.
- `series_metadata_cache` is dead weight (seeded, unread) until `series-q8jwc` cuts a reader over to it
  — the same accepted cost ADR-0045 took for `movie_metadata_cache`.

## Alternatives considered

- **Copy rows into new tables via `CREATE ... AS SELECT`, then `DROP` the old ones** — rejected in favor
  of `ALTER TABLE ... RENAME`: no reason to double the write volume or introduce a window with two
  tables holding the same rows, when SQLite's rename is atomic and free.
- **A materialized-column trigger recomputing `next_up`/counts on every episode-progress write** —
  rejected: a trigger is exactly the kind of second write path the views exist to eliminate; a view has
  no state of its own to fall out of sync.
- **Scope the rename to only the two ADR-0040 functions the task text named, leaving the rest of
  `SeriesProjection.fs` on the old table names** — rejected as unworkable: every other reference would
  fail at runtime the moment the tables were renamed out from under them; the mechanical rename had to be
  exhaustive.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
  — the doctrine this rename implements for the Series BC.
- `.agentheim/knowledge/decisions/0044-every-durable-table-classified-projected-cache-imperative.md`
  — the registry `series_season_cache`/`series_episode_cache`/`series_metadata_cache` are classified in.
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the cache tier this
  task moves the episode tree into, and the hard constraint on `ProjectionHandler` reads this ADR
  clarifies does not extend to live read-only SQL joins.
- `.agentheim/knowledge/decisions/0025-image-cache-orphan-detection-guard.md` — `imageRefColumns`,
  `getReferencedImageRefs`; the reason the registry retarget and its regression test are non-negotiable.
- `.agentheim/knowledge/decisions/0031-projection-drift-detector-throwaway-shadow-connection.md` — why
  views/indexes are invisible to the diff, and why the shadow connection still creates these tables
  during replay even though they're excluded from the diffed set.
- `.agentheim/knowledge/decisions/0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md`
  (amended by ADR-0043) — the `source` provenance column this rename preserves byte-identically.
- `src/Server/MetadataCache.fs`, `src/Server/SeriesProjection.fs`, `src/Server/Administration.fs` — the
  code this ADR describes.
- `tests/Server.Tests/MetadataCacheTests.fs`, `tests/Server.Tests/AdministrationTests.fs`,
  `tests/Server.Tests/TableClassificationTests.fs` — rename/idempotence/view/regression/registry
  coverage.
- `.agentheim/contexts/series/todo/series-r2xhv-refresh-writes-cache-only-narrow-series-refreshed.md`,
  `.agentheim/contexts/series/todo/series-q8jwc-compose-reads-from-metadata-cache.md` — the two tasks
  this rename unblocks.
