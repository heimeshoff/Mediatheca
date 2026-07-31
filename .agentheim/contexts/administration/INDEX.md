# administration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 3
- **Todo:** 1
- **Doing:** 0
- **Done:** 22
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **administration-jrflk** — Retire Administration.fs's three ambient module-level guards (runningJobs, rebuildingProjections, driftCheckInProgress) in favour of composition-root-owned per-instance state, closing the cross-file test-collision class the JobRunsTests name prefix papers over (bug) — `todo/administration-jrflk-job-name-collision-test-flake.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **administration-wwc36** — Event surgery — raw edit/delete/rename with auto-backup, preview, and projections-dirty flag (feature) — `done/administration-wwc36-event-surgery-guardrails.md`
- **administration-mz6kp** — Migrate Api.create/Administration.create and the raw Giraffe stream handlers from one shared SqliteConnection to per-request (factory-based) connections, retiring the ADR-0030 semaphore gate (refactor) — `done/administration-mz6kp-per-request-connection-migration.md`
- **administration-qk3f7** — Add a formatEvent case for Game_rawg_id_set — the one real handled-but-unformattable drift the unknown-event report caught (bug) — `done/administration-qk3f7-game-rawg-id-set-formatter-gap.md`
- **administration-xjmda** — Compensating-event composer — append corrective events from the admin UI (feature) — `done/administration-xjmda-compensating-event-composer.md`
- **administration-btvqa** — Shadow-table replay drift detector — verify projection read models exactly match the event log (feature) — `done/administration-btvqa-projection-drift-integrity-checks.md`
- **administration-gxd6e** — Unknown-event report — distinct event types no projection handler recognizes or formatEvent can't render, with counts and samples (feature) — `done/administration-gxd6e-unknown-event-report.md`
- **administration-cx92m** — Audit whether the single shared SqliteConnection is safe under request×request concurrency, and decide per-operation connections vs. a global gate (spike) — `done/administration-cx92m-shared-connection-request-concurrency-audit.md`
- **administration-nf3wk** — "Event Browser's \"No matches\" pagination-bar text is dead code — give the filter-empty state its own message instead" (bugfix) — `done/administration-nf3wk-dead-no-matches-branch.md`
- **administration-h4k2p** — Fix trailing-comma malformed JSON in empty-payload SSE frames — extract one shared pure `sseFrame` helper the three SSE handlers call, so an empty-object payload can never emit `data: {"type":"complete",}`. Fixes the Projections-tab Rebuild button reporting every successful rebuild as a failure. (bug) — `done/administration-h4k2p-sse-empty-payload-trailing-comma-bug.md`
- **administration-vrc56** — Event log export/import as NDJSON — stream out/in via plain Giraffe routes, preserving exact global_position, into an empty store only (feature) — `done/administration-vrc56-ndjson-export-import.md`
- **administration-tj8n2** — Scheduled-job timers race on the shared SqliteConnection and crash the process — fix with a dedicated job connection plus a per-command lock (bug) — `done/administration-tj8n2-scheduled-job-catchup-connection-race.md`
- **administration-a4d9b** — Assert the Events-tab Follow toggle's three live-tail behaviors via committed Playwright specs (feature) — `done/administration-a4d9b-playwright-follow-toggle-specs.md`
- **administration-da908** — Prove a Playwright harness can drive the full Mediatheca stack and observe network traffic (spike) — `done/administration-da908-playwright-e2e-harness-spike.md`
- **administration-h4br2** — Browser smoke-test the Events tab Follow toggle end-to-end (chore) — `done/administration-h4br2-event-browser-follow-smoke-test.md`
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
- **administration-bq4tw** — DaisyUI `select.bordered`/`textarea.bordered` don't exist on Feliz.DaisyUI 5.x — Fable's FS0039 placeholder throws at render and unmounts the whole SPA root (bug) — `backlog/administration-bq4tw-daisyui-bordered-crash.md`
- **administration-svq3t** — Playwright e2e spec for the Surgery tab (edit/delete/rename + confirm dialogs + dirty banner) (feature) — `backlog/administration-svq3t-surgery-tab-e2e-spec.md`
- **administration-n8kqw** — Event log import — wipe-first path for a non-empty store: backup, preview + confirm, then wipe and re-import in one transaction (feature) — `backlog/administration-n8kqw-wipe-first-import.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0034** -- Event surgery (raw edit/delete/rename) guardrail protocol: `VACUUM INTO` backup on the op's own per-request connection (verified before any mutation, abort-with-no-row-touched on failure), the mutation + FTS5 `('rebuild')` re-sync + `projection_checkpoints`-rewind-to-0 dirty signal sharing one transaction, and deliberate stream/global-position gap tolerance on delete. -- 2026-07-22 -- `knowledge/decisions/0034-event-surgery-guardrails.md`
- **0033** -- Each request and each long-running SSE operation opens and disposes its own `SqliteConnection` from a shared `unit -> SqliteConnection` factory (per-connection pragmas re-applied on open, pooled by connection string); retires ADR-0030's `requestDbLock` and closes the residual read×write race it accepted, while ADR-0028's `jobConn`/`jobDbLock` remain untouched. **Supersedes 0030.** -- 2026-07-22 -- `knowledge/decisions/0033-per-request-connection-factory.md`
- **0032** -- Compensating-event composer validates and canonicalizes an operator's corrective event by round-tripping it through the owning BC's existing `serialize`/`deserialize` seam (prefix-dispatched like `formatEvent`, reflection and template-registries rejected); the re-serialized canonical bytes are what get appended (expected-position checked, under the ADR-0030 request lock), so a composer event is indistinguishable from an organic one except for `{"source":"admin-console"}` metadata. -- 2026-07-22 -- `knowledge/decisions/0032-compensating-event-composer-round-trip-validation.md`
- **0031** -- Drift detector replays the event log into a throwaway `SqliteConnection` (temp/`:memory:`) per handler and diffs row-by-row against live projection tables, rather than table-name prefixing or `ATTACH` — so unmodified handler code runs verbatim and read-only-against-live holds by construction; gated by the not-dirty guard, streamed over its own single-flight SSE route. -- 2026-07-22 -- `knowledge/decisions/0031-projection-drift-detector-throwaway-shadow-connection.md`
- **0030** -- A single process-wide `SemaphoreSlim(1,1)` (`requestDbLock`) guards the 3 request-reachable transaction-opening choke points on the shared request `SqliteConnection` (`Api.executeCommand`, `GameJournal.save`, `importNdjson`), generalizing ADR-0028's per-command-lock idiom to the request connection; the residual read×write race is accepted-not-closed, and the full per-request-connection migration is deferred to administration-mz6kp. -- 2026-07-22 -- `knowledge/decisions/0030-request-connection-narrow-semaphore-gate.md`
- **0029** -- Event-log NDJSON export/import: opaque JSON-escaped-string payload embedding for byte-stable round-trips, explicit-position INSERT bypassing `appendToStream` to preserve `global_position` into an empty store only, "leave projections dirty, reuse Rebuild-all" over self-rebuilding, and an asymmetric transport (plain stream out, SSE-progress in). -- 2026-07-22 -- `knowledge/decisions/0029-ndjson-event-log-export-import.md`
- **0028** -- Scheduled jobs use a dedicated `SqliteConnection` plus a per-command `SemaphoreSlim` (not the shared request connection), closing both the 5s catch-up and the nightly same-hour (04:00) job×job / job×request races; corrects ADR-0024/0026's premise that WAL + `busy_timeout` made one shared connection thread-safe. -- 2026-07-22 -- `knowledge/decisions/0028-scheduled-jobs-dedicated-connection-and-per-command-lock.md`
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
