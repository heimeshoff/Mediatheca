---
id: administration-yamm5
title: Job runs console — history, outcomes, and run-now for scheduled jobs
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, jobs, observability]
related_adrs: [0026]
related_research: []
prior_art: []
---

## Why
The two scheduled jobs (Steam playtime sync, Series TMDB refresh — `ScheduledJobs.fs` / `Composition.fs`) report only to stderr; their history evaporates. "Did last night's sync run? What did it do?" is unanswerable from the app.

## What
Every fire of a scheduled job — whether the daily timer or an operator "Run now" — is recorded as a durable **job run** (job name, trigger source, terminal outcome, one-line summary, start/finish timestamps) through the single choke point in `ScheduledJobs.fs`, which guarantees a run can **never** be left stuck `running`. A **Jobs tab** (`/admin/jobs`) shows, per job: next scheduled fire time, last outcome + summary, and a recent-run history. A **"Run now"** button triggers the job immediately and returns before it completes (fire-and-forget); the tab polls run history until the new row resolves. A job already in flight — from either trigger — refuses a second concurrent trigger.

Scope is exactly the two `ScheduledJobs.JobSpec` entries, driven by a **shared job registry** lifted so both `ScheduledJobs.startAll` and `Administration.create` read one list — a future `JobSpec` auto-appears with no extra wiring. History is kept in full (no pruning). The full technical shape is **ADR-0026**.

## Acceptance criteria
- [ ] A **scheduled** run writes a `job_runs` row with `trigger = 'scheduled'`, a terminal `status` (`ok` / `error` / `skipped`), a `summary`, and both timestamps.
- [ ] A **manual** ("Run now") run writes an otherwise-identical row with `trigger = 'manual'`.
- [ ] `runJobNow` returns **before** the job completes (fire-and-forget); the `running` row it created resolves to a terminal status once the job finishes.
- [ ] A run body that ran but declined to act (e.g. Steam API key unconfigured) is recorded `skipped` and renders **distinctly from `error`** on the tab (a config gap is not a failure).
- [ ] An uncaught exception in a job body resolves its row to `error` with the exception message — the row is never left `running` (try/finally terminal-outcome guarantee).
- [ ] A second concurrent trigger of the **same** job (manual-while-scheduled, scheduled-while-manual, or manual-while-manual) is **refused** — no second row, and the running job is unaffected.
- [ ] On **server startup**, any `running` row is reconciled to `interrupted` with a finished timestamp (crash left it orphaned); reconciliation happens on startup only, never on read.
- [ ] The Jobs tab shows, per job: next-fire time (derived from the configured hour via `ScheduledJobs.nextRun`), last outcome + summary, and recent-run history, and the new run appears without a page reload (polling, per ADR-0023).
- [ ] `npm run build` is clean and the Expecto suite passes (add tests for: terminal-outcome on ok/skip/exception, concurrent-trigger refusal, startup reconciliation of a `running` row).

## Notes

**Cross-BC touchpoint** (per context map — "Administration owns the operational surface"): instrumentation lives in Integration's job code (`ScheduledJobs.fs`, and richer return values from `PlaytimeTracker`/`SeriesRefresh`); the durable record, the guard, the read API, and the tab are Administration's. The one-directional dependency edge is an **injected recorder seam** — required because `ScheduledJobs.fs` compiles *before* `Administration.fs` (`Server.fsproj` 46 vs 51), so `ScheduledJobs` must not name the store. See ADR-0026 for the full rationale, alternatives, and the shared-connection note.

### Server shape (from ADR-0026)
- **`ScheduledJobs.fs`:** `JobSpec.Run` changes `unit -> Async<unit>` → `unit -> Async<JobRunOutcome>` (`JobRunOutcome = { Disposition: JobDisposition (Ok | Skipped); Summary: string }`). New `JobRunRecorder` seam record (`TryClaim`/`Release`/`BeginRun`/`Complete`/`Fail`). `runJobSafe` is replaced by one shared `tryStartJob rec spec trigger : Result<int64 * Async<unit>, unit>` holding the try/finally that makes the row never stay `running` and always release the slot. Scheduled timer awaits the body; run-now `Async.Start`s it and returns the run id. `startAll` gains a recorder parameter.
- **`Administration.fs`:** owns `job_runs` (schema + CRUD), the `runningJobs : ConcurrentDictionary<string, unit>` guard (exact copy of `rebuildingProjections`, keyed on job name for both triggers), `makeJobRunRecorder conn`, `initializeJobRuns conn` (table + startup `running` → `interrupted` reconciliation), and new `getJobStatuses` / `runJobNow` members of `create` (which now also takes the `scheduledJobs` list).
- **`Composition.fs`:** the `scheduledJobs` registry binding (and the `playtime_sync_hour`/`series_refresh_hour` reads) moves **above** `Administration.create`; each job body maps its natural result to a `JobRunOutcome`; the single recorder is built via `makeJobRunRecorder` and passed to both `startAll` and `create`; `initializeJobRuns` is called in the init sequence before `startAll`.
- **`PlaytimeTracker.fs`:** `runSync` unchanged (`Result<PlaytimeSyncResult, string>` → `Ok`⇒`Ok`+counts summary, `Error`⇒`Skipped`+message). **`SeriesRefresh.fs`:** `runNightlyJob` changes `Async<unit>` → `Async<SeriesRefreshSummary>` (the counts it currently only `eprintfn`s).

### `job_runs` schema
`id` (PK autoincrement), `job_name`, `trigger` (`scheduled`|`manual`), `status` (`running`|`ok`|`error`|`skipped`|`interrupted`), `summary` (NULL while running), `started_at` (ISO-8601 UTC), `finished_at` (NULL while running). Index `(job_name, started_at DESC)` for the per-job newest-N query.

### Shared / client
- **`Shared.fs`:** new `JobRunStatus` DU, `JobRunDto`, `JobStatusDto`, `RunJobResult`; new `IAdminApi.getJobStatuses` / `runJobNow` members (routed `/api/admin/{Method}`). Run-now is a **plain Remoting call** returning a row id — **not** an SSE route (fire-and-forget + poll was chosen over streaming; ADR-0026 §Alternatives).
- **Client:** new `src/Client/Pages/AdminJobs/` (`Types.fs`/`State.fs`/`Views.fs`), MVU shape mirroring `AdminHealth`/`AdminProjections`; reuse ADR-0023's epoch-guarded polling `Cmd` for the run-now refetch. `Router.fs` already has `AdminJobs` at `/admin/jobs`. Frontend gate satisfied via `design-system-001` (done). **Timezone care:** `NextFireAt` is local time while `started_at`/`finished_at` are stored UTC — label them explicitly rather than rendering both in one zone.

_No split — the architect recommends this stays a single task; both `depends_on` targets (`administration-p0jka`, `design-system-001`) are done._
