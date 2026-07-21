---
id: 0026
title: Scheduled-job runs are recorded through a shared registry and an injected recorder seam; run-now is fire-and-forget with a startup-reconciled running row and a name-keyed in-memory guard
scope: administration
status: accepted
date: 2026-07-21
supersedes: []
superseded_by: []
related_tasks: [administration-yamm5]
related_research: []
---

# ADR 0026: Scheduled-job runs are recorded through a shared registry and an injected recorder seam; run-now is fire-and-forget with a startup-reconciled running row and a name-keyed in-memory guard

## Context

The two scheduled jobs (`Steam playtime sync`, `Series TMDB refresh`) report only
to stderr through `ScheduledJobs.runJobSafe` (`src/Server/ScheduledJobs.fs`), the
single choke point that already wraps every fire in a try/catch and logs
start/finish. Their history evaporates: "did last night's sync run, and what did
it do?" is unanswerable from the app. administration-yamm5 adds a durable
`job_runs` record per run and a `/admin/jobs` tab showing, per job, the last
outcome + summary, a recent-run history, the next scheduled fire time, and a
"Run now" button.

Three product decisions were locked before design and are not reopened here:

1. **Run-now is fire-and-forget + poll.** The trigger returns immediately after a
   `running` row exists; the tab polls until that row resolves. The try/finally
   around a run must guarantee the row *always* reaches a terminal outcome, and a
   row left `running` by a hard process crash must be reconciled.
2. **Scope is exactly the two existing `ScheduledJobs.JobSpec` entries**, driven
   by a shared registry (the same "one source of truth" shape as
   `projectionHandlers`) so future `JobSpec` entries auto-appear. No Jellyfin
   sync, director backfill, or GameJournal migration.
