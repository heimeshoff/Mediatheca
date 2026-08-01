---
id: 0047
title: Series_refreshed narrowed to real airing-status transitions; all other TMDB metadata leaves the log
scope: series
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [series-r2xhv]
related_research: []
---

# ADR 0047: Series_refreshed narrowed to real airing-status transitions; all other TMDB metadata leaves the log

## Context

`SeriesRefresh.applyToProjection` was the single writer producing all 2437 drift discrepancies
`infrastructure-e4kwm` found in the Series BC — it wrote TMDB-fetched name/overview/poster/genres/
rating/status directly into `series_list`/`series_detail` with no backing event, so a projection
rebuild silently lost every refresh's worth of state. `series-m7fdk` (ADR-0046) renamed the
season/episode tree into the cache tier ADR-0045 built, but deliberately left `SeriesRefresh`'s write
path and `SeriesProjection.handleEvent`'s cache seeding untouched, assigning that cutover to this task.

Separately, `Series_refreshed` fired on every refresh — 780 events across 38 series, verified against
the live log. 566 carried `previousStatus: null, newStatus: null` (no change); 214 carried a real
transition, because `SeriesRefresh.fs`'s existing `previousStatus <> result.Status` guard already only
populated those two fields when the status actually changed. The historical log therefore already
encodes every airing-status transition it ever observed — the 566 no-change events are pure volume tax,
not information. Replaying `Series_added_to_library.status` plus the 214 real transitions reproduced
live status for 103 of 105 series (down from 7 status mismatches to 2, the two documented as residual
in the task's Notes and deferred to `series-d5tpn`).

## Decision

### Cache-only writes

`SeriesRefresh.applyToProjection` now calls a single shared function, `upsertSeasonEpisodeCache`, which
writes only `series_season_cache`/`series_episode_cache`. Its `UPDATE series_list`/`UPDATE series_detail`
statements are deleted outright — TMDB's name/overview/poster/genres/rating/status describe a third
party, not the user's own engagement (ADR-0043's doctrine), so a refresh no longer touches either
Projected table at all.

`upsertSeasonEpisodeCache` is shared between the refresh path and a new command-time use:
`Series_added_to_library`'s episode/season snapshot is now seeded into the cache imperatively by
`Api.addSeriesToLibraryImpl`, right after the command succeeds — not by `SeriesProjection.handleEvent`
during replay. Symmetrically, `Api.removeSeries` now deletes the cache rows imperatively after
`Series_removed_from_library` succeeds, instead of `handleEvent` doing it. A cache write or delete
inside `handleEvent` would be a live write issued from a shadow replay — the same
`administration-c3nvp`/ADR-0045 constraint that keeps a `ProjectionHandler`'s replay path read-only
against the cache tier, extended here to the two tables ADR-0046 renamed into that tier but had not yet
cut the write path for.

### `Series_refreshed` narrowed to only fire on a real transition

`SeriesRefresh.refreshOne`/`refreshOneForJob` now source `previousStatus` from `Series.reconstitute` (the
aggregate), never from a `SELECT ... status FROM series_detail` read-model query — reading the read
model on the write path is the same CQRS inversion species as `promoteToInFocusIfNeeded`'s, and it would
misfire the transition check whenever the projection lags the aggregate (proven directly by
`SeriesRefreshCacheTests.fs`'s "previousStatus is sourced from the aggregate, not a stale
series_detail.status" test, which hand-corrupts the read model and confirms the emitted event's
`PreviousStatus` still matches the aggregate). `Series.decide`'s `Refresh_series_from_tmdb` arm appends
`Series_refreshed` only when `NewStatus.IsSome` — no transition, no event.

