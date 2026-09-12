## ADRs scoped to this BC

<!-- adr-local:start -->
- **0063** -- Next Up follows the furthest-watched frontier: the `series_next_up` view and the shared client `NextUp.compute` both skip unwatched gaps behind the max watched (season, episode) tuple; view redefinitions in `MetadataCache.initialize` now require `DROP VIEW IF EXISTS` to take effect on existing databases. -- 2026-08-07 -- `../../decisions/0063-next-up-follows-furthest-watched-frontier.md`
- **0051** -- SeriesProjection drift reaches zero by physically dropping the externally-sourced columns (`status`/`backdrop_ref` retained per the identity-card clause); the ADR-0049 lossy-rebuild guard is retired, and `MetadataCache.recoverStranded` guards the rename-ordering hazard (view-safe, atomic, non-fatal). Supersedes ADR-0049. -- 2026-08-02 -- `../../decisions/0051-series-projection-drift-reaches-zero-via-column-drop-and-guard-retirement.md`
- **0048** -- Series read composition joins `series_metadata_cache` and the `series_next_up`/`series_episode_counts` views at query time, never at the API layer — DTOs and client stay byte-identical. -- 2026-08-01 -- `../../decisions/0048-series-reads-composed-from-metadata-cache-at-query-time.md`
- **0047** -- `Series_refreshed` narrowed to real airing-status transitions (previousStatus from the aggregate; projection handler applies it); all other TMDB metadata leaves the log for the cache tier. Backward-compatible with all 780 historical events. -- 2026-08-01 -- `../../decisions/0047-series-refreshed-narrowed-to-real-airing-status-transitions.md`
- **0046** -- Series season/episode tree renamed into the cache tier (`series_episode_cache`/`series_season_cache`, idempotent ALTER TABLE RENAME, zero data movement); SQL views `series_next_up`/`series_episode_counts` replace the materialized columns. -- 2026-08-01 -- `../../decisions/0046-series-episode-tree-renamed-into-cache-views-replace-materialized-columns.md`
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
- Task board (tasks by status) for this BC: `../../../board/series/INDEX.md`
