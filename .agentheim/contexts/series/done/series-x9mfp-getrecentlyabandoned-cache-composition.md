---
id: series-x9mfp
title: Retarget getRecentlyAbandoned's TmdbRating/SeasonCount/EpisodeCount/NextUp onto the metadata cache and views, same as its sibling getRecentlyFinished
status: done
type: refactor
context: series
created: 2026-08-01
completed: 2026-08-03
depends_on: [series-q8jwc]
blocks: []
tags: [series, metadata, cache, read-model, projection]
related_adrs: [0045, 0046, 0047]
related_research: []
prior_art: [series-q8jwc]
---

## Why

`series-q8jwc`'s task text explicitly enumerated `getAll`, `getRecentSeries`, and `getRecentlyFinished`
as the `SeriesListItem`/`RecentSeriesItem`-returning functions to retarget onto `series_metadata_cache`
and the `series_next_up`/`series_episode_counts` views. `getRecentlyAbandoned` — structurally identical
to `getRecentlyFinished` (same `SeriesListItem` DTO, same `series_list` columns, same shape of query) —
was not named and was deliberately left untouched to keep that task's diff scoped to its explicit list.

This leaves an inconsistency: `getRecentlyFinished` now composes `TmdbRating` from the cache and
`SeasonCount`/`EpisodeCount`/`NextUp` from the views, while `getRecentlyAbandoned` still reads all four
straight off `series_list`'s soon-to-be-dropped (`series-d5tpn`) materialized columns — which
`series-r2xhv`/ADR-0047 already stopped keeping fresh. An abandoned series' rating/next-up/counts shown
here will be frozen at whatever `series_list` held before that cutover, same staleness class
`series-q8jwc` closed everywhere else.

## What

- Apply the exact same `LEFT JOIN series_metadata_cache` / `LEFT JOIN series_episode_counts` /
  `LEFT JOIN series_next_up` composition `series-q8jwc` gave `getRecentlyFinished` to
  `getRecentlyAbandoned` too — same mapper shape, same nullable-read defaults.
- No DTO change — still `SeriesListItem`.

## Acceptance criteria

- [ ] To be written during refinement.

## Notes

Small, low-risk, mechanical diff — the reference implementation already exists in
`SeriesProjection.getRecentlyFinished` right next to this function.