`SeriesRefreshedData` drops `RefreshedAt` (the event already carries its own store timestamp) and
`NewEpisodeCount` (`job_runs`, ADR-0026, already reports "N refreshed, N errors, N new episodes, N status
transitions" per run from `RefreshOutcome`, a new `SeriesRefresh`-local type that carries the fetch
result's episode count directly to the caller instead of routing it through the event). `PreviousStatus`/
`NewStatus` stay `string option` — not because the *narrowed* event ever carries `None`, but because the
same decoder must still read all 780 historical events, 566 of which are exactly that shape.
`decodeSeriesRefreshedData` was already tolerant of missing/extra fields (`get.Optional.Field`, defaulted
`RefreshedAt`/`NewEpisodeCount`), so no decoder logic changed — only the encoder narrowed, and the two
now-decommissioned fields were deleted from the type. A null-status historical event decodes to
`PreviousStatus = None; NewStatus = None`, which both `Series.evolve` and
`SeriesProjection.handleEvent`'s (no longer no-op) `Series_refreshed` arm treat as "apply nothing."

### Why "keep writing the column from the refresh path" is provably drift-generating

The alternative — leave `series_list.status`/`series_detail.status` writable directly from
`applyToProjection`, narrow only the event — was rejected. If the live column were written from the
refresh path but the event stayed unnarrowed-in-practice (or omitted the transition), the column would
hold only whatever the refresh path happened to write, and the log would hold a different, growing
subset. Every future refresh that discovers a status the log never recorded would widen the gap into a
permanent `columnMismatch`, the exact 2437-discrepancy shape this task exists to close. Airing status
escapes this because the narrowed event carries every transition into the log, so the projection column
is written **exclusively** by an event that carries it — ADR-0043's/`infrastructure-e4kwm`'s
identity-card clause (a projection column is either derived from an event carrying it, or it isn't
Projected at all).

## Consequences

### Positive
- Closes the two-Projected-tables share of the 2437 discrepancies `infrastructure-e4kwm` found: a refresh
  no longer writes `series_list`/`series_detail` out of band.
- `status` becomes the first column in this cutover to be *fully* replayable, not merely demoted to cache
  — replaying `Series_added_to_library` + every narrowed `Series_refreshed` reproduces live status for
  103 of 105 series today (the residual two are drift already present in the log, not caused by this
  narrowing, and are assigned to `series-d5tpn`).
- Cuts `Series_refreshed` volume from 780 historical (566 no-change) down to only-real-transitions going
  forward — no future no-op refresh appends anything.
- `Series_added_to_library`'s cache seed and `Series_removed_from_library`'s cache cleanup move to
  command time, closing the same replay-writes-a-cache-table gap ADR-0045 forbids for `MetadataCache.fs`'s
  own tables, now extended to the season/episode cache tables ADR-0046 renamed but left write-path
  unchanged.

### Negative / accepted tradeoff
- `SeriesProjection.dropTables` still drops `series_season_cache`/`series_episode_cache` (a
  `ProjectionHandler`-owned table via `Init`/`Drop`), while `handleEvent` no longer repopulates them from
  `Series_added_to_library` on replay. A real admin-triggered full projection rebuild would therefore wipe
  season/episode cache data until a subsequent refresh or Jellyfin materialization repopulates it. This is
  the same deliberately incomplete intermediate state ADR-0046 called out for its own scope, now carried
  one step further; fully moving these tables' ownership out of `SeriesProjection`'s `Init`/`Drop` is
  `series-d5tpn`'s job, not this task's.
- Two residual status discrepancies remain, unresolved here (assigned to `series-d5tpn`):
  `love-death-robots-2019` (replay yields `Ended`, live holds `Returning` — a transition back to
  `Returning` happened without being recorded) and `silo-2023-2` (replays to `Returning` but has no live
  row at all — a stale/orphaned `series_list` row).

## Alternatives considered

- **Keep writing `series_list.status`/`series_detail.status` from `applyToProjection`, narrow only the
  event** — rejected: provably drift-generating, see above.
- **Demote `status` to cache-only, like every other TMDB-fetched field** — rejected: unlike
  name/overview/poster/genres/rating, status transitions are fully recoverable from the log once the
  event is narrowed (103/105 series, verified), so there is no reason to give up projection-column
  replayability for a field the log can already carry losslessly.
- **Keep `RefreshedAt`/`NewEpisodeCount` on the narrowed event "just in case"** — rejected: `RefreshedAt`
  duplicates the event's own store timestamp, and `NewEpisodeCount` already has a home in the job-runs
  summary (ADR-0026); carrying either forward would keep exactly the kind of TMDB-metadata-in-the-log the
  event-worthiness doctrine (ADR-0043) exists to stop.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md` —
  the doctrine this narrowing implements.
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the
  replay-never-writes-the-cache constraint extended here to `series_season_cache`/`series_episode_cache`.
- `.agentheim/knowledge/decisions/0046-series-episode-tree-renamed-into-cache-views-replace-materialized-columns.md`
  — the rename this task's write-path cutover completes, and the deliberately incomplete `dropTables`
  state this task inherits and does not resolve.
- `src/Server/SeriesRefresh.fs` (`applyToProjection`, `upsertSeasonEpisodeCache`, `refreshOne`,
  `refreshOneForJob`, `RefreshOutcome`), `src/Server/Series.fs` (`SeriesRefreshedData`, `decide`'s
  `Refresh_series_from_tmdb` arm, `Serialization`), `src/Server/SeriesProjection.fs` (`handleEvent`'s
  `Series_added_to_library`/`Series_removed_from_library`/`Series_refreshed` arms), `src/Server/Api.fs`
  (`addSeriesToLibraryImpl`, `removeSeries`) — the code this ADR describes.
- `tests/Server.Tests/SeriesRefreshCacheTests.fs`, `tests/Server.Tests/SeriesTests.fs`,
  `tests/Server.Tests/AdministrationTests.fs` — cache-only-write, narrowing, historical-decode-compat,
  and unknown-event-report coverage.
- `.agentheim/contexts/series/todo/series-d5tpn-drop-columns-prove-drift-zero.md` (referenced by this
  task's Notes) — the deferred drop-columns-and-prove-zero step, and the two residual status
  discrepancies' resolution.
