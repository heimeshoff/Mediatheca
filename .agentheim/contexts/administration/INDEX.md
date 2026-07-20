# administration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 6
- **Todo:** 4
- **Doing:** 0
- **Done:** 2
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **administration-v4y9g** — Stream drill-in — per-stream timeline with formatted+raw views, projection state, cross-links (feature) — `todo/administration-v4y9g-stream-drill-in-timeline.md`
- **administration-mtf1f** — Event explorer live tail — follow mode for incoming events (feature) — `todo/administration-mtf1f-event-live-tail.md`
- **administration-qjcp4** — Projection dashboard — checkpoint/lag overview and rebuild-by-command with streamed progress (feature) — `todo/administration-qjcp4-projection-dashboard-rebuild.md`
- **administration-hw74a** — Store health tab — event volume stats, largest streams, storage sizes (feature) — `todo/administration-hw74a-store-health-stats.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **administration-g5dfy** — Event explorer — FTS payload search, time/position/BC filters, keyset pagination (feature) — `done/administration-g5dfy-event-explorer-search-filters-pagination.md`
- **administration-p0jka** — Admin console foundation — IAdminApi contract, Administration.fs, /admin section with tabs (feature) — `done/administration-p0jka-admin-console-foundation.md`
<!-- no tasks in done -->
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **administration-btvqa** — Integrity checks — shadow-table replay drift detector and unknown-event report (feature) — `backlog/administration-btvqa-projection-drift-integrity-checks.md`
- **administration-xjmda** — Compensating-event composer — append corrective events from the admin UI (feature) — `backlog/administration-xjmda-compensating-event-composer.md`
- **administration-wwc36** — Event surgery — raw edit/delete/rename with auto-backup, preview, and projections-dirty flag (feature) — `backlog/administration-wwc36-event-surgery-guardrails.md`
- **administration-vrc56** — Event log export/import as NDJSON (feature) — `backlog/administration-vrc56-ndjson-export-import.md`
- **administration-yamm5** — Job runs console — history, outcomes, and run-now for scheduled jobs (feature) — `backlog/administration-yamm5-job-runs-console.md`
- **administration-xx3mw** — Image cache admin — orphan detection, size overview, purge (feature) — `backlog/administration-xx3mw-image-cache-admin.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0020** -- Event explorer uses FTS5 external-content search and client-tracked keyset pagination -- 2026-07-20 -- `knowledge/decisions/0020-event-explorer-fts5-search-and-keyset-pagination.md`
- **0017** -- Administration console gets its own Fable.Remoting API (IAdminApi) -- 2026-07-20 -- `knowledge/decisions/0017-second-remoting-api-for-admin-console.md`
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
