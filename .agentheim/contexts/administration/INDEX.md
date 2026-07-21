# administration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 6
- **Todo:** 0
- **Doing:** 1
- **Done:** 8
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
- **administration-h4br2** — Browser smoke-test the Events tab Follow toggle end-to-end (chore) — `doing/administration-h4br2-event-browser-follow-smoke-test.md`
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **administration-yamm5** — Job runs console — history, outcomes, and run-now for scheduled jobs (feature) — `done/administration-yamm5-job-runs-console.md`
- **administration-xx3mw** — Image cache admin — orphan detection, size overview, purge (feature) — `done/administration-xx3mw-image-cache-admin.md`
- **administration-qjcp4** — Projection dashboard — checkpoint/lag overview and rebuild-by-command with streamed progress (feature) — `done/administration-qjcp4-projection-dashboard-rebuild.md`
- **administration-v4y9g** — Stream drill-in — per-stream timeline with formatted+raw views, projection state, cross-links (feature) — `done/administration-v4y9g-stream-drill-in-timeline.md`
- **administration-mtf1f** — Event explorer live tail — follow mode for incoming events (feature) — `done/administration-mtf1f-event-live-tail.md`
- **administration-hw74a** — Store health tab — event volume stats, largest streams, storage sizes (feature) — `done/administration-hw74a-store-health-stats.md`
- **administration-g5dfy** — Event explorer — FTS payload search, time/position/BC filters, keyset pagination (feature) — `done/administration-g5dfy-event-explorer-search-filters-pagination.md`
- **administration-p0jka** — Admin console foundation — IAdminApi contract, Administration.fs, /admin section with tabs (feature) — `done/administration-p0jka-admin-console-foundation.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **administration-da908** — Prove a Playwright harness can drive the full Mediatheca stack and observe network traffic (spike) — `backlog/administration-da908-playwright-e2e-harness-spike.md`
- **administration-a4d9b** — Assert the Events-tab Follow toggle's three live-tail behaviors via committed Playwright specs (feature) — `backlog/administration-a4d9b-playwright-follow-toggle-specs.md`
- **administration-btvqa** — Integrity checks — shadow-table replay drift detector and unknown-event report (feature) — `backlog/administration-btvqa-projection-drift-integrity-checks.md`
- **administration-xjmda** — Compensating-event composer — append corrective events from the admin UI (feature) — `backlog/administration-xjmda-compensating-event-composer.md`
- **administration-wwc36** — Event surgery — raw edit/delete/rename with auto-backup, preview, and projections-dirty flag (feature) — `backlog/administration-wwc36-event-surgery-guardrails.md`
- **administration-vrc56** — Event log export/import as NDJSON (feature) — `backlog/administration-vrc56-ndjson-export-import.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0026** -- Scheduled-job runs are recorded through a shared registry and an injected recorder seam; run-now is fire-and-forget with a startup-reconciled running row and a name-keyed in-memory guard -- 2026-07-21 -- `knowledge/decisions/0026-job-runs-recording-shared-registry-and-run-now.md`
- **0025** -- Image-cache orphan detection diffs on-disk files against projection refs, guarded by a not-dirty check, and hard-deletes with re-derivation at purge -- 2026-07-21 -- `knowledge/decisions/0025-image-cache-orphan-detection-guard.md`
- **0024** -- Projection rebuild streams over the shared connection, guarded by an in-memory concurrency lock; "Rebuild all" is client-side orchestration, not a second route -- 2026-07-21 -- `knowledge/decisions/0024-projection-rebuild-stream-connection-and-concurrency.md`
- **0022** -- Stream drill-in flattens typed projection DTOs and links dangling cross-references without verification -- 2026-07-20 -- `knowledge/decisions/0022-stream-drill-in-projection-flattening-and-dangling-cross-links.md`
- **0023** -- Event explorer live tail polls via an epoch-guarded self-rescheduling Cmd, torn down on navigation away from Admin -- 2026-07-20 -- `knowledge/decisions/0023-event-explorer-live-tail-polling-with-epoch-guarded-cmd.md`
- **0021** -- Health tab uses index-only aggregate queries over the events table (materialized summary deferred) -- 2026-07-20 -- `knowledge/decisions/0021-health-tab-index-only-aggregate-queries.md`
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
