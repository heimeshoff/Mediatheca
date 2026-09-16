---
id: ADR-0078
title: Goodreads' manual "Sync now" shares ScheduledJobs' JobRunRecorder via a result-capturing wrapper JobSpec, not a bespoke un-recorded trigger
scope: integration
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
related_tasks: [integration-wmqn3]
related_adrs: [0026, 0075]
---

# ADR 0078: Goodreads' manual "Sync now" shares the job-runs recorder via a result-capturing wrapper JobSpec

## Context

ADR-0026 gives two existing shapes for triggering a job body: the scheduled timer (awaits
`tryStartJob`'s body directly) and the Jobs tab's generic `IAdminApi.runJobNow` (fire-and-forget,
`Async.Start`, returns `RunJobStarted runId`/`RunJobRejected` immediately — the tab polls
`getJobStatuses` for the outcome). Neither shape hands the caller the job body's own typed return
value: the scheduled timer discards it (only the `JobRunOutcome.Disposition`/`Summary` strings
survive, into `job_runs`), and `runJobNow`'s fire-and-forget contract structurally cannot return one
either. A third, older shape also exists — `triggerPlaytimeSync` (`Api.fs`, predating ADR-0026) calls
`PlaytimeTracker.runSync` directly under its own ad-hoc lock, bypassing the recorder entirely: no
`job_runs` row, no overlap guard against the nightly fire.

This task's Settings card needs its own "Sync now" button to (a) show the typed `GoodreadsSyncResult`
(per-shelf counts) in an inline alert immediately, synchronously, the same click, and (b) be guarded
against colliding with the 05:00 scheduled fire, and (c) show up in the job's own `job_runs` history
like every other trigger — none of the three existing shapes satisfies all three at once.

## Decision

`Composition.fs` builds `runGoodreadsShelfSyncNow : unit -> Async<Result<GoodreadsSyncResult, string>>`
by constructing a **wrapper `JobSpec`** that carries the SAME `Name` as the registered "Goodreads
shelf sync" spec (so `tryStartJob`'s name-keyed guard is the identical slot the scheduled timer
claims) but whose `Run` closes over a local mutable `resultCell` and stashes the real,
typed `Result<GoodreadsSyncResult, string>` into it before returning the `JobRunOutcome` the
recorder needs. The caller runs `tryStartJob jobRunRecorder wrappedSpec "manual"`, `do!`-awaits the
returned body (rather than firing-and-forgetting it, since the Settings card's click IS the "wait for
the result" moment), and reads `resultCell` afterward.

## Alternatives considered

- **Reuse `triggerPlaytimeSync`'s older, un-recorded shape.** Rejected: loses the `job_runs` audit
  trail (the task's own acceptance criterion is "Sync now records a manual run") and the overlap
  guard against the nightly fire — the exact gap ADR-0026 closed for every other job.
- **Widen `ScheduledJobs.tryStartJob`'s own signature to hand back a generic `'a` result.** Rejected:
  a wider, riskier change to a primitive every job in the registry depends on, for the sake of one
  caller's need; `ScheduledJobs.fs` compiles before every job module and must stay generic over
  `JobRunOutcome`, not a per-job payload type.
- **Fire-and-forget + poll, the `runJobNow`/Jobs-tab shape.** Rejected for this specific button: the
  Settings card's own inline alert is meant to resolve on the same click, not via a second poll loop
  duplicating the Jobs tab's own polling UI for one card.

## Consequences

- A future adapter that wants its own synchronous "Sync now" button with a typed result (Audible's
  progress sync, `integration-jjvg2`) can reuse this exact wrapper-`JobSpec` shape rather than
  re-deriving it or falling back to the un-recorded `triggerPlaytimeSync` precedent.
- The wrapper `JobSpec`'s `Run` is a one-shot closure (a fresh `resultCell` per call) — never
  registered in the real `scheduledJobs` list, so it never appears in `ScheduledJobs.startAll`'s own
  timers; only its `Name` (for the guard) and `Run` (for the work) are borrowed.
- `job_runs` gains a `trigger = "manual"` row per Settings-card sync exactly like every other job's
  manual trigger, with no special-casing in `Administration.fs`.
