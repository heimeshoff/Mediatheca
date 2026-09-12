# administration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 0
- **Doing:** 0
- **Done:** 31
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (current-month entries live; older months archived verbatim under `done-archive/` — kept for prior-art search, ADR-0039 convention)
<!-- done-list:start -->
- **administration-b3xqf** — Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt (chore) — `done/administration-b3xqf-readme-stale-after-cutover-and-eventlogfilter-retirement.md`
- **administration-z6ymt** — Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change (chore) — `done/administration-z6ymt-purge-demoted-metadata-events.md`
- **administration-kv7dp** — Block projection rebuild for handlers with out-of-band writers — rebuilding SeriesProjection today permanently destroys 780 refreshes' worth of TMDB metadata plus 23 Jellyfin-materialized episodes (bug) — `done/administration-kv7dp-block-lossy-projection-rebuild.md`
- **administration-c3nvp** — Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents (feature) — `done/administration-c3nvp-metadata-cache-tier.md`
- **administration-t9bzx** — Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions (refactor) — `done/administration-t9bzx-table-classification-registry.md`
- **administration-k3vmt** — Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button (refactor) — `done/administration-k3vmt-dissolve-admin-console-into-settings.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/administration/INDEX.md`
