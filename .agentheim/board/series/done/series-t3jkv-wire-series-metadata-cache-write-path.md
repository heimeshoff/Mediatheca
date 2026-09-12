---
id: series-t3jkv
title: Wire series_metadata_cache's write path — nothing keeps it fresh after the one-time seed, so refreshed and newly-added series never get real TmdbRating/Overview/EpisodeRuntime
status: done
type: refactor
context: series
created: 2026-08-01
completed: 2026-08-03
depends_on: [series-q8jwc]
blocks: []
tags: [series, metadata, cache, tmdb, refresh]
related_adrs: [0043, 0045, 0046, 0047]
related_research: []
prior_art: [series-q8jwc]
---

## Why

`series-q8jwc` composed `SeriesProjection`'s query functions (`getAll`, `getBySlug`, etc.) to read
`TmdbRating`/`Overview`/`EpisodeRuntime`/`BackdropRef`-shaped fields from `series_metadata_cache`
(`MetadataCache.fs`, ADR-0045/0046) instead of the now-stale `series_list`/`series_detail` columns. That
task was scoped to reads only ("join in the query function, not the API layer") and deliberately did not
touch any write path.

While implementing it, a real gap surfaced: **nothing writes `series_metadata_cache` going forward.**

- `MetadataCache.seedFromProjections` seeds it exactly once, gated by the `metadata_cache_seeded` marker
  (ADR-0045). By design, it never runs again.
- `SeriesRefresh.applyToProjection` (post-`series-r2xhv`/ADR-0047) calls only
  `upsertSeasonEpisodeCache`, which writes `series_season_cache`/`series_episode_cache`. It receives a
  fully-populated `RefreshFetchResult` — `Name`, `Overview`, `Genres`, `PosterRef`, `BackdropRef`,
  `TmdbRating`, `EpisodeRuntime` — and **discards all of it**. None of these fields land anywhere.
- `Api.addSeriesToLibraryImpl` seeds `series_season_cache`/`series_episode_cache` at command time
  (mirroring `series-r2xhv`'s pattern) but does not seed a `series_metadata_cache` row for the new
  series.

Net effect: for any series added *after* the one-time seed ran, `getBySlug`/`getAll`/etc. will show
`TmdbRating = None`, `Overview = ""`, `EpisodeRuntime = None` forever — even though the add flow just
fetched all of that from TMDB moments earlier — and no future refresh will ever populate it. This
silently defeats the entire cutover's purpose for every series added from this point forward, and freezes
every existing series' cache-sourced fields at whatever `seedFromProjections` captured at cutover time.

## What

- Add a `MetadataCache`-scoped (or `SeriesRefresh`-local) `upsertSeriesMetadataCache` helper that writes
  `series_metadata_cache (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at)`
  via `INSERT OR REPLACE`, mirroring `upsertSeasonEpisodeCache`'s shape.
- Call it from `SeriesRefresh.applyToProjection` alongside `upsertSeasonEpisodeCache`, using
  `RefreshFetchResult`'s already-fetched `Overview`/`BackdropRef`/`TmdbRating`/`EpisodeRuntime`.
- Call it from `Api.addSeriesToLibraryImpl` at command time (same imperative-seed pattern
  `series-r2xhv` established for the season/episode cache), using `SeriesAddedData`'s fields.
- Decide `fetched_at`'s value on a genuine write (vs. the seed step's deliberate `NULL`) — probably the
  current UTC timestamp, so a real refresh is distinguishable from a never-refreshed seed row.

## Acceptance criteria

- [ ] To be written during refinement.

## Notes

Cross-reference: `series-q8jwc`'s Outcome section and the `SeriesRefresh.fs`/`Api.fs` files it touched
(or rather, didn't touch) for the code shape this task extends.
