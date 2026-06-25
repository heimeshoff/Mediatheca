# integration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 2
- **Todo:** 0
- **Doing:** 0
- **Done:** 4
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- no tasks in todo -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **integration-004** -- Steam playtime sync silently drops same-day deltas -- `bug` -- `done/integration-004-steam-sync-drops-same-day-delta.md`
- **integration-003** -- Surface the persisted Jellyfin sync failure in the Settings UI -- `feature` -- `done/integration-003-surface-jellyfin-sync-failure-in-settings.md`
- **integration-002** -- Re-authenticate Jellyfin and retry once on a 401/403 during sync -- `bug` -- `done/integration-002-jellyfin-reauth-on-401.md`
- **integration-001** -- Jellyfin sync silently stopped writing episode watch history -- `bug` -- `done/integration-001-jellyfin-sync-silently-stopped.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **integration-005** -- Spike — fallback metadata source when TMDB lags on new seasons -- `spike` -- `backlog/integration-005-fallback-metadata-source-spike.md`
- **integration-006** -- Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up -- `bug` -- `backlog/integration-006-nightly-refresh-skips-ended-series.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0011** -- Jellyfin self-heals a rejected token via a pure re-auth-and-retry orchestration -- 2026-05-27 -- `knowledge/decisions/0011-jellyfin-reauth-on-401.md`
- **0010** -- Jellyfin sync persists its last result and isolates per-item faults -- 2026-05-27 -- `knowledge/decisions/0010-jellyfin-sync-observability-fault-isolation.md`
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
