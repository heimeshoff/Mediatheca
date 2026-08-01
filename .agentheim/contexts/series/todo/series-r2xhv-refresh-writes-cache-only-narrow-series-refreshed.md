---
id: series-r2xhv
title: Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time
status: todo
type: refactor
context: series
created: 2026-08-01
completed:
depends_on: [series-m7fdk]
blocks: []
tags: [series, integration, tmdb, jellyfin, drift, event-log]
related_adrs: [0012, 0026, 0029, 0031, 0032, 0039, 0040]
related_research: [tv-series-metadata-fallback-sources]
prior_art: [integration-m4k7p, integration-006, integration-q7wv3]
---

## Why

`SeriesRefresh.applyToProjection` (`src/Server/SeriesRefresh.fs:169`) is the single writer producing
all 2437 drift discrepancies. Series and Integration cannot move separately — `SeriesRefresh` and
`JellyfinImport` **are** Integration's write paths into Series' read model.

Separately, `Series_refreshed` fires on every refresh: 780 events across 38 series. Verified against
the live log, **566 carry `previousStatus: null, newStatus: null`** (no change) and **214 carry a real
transition** — `SeriesRefresh.fs:299-304` already sets those fields only when
`previousStatus <> result.Status`. So the historical log already encodes every airing-status
transition it ever observed; the 566 no-change events are pure volume tax.

**Verified consequence:** replaying `Series_added_to_library.status` plus the 214 real transitions
reproduces live status for **103 of 105 series** (down from 7 status mismatches to 2). Narrowing the
event therefore does not merely cut volume — it makes `status` fully replayable, which is why
`status` survives as a projection column in `series-d5tpn` instead of being demoted to cache.

## What

**Cache-only writes.**

- `applyToProjection` writes **only** `series_metadata_cache` / `series_season_cache` /
  `series_episode_cache`. Its `UPDATE series_list` and `UPDATE series_detail` statements
  (`SeriesRefresh.fs:176-230`) are deleted outright.
- `JellyfinImport.materializeMissingEpisodes`' injected season/episode writers (wired in
  `JellyfinSync.fs`) point at the cache tables. `INSERT OR IGNORE` / `INSERT OR REPLACE`-resets-`source`
  semantics unchanged — ADR-0012's enrichment mechanism is preserved verbatim.
- `Series_added_to_library`'s episode/season snapshot becomes a **cache seed written at the command
  path** (`Api.addSeriesToLibrary`), not by replay. Cache cleanup on `Series_removed_from_library` is
  likewise **imperative at the command site**, never in `handleEvent` — a cache delete inside
  `handleEvent` would be a live write issued from a shadow replay, breaking the
  `administration-c3nvp` constraint.

**Narrow `Series_refreshed`.**

- `refreshOne` appends the event **only when `statusTransitioned`**. No transition → no event. Keep
  the `Refresh_series_from_tmdb` command and `ActiveSeries.Status` in aggregate state.
- Payload narrows to `{previousStatus, newStatus}` — both non-optional in the new shape. Drop
  `refreshedAt` (the timestamp is already the event's own) and `newEpisodeCount` (`job_runs`, ADR-0026,
  already records "N refreshed, N errors, N new episodes, N status transitions" per run —
  `Composition.fs:331-334`).
- **Deserialization must stay backward-compatible**: the 780 historical events carry `refreshedAt` and
  `newEpisodeCount`, and 566 carry `previousStatus: null` / `newStatus: null`. The codec must read
  them without error; a null-status historical event decodes to a no-transition event.
- **`previousStatus` must come from the aggregate**, not from `SELECT tmdb_id, status FROM series_detail`
  (`SeriesRefresh.fs:284-287`). Reading a read model on the write path is the same CQRS inversion
  species as `promoteToInFocusIfNeeded`, and it misfires whenever the projection lags. Keep the
  `tmdb_id` lookup; source `previousStatus` from `Series.reconstitute`.
- **`SeriesProjection.handleEvent`'s `Series_refreshed` arm stops being a no-op** (currently
  `SeriesProjection.fs:687-693`) and applies the transition to `series_list.status` and
  `series_detail.status`. A no-transition event applies nothing.

## Acceptance criteria

- [ ] `grep -n "series_list\|series_detail\|series_seasons\|series_episodes" src/Server/SeriesRefresh.fs` returns only the `SELECT tmdb_id FROM series_detail` lookup.
- [ ] Expecto: a simulated refresh with no status change writes N cache rows, **zero** rows in `series_list` / `series_detail`, and appends **zero** events.
- [ ] Expecto: a simulated refresh with a status change appends exactly one `Series_refreshed` and updates `series_list.status` / `series_detail.status` through the projection handler.
- [ ] Expecto: `Series.Serialization.deserialize "Series_refreshed" data` succeeds for all three historical payload shapes — full payload with a real transition, full payload with null statuses, and the new narrowed payload — and `deserialize |> Option.map serialize` round-trips (ADR-0032 composer compatibility).
- [ ] Expecto: replaying a fixture of `Series_added_to_library` + a null-status `Series_refreshed` + a real-transition `Series_refreshed` yields the transition's `newStatus` in both `series_list` and `series_detail`.
- [ ] Expecto: `previousStatus` on an emitted event equals the aggregate's status, proven by a fixture where the projection is deliberately stale.
- [ ] Expecto: `Administration.buildUnknownEventReport` on a fixture containing historical `Series_refreshed` events reports it as neither unhandled nor unformattable.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"`Series_refreshed` narrowed to real airing-status transitions; all other TMDB metadata leaves
the log"*, `scope: series`.

The ADR must record why *"keep writing the column from the refresh path"* is **provably
drift-generating**: the live column would hold only the refresh-written half, and every future refresh
that discovers a new value widens the gap into a permanent, growing `columnMismatch`. Airing status
escapes this because the narrowed event carries the transition into the log, so the projection column
is written **exclusively by an event that carries it** — the identity-card clause of
`infrastructure-e4kwm`.

**Two known residual status discrepancies**, verified against the live log, to be resolved in
`series-d5tpn` rather than here:

- `love-death-robots-2019` — replay yields `Ended`, live holds `Returning`. A transition back to
  `Returning` happened without being recorded. Fix with a compensating `Series_refreshed` via the
  ADR-0032 composer.
- `silo-2023-2` — replays to `Returning` but has no live row at all (this is the `series_list`
  `onlyInShadow` row). A stale/orphaned stream; decide remove-vs-restore during `series-d5tpn`.