3. **Retention keeps all rows, no pruning** (ADR-0021's "defer materialization
   until a real threshold" posture).

Four things needed a deliberate technical answer, not a default:

- The `JobSpec.Run: unit -> Async<unit>` choke point knows start/finish and
  ok-vs-exception, but the *summary* (counts) is computed inside the job body and
  thrown away by the `unit` return. It has to survive to the recorder.
- The registry that both `ScheduledJobs.startAll` and the Administration surface
  read has to be one list, not two.
- Recording (the `job_runs` table) is an Administration-BC concern, but the choke
  point that fires it lives in `ScheduledJobs.fs` (Integration), which compiles
  *before* `Administration.fs` (`Server.fsproj`: ScheduledJobs at 46,
  Administration at 51). The dependency can only point one way.
- A `running` row from a hard crash, and a second concurrent trigger of the same
  job, both need concrete mechanisms.

## Decision

### `JobSpec.Run` returns a `JobRunOutcome`; the summary flows to the recorder

`ScheduledJobs.JobSpec.Run` changes from `unit -> Async<unit>` to
`unit -> Async<JobRunOutcome>`, where (both types defined in `ScheduledJobs.fs`):

```fsharp
/// The disposition a job body reports for a run that ran to completion. A
/// thrown exception is NOT one of these — the runner turns an uncaught
/// exception into a terminal 'error' outcome (see tryStartJob).
type JobDisposition =
    | Ok        // ran and did its work
    | Skipped   // ran, but declined to act (e.g. an API key isn't configured)

type JobRunOutcome = {
    Disposition: JobDisposition
    /// One-line human summary — the counts the body already formats for stderr,
    /// e.g. "12 sessions, 8 snapshots, 1 game created, 0 promoted to focus".
    Summary: string
}
```

The two job *modules* do **not** depend on `ScheduledJobs` (they compile before
it): they keep returning their own natural result types, and the `Async<JobRunOutcome>`
mapping happens in the registry bodies in `Composition.fs`, which sees every
module. `PlaytimeTracker.runSync` already returns `Result<PlaytimeSyncResult,
string>` (`Ok r` → `{ Disposition = Ok; Summary = "…counts…" }`; `Error e` →
`{ Disposition = Skipped; Summary = e }`, preserving today's "sync skipped"
semantics). `SeriesRefresh.runNightlyJob` changes from `Async<unit>` to return a
small `SeriesRefreshSummary` record ({ Refreshed; Errors; NewEpisodes;
StatusTransitions; Skipped }) — the counts it currently only `eprintfn`s — which
the registry body formats into the summary string.

### One registry, built in `Composition.fs`, consumed by both sides

Mirroring `projectionHandlers` (a single `Composition.fs` `let` passed to both
`Projection.startAllProjections` and `Administration.create`), the
`scheduledJobs : ScheduledJobs.JobSpec list` binding stays in `Composition.fs`
but is now passed to **both** `ScheduledJobs.startAll` **and**
`Administration.create`. Its binding (and the `playtime_sync_hour` /
`series_refresh_hour` settings reads it depends on) moves up above the
`Administration.create` call (currently ~line 219; the registry is currently
defined far below at ~287). A new `JobSpec` entry appended to this one list is
scheduled, listed, run-now-able, and recorded with no further wiring — the
"future JobSpec entries auto-appear" requirement.

### The recorder seam: `ScheduledJobs` stays decoupled; `Administration` owns the table and the guard

`job_runs` (schema + CRUD) and the concurrency guard are **owned by the
Administration BC and live in `Administration.fs`**, alongside the existing
`rebuildingProjections`, `projectionTables`, and `imageRefColumns`
admin-console-only state (ADR-0024, ADR-0025). Because `ScheduledJobs.fs`
compiles first and must not depend on `Administration.fs`, the choke point calls
through an **injected seam** defined in `ScheduledJobs.fs` and implemented by
`Administration.fs`:

```fsharp
/// Injected by Composition so the generic runner records runs and enforces the
/// single-run-per-job guard without ScheduledJobs depending on Administration.
/// Both the scheduled timer AND the manual run-now trigger call through the
/// same seam instance, so the guard and the recorded row are identical
/// regardless of trigger source.
type JobRunRecorder = {
    /// Atomically claim the run slot. false => a run (scheduled or manual) of
    /// this job is already in flight; the caller must not run.
    TryClaim: string -> bool
    Release: string -> unit                       // always called in finally
    BeginRun: string -> string -> int64           // jobName -> trigger -> new running-row id
    Complete: int64 -> JobDisposition -> string -> unit
    Fail:     int64 -> string -> unit             // uncaught exception -> 'error'
}
```

`runJobSafe` is replaced by one shared primitive, `ScheduledJobs.tryStartJob`,
that both trigger paths use so the terminal-outcome guarantee and the guard are
written exactly once:

```fsharp
/// claim -> begin('running') -> Run -> terminal, with the try/finally that makes
/// the row NEVER stay 'running' and ALWAYS release the slot, even on exception.
/// Returns Error () if the slot was already claimed (rejected, no row written);
/// Ok (runId, body) once the running row exists. The caller decides whether to
/// await `body` (scheduled timer) or Async.Start it and return runId now (run-now).
let tryStartJob (rec: JobRunRecorder) (spec: JobSpec) (trigger: string)
    : Result<int64 * Async<unit>, unit> =
    if not (rec.TryClaim spec.Name) then Error ()
    else
        let runId = rec.BeginRun spec.Name trigger
        let body = async {
            try
                try
                    let! outcome = spec.Run ()
                    rec.Complete runId outcome.Disposition outcome.Summary
                with ex ->
                    rec.Fail runId ex.Message
            finally
                rec.Release spec.Name
        }
        Ok (runId, body)
```

- **Scheduled timer** (`startTimer`): `match tryStartJob rec spec "scheduled"
  with Ok (_, body) -> do! body` (awaited, so rescheduling still happens after
  the run finishes, preserving today's non-drifting behavior) `| Error () ->
  eprintfn "…skipped, already running"`.
- **Run-now** (`IAdminApi.runJobNow`, in `Administration.create`): `tryStartJob
  rec spec "manual"`; on `Ok (runId, body)` it `Async.Start body` and returns
  `Started runId` **immediately** — the fire-and-forget half of locked decision
  #1; on `Error ()` returns `Rejected`.

`Composition.fs` builds one recorder from an Administration-exposed builder,
`Administration.makeJobRunRecorder conn` (closing over the module-private guard +
CRUD), and passes that single instance to `ScheduledJobs.startAll` (new
parameter) so the timer and `Administration.create`'s run-now share the same
guard dictionary and the same `conn`.

### `job_runs` schema

```sql
CREATE TABLE IF NOT EXISTS job_runs (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    job_name     TEXT NOT NULL,
    trigger      TEXT NOT NULL,   -- 'scheduled' | 'manual'
    status       TEXT NOT NULL,   -- 'running' | 'ok' | 'error' | 'skipped' | 'interrupted'
    summary      TEXT,            -- NULL while running
    started_at   TEXT NOT NULL,   -- ISO-8601 UTC
    finished_at  TEXT             -- NULL while running
);
CREATE INDEX IF NOT EXISTS idx_job_runs_name_started
    ON job_runs (job_name, started_at DESC);
```

The `status` string is the DB form of `JobDisposition` plus the two states no job
body reports: `running` (set by `BeginRun`), `error` (set by `Fail` on an
uncaught exception), and `interrupted` (set by startup reconciliation, below).
`Administration` maps this string to the client-facing `JobRunStatus` DU.

**Why an index here but ADR-0021 deferred materialization.** The tab's primary
query is per-job, ordered, limited — "the newest N runs for this `job_name`" and
"the single last run per job" — which the composite `(job_name, started_at DESC)`
index answers as an indexed range + limit, and idiomatically matches the existing
`idx_events_*` indexes. That is *not* the thing ADR-0021 declined: ADR-0021
declined a speculative *materialized summary / caching layer*, not a basic index.
The index is one line of cheap, non-speculative insurance. (At ~2 rows/day, ~730
rows/year with all-rows retention, even an unindexed scan would be trivial —
which is exactly why keeping-all-rows costs nothing and no pruning is needed,
consistent with locked decision #3 and ADR-0021's threshold reasoning.)

### Crash reconciliation happens on startup only, never on read

`Administration.initializeJobRuns conn` (called from `Composition.fs`'s init
sequence, after the table exists and before `ScheduledJobs.startAll`) runs:

```sql
UPDATE job_runs
   SET status = 'interrupted',
       finished_at = <now UTC>,
       summary = 'Interrupted — server restarted while this run was in progress'
 WHERE status = 'running';
```

Startup is the **only** safe reconciliation point, and it is unambiguous: a
`running` row is only ever genuinely in-flight within a single process lifetime,
and at startup the in-memory guard (`runningJobs`) is empty, so *any* `running`
row present at startup is definitionally orphaned — its owning process is gone.
This mirrors ADR-0024's reasoning that a lock surviving a restart is meaningless;
here we additionally leave a durable `interrupted` breadcrumb instead of silently
forgetting.

**Reconcile-on-read is explicitly rejected.** Within a live process a `running`
row may be a genuinely in-flight run-now job; read-time reconciliation cannot
distinguish that live run from a crashed orphan (both look like `status =
'running'` with no `finished_at`), so it would falsely mark a running job
interrupted. Startup is the one moment where the ambiguity does not exist.

### Concurrency guard: a name-keyed in-memory `ConcurrentDictionary`, identical for both triggers

`Administration.runningJobs : ConcurrentDictionary<string, unit>` — module-level,
process-lifetime state, an exact structural copy of `rebuildingProjections`
(ADR-0024). `TryClaim` is `TryAdd job_name`, `Release` is `TryRemove job_name`,
both atomic without an explicit lock. Because both the scheduled timer and the
run-now trigger reach the guard only through `tryStartJob`, which calls
`rec.TryClaim spec.Name`, the key is **the job name for both trigger sources** —
a manual run and a scheduled fire of the same job contend on the same dictionary
key and can never both hold it. A DB-persisted lock is unnecessary for the same
reason ADR-0024 gave: the guard only needs process-lifetime durability, and a
crashed run's `running` row is handled by startup reconciliation, not by a
persisted lock flag.

### Cross-BC touchpoint, stated plainly

The instrumentation lives in Integration's job code (`ScheduledJobs.fs`, and the
richer return values from `PlaytimeTracker`/`SeriesRefresh`) while the durable
record, the guard, the read API, and the tab are Administration's — consistent
with the context map's "Administration owns the operational surface." The seam
(`JobRunRecorder`) is the explicit, one-directional dependency edge that lets the
earlier-compiled Integration choke point call the later-compiled Administration
store without `ScheduledJobs` knowing the store exists.

## Consequences

### Positive
- The terminal-outcome guarantee (locked decision #1) is written exactly once, in
  `tryStartJob`'s try/finally, and is shared verbatim by the scheduled timer and
  run-now — a fix to one is a fix to both, the same property ADR-0024 valued for
  the rebuild stream.
- The concurrency guard reuses `rebuildingProjections`'s proven four-line
  `TryAdd`/`finally TryRemove` shape with no new primitive, and is provably
  collision-free across trigger sources because both paths key on `spec.Name`.
- One registry means a future `JobSpec` auto-appears on the tab, run-now, and
  recording with zero extra wiring — the `projectionHandlers` property, applied
  to jobs.
- No SSE/streaming infrastructure: run-now is a plain Remoting request that
  returns a row id, and the tab reuses ADR-0023's epoch-guarded polling Cmd —
  fire-and-forget + poll, exactly as locked.

### Negative / accepted tradeoff
- `SeriesRefresh.runNightlyJob`'s signature changes (`Async<unit>` →
  `Async<SeriesRefreshSummary>`) so its counts survive to the recorder — a small
  Integration-side edit, plus the `JobSpec.Run` type change touches both registry
  bodies in `Composition.fs`. Contained and mechanical.
- The recorder seam adds one indirection (a record of five functions) between the
  choke point and the store. Justified by the one-way compile-order constraint;
  the alternative (moving the store to an early-compiled file) would misplace an
  Administration concern purely for linkage.
- An `interrupted` row is only ever written at the *next* startup, not at crash
  time (a hard crash can't run cleanup) — so a crashed run shows as `running` on
  the tab until the server is restarted. Acceptable: a hard crash means the
  server is down anyway, and the operator's next interaction is a restart.

### Neutral
- Run-now lets an operator start a genuinely write-heavy job (Steam sync: network
  fetch + writes) concurrently with normal browsing queries on the single shared
  `SqliteConnection`. This is the same single-writer serialization (WAL + 5s
  `busy_timeout`) ADR-0024 already examined and accepted for the shared
  connection; run-now adds one more human-initiated writer but does not change
  the model. Flagged, not re-architected — recorded so a future connection-model
  revisit knows this trigger was considered here. The guard prevents same-job
  overlap; two *different* jobs (Steam sync + Series refresh) can still run
  concurrently exactly as their two timers already allow, serializing on the
  connection.

## Alternatives considered

- **Record inside each job body instead of the choke point.** Rejected: it would
  duplicate the begin/complete/finally logic per job and lose the single
  terminal-outcome guarantee, the same reason ADR-0024 kept one guarded runner.
  The task's Notes already flag the choke point as preferred.
- **Job modules return `Async<JobRunOutcome>` directly.** Rejected on compile
  order: `PlaytimeTracker`/`SeriesRefresh` (44/45) compile before `ScheduledJobs`
  (46) where `JobRunOutcome` is defined. Mapping in the `Composition.fs` registry
  bodies (which see everything) keeps the domain modules decoupled from
  `ScheduledJobs` and needs no new shared type in an early file.
- **Put `job_runs` + the guard in an early-compiled `JobRunStore.fs`** so
  `ScheduledJobs` calls it directly with no seam. Rejected: it would place an
  Administration-owned concern early purely for linkage and give `ScheduledJobs`
  a hard dependency on a persistence store, whereas today it is a pure mechanism.
  The injected seam keeps ownership in `Administration.fs` next to
  `rebuildingProjections`/`imageRefColumns` and keeps `ScheduledJobs` persistence-
  agnostic.
- **A DB-persisted run lock instead of a `ConcurrentDictionary`.** Rejected for
  the same reason as ADR-0024: the guard only needs process-lifetime durability,
  and a crashed run is handled by startup reconciliation of the `running` row —
  a persisted lock would be redundant ceremony with a second source of truth.
- **Reconcile stale `running` rows on read (or on both read and startup).**
  Rejected: read-time reconciliation cannot distinguish a live in-flight run-now
  job from a crashed orphan, so it would mis-mark live runs. Startup — where the
  in-memory guard is provably empty — is the only unambiguous point.
- **SSE-stream the run-now progress** like `projectionRebuildStreamHandler`
  (ADR-0024). Rejected because locked decision #1 chose fire-and-forget + poll:
  a job's progress is a single terminal outcome + summary, not a per-event
  stream, so a Remoting call returning a row id + ADR-0023 polling is the lighter
  fit; SSE's long-lived response and manual framing would be unearned complexity.

## References
- `src/Server/ScheduledJobs.fs` — `JobSpec.Run` (type change), `JobDisposition`/
  `JobRunOutcome`/`JobRunRecorder` (new), `tryStartJob` (replaces `runJobSafe`),
  `startAll` (new recorder parameter), `nextRun` (reused for next-fire time).
- `src/Server/Administration.fs` — `runningJobs` (new, mirrors
  `rebuildingProjections`), the private `job_runs` CRUD, `makeJobRunRecorder`,
  `initializeJobRuns` (table + startup reconciliation), and the new
  `getJobStatuses` / `runJobNow` members of `create` (now also takes the
  `scheduledJobs` list).
- `src/Server/Composition.fs` — the `scheduledJobs` registry (moved above
  `Administration.create`), the `JobRunOutcome` mapping in each job body, the
  recorder wiring into `startAll`, and the `initializeJobRuns` init call.
- `src/Server/PlaytimeTracker.fs` — `PlaytimeSyncResult` (mapped to a summary,
  unchanged); `src/Server/SeriesRefresh.fs` — `runNightlyJob` returns a
  `SeriesRefreshSummary` (signature change).
- `src/Shared/Shared.fs` — `JobRunStatus`, `JobRunDto`, `JobStatusDto`,
  `RunJobResult`, and the `IAdminApi.getJobStatuses` / `runJobNow` members.
- `src/Client/Pages/AdminJobs/` — `Types.fs`/`State.fs`/`Views.fs`; `Router.fs`
  already has `AdminJobs` at `/admin/jobs`.
- ADR-0023 — epoch-guarded self-rescheduling polling `Cmd` (reused for the tab's
  run-now poll, and its navigation-teardown lesson).
- ADR-0024 — `rebuildingProjections` guard shape, the shared-connection
  reasoning, and the "a restart-surviving lock is meaningless" premise.
- ADR-0025 — the `imageRefColumns`/`projectionTables` admin-owned-registry pattern.
- ADR-0021 — retention / "defer materialization until a real threshold," the
  precedent for keeping all rows and for the index-vs-materialization distinction.
- ADR-0007 — single-user/single-operator premise underpinning the concurrency
  reasoning.
