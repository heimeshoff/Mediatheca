# series -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 4
- **Doing:** 0
- **Done:** 0
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **series-m7fdk** — Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views (refactor) — `todo/series-m7fdk-rename-episode-tree-into-cache.md`
- **series-r2xhv** — Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time (refactor) — `todo/series-r2xhv-refresh-writes-cache-only-narrow-series-refreshed.md`
- **series-q8jwc** — Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged (refactor) — `todo/series-q8jwc-compose-reads-from-metadata-cache.md`
- **series-d5tpn** — Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard (refactor) — `todo/series-d5tpn-drop-columns-prove-drift-zero.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- no tasks in backlog -->
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
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
