# series -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 2
- **Doing:** 0
- **Done:** 6
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **series-k4zpn** — Next Up must follow the furthest-watched episode, not the first unwatched one — a skipped episode currently pins Next Up forever; when nothing remains beyond the furthest watched, show the fully-watched state even if a gap exists (bug) — `todo/series-k4zpn-next-up-follows-furthest-watched.md`
- **series-ww1rb** — Dashboard series cards show only the *current season's* episode dots with a season rail above, and mark the episodes actually watched — extend `DashboardSeriesNextUp` with per-season touched flags and current-season per-episode watched flags, joined at query time per ADR-0048 (feature) — `todo/series-ww1rb-dashboard-current-season-dots-real-watch-state.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **series-t3jkv** — Wire series_metadata_cache's write path — nothing keeps it fresh after the one-time seed, so refreshed and newly-added series never get real TmdbRating/Overview/EpisodeRuntime (refactor) — `done/series-t3jkv-wire-series-metadata-cache-write-path.md`
- **series-x9mfp** — Retarget getRecentlyAbandoned's TmdbRating/SeasonCount/EpisodeCount/NextUp onto the metadata cache and views, same as its sibling getRecentlyFinished (refactor) — `done/series-x9mfp-getrecentlyabandoned-cache-composition.md`
- **series-d5tpn** — Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard (refactor) — `done/series-d5tpn-drop-columns-prove-drift-zero.md`
- **series-q8jwc** — Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged (refactor) — `done/series-q8jwc-compose-reads-from-metadata-cache.md`
- **series-r2xhv** — Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time (refactor) — `done/series-r2xhv-refresh-writes-cache-only-narrow-series-refreshed.md`
- **series-m7fdk** — Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views (refactor) — `done/series-m7fdk-rename-episode-tree-into-cache.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0051** -- SeriesProjection drift reaches zero by physically dropping the externally-sourced columns (`status`/`backdrop_ref` retained per the identity-card clause); the ADR-0049 lossy-rebuild guard is retired, and `MetadataCache.recoverStranded` guards the rename-ordering hazard (view-safe, atomic, non-fatal). Supersedes ADR-0049. -- 2026-08-02 -- `../../knowledge/decisions/0051-series-projection-drift-reaches-zero-via-column-drop-and-guard-retirement.md`
- **0048** -- Series read composition joins `series_metadata_cache` and the `series_next_up`/`series_episode_counts` views at query time, never at the API layer — DTOs and client stay byte-identical. -- 2026-08-01 -- `../../knowledge/decisions/0048-series-reads-composed-from-metadata-cache-at-query-time.md`
- **0047** -- `Series_refreshed` narrowed to real airing-status transitions (previousStatus from the aggregate; projection handler applies it); all other TMDB metadata leaves the log for the cache tier. Backward-compatible with all 780 historical events. -- 2026-08-01 -- `../../knowledge/decisions/0047-series-refreshed-narrowed-to-real-airing-status-transitions.md`
- **0046** -- Series season/episode tree renamed into the cache tier (`series_episode_cache`/`series_season_cache`, idempotent ALTER TABLE RENAME, zero data movement); SQL views `series_next_up`/`series_episode_counts` replace the materialized columns. -- 2026-08-01 -- `../../knowledge/decisions/0046-series-episode-tree-renamed-into-cache-views-replace-materialized-columns.md`
<!-- no ADRs scoped to this BC -->
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
<!-- no research touching this BC -->
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- no concept pages yet -->
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
