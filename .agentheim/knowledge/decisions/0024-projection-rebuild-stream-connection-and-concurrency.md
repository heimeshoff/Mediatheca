---
id: 0024
title: Projection rebuild streams over the shared connection, guarded by an in-memory concurrency lock; "Rebuild all" is client-side orchestration, not a second route
scope: administration
status: accepted
date: 2026-07-21
supersedes: []
superseded_by: []
related_tasks: [administration-qjcp4]
related_research: []
---

# ADR 0024: Projection rebuild streams over the shared connection, guarded by an in-memory concurrency lock; "Rebuild all" is client-side orchestration, not a second route

## Context

administration-qjcp4 replaces the startup-time forced rebuild of
Series/Game (`Composition.fs`, a workaround left over from adding the
`steam_app_id` column) with an explicit operator command: a "Rebuild"
button per projection on the new Projections tab, with live progress, plus
a "Rebuild all". The task's own architecture notes flagged two things that
needed a deliberate answer, not a default:

1. **Connection strategy.** `Projection.rebuildProjection` (drop + init +
   replay) does real writes — dropping and recreating tables, then
   replaying potentially thousands of events. Run on the app's single
   shared `SqliteConnection` (the one `createConnection` opens once in
   `Composition.fs` and threads through `Api.create`, `Administration.create`,
   and every scheduled job), or open a dedicated connection for the
   background rebuild task?
2. **Concurrency guard.** The acceptance criteria require that a second
   rebuild request for a projection already rebuilding not corrupt state —
   "rejected with a visible message, or queued."

A third question came up designing the UI: does "Rebuild all" need its own
server route, or can it reuse the single-projection one?

## Decision

### Reuse the existing shared connection — do not open a second one

`Administration.projectionRebuildStreamHandler` and
`Projection.rebuildProjectionWithProgress` both take the same `conn` that
every other admin/domain handler in this codebase already shares. This
matches the codebase's existing, pre-existing architecture: one
`SqliteConnection` opened at startup (WAL mode, NORMAL sync, 5s
`busy_timeout` — see CLAUDE.md), passed by reference into `Api.create`,
`Administration.create`, `PlaytimeTracker.runSync`'s scheduled job, and
every other background operation this app already runs. Introducing a
second, rebuild-only connection would be a new pattern with no precedent
here, and it doesn't buy correctness: SQLite's single-writer model means a
second writer connection still serializes against the first one via WAL +
`busy_timeout`, the same mechanism the shared-connection approach already
relies on. Reviewing this codebase's actual concurrency profile (a
single-user, single-process admin tool — ADR-0007) confirmed the existing
pattern is adequate; this task doesn't need to be the one that fixes it if
it weren't.

### Concurrency guard: an in-memory `ConcurrentDictionary`, not a DB row

`Administration.rebuildingProjections : ConcurrentDictionary<string, unit>`
is module-level, process-lifetime state. `projectionRebuildStreamHandler`
does `TryAdd` before starting a rebuild and `TryRemove` in a `finally` after
it ends (success or exception); a `TryAdd` failure emits a `rejected` SSE
event instead of running. This is deliberately simpler than a
DB-persisted lock row: the guard only needs to survive the lifetime of one
server process (a lock surviving a restart is meaningless here — a crashed
rebuild leaves the projection's checkpoint at whatever it last saved, which
is safely resumable by just re-running the rebuild, not a state needing a
persisted "still locked" flag to protect against). `ConcurrentDictionary`'s
`TryAdd`/`TryRemove` are atomic without needing an explicit lock statement,
matching the one-flag-per-projection shape of the problem exactly.

### "Rebuild all" is client-side sequential orchestration over the single-projection route — no second server endpoint

`AdminProjections.State`'s `PendingRebuildAllQueue` drives "Rebuild all" by
dispatching `Rebuild_clicked` for one projection name at a time, waiting for
that projection's stream to reach `complete`/`rejected`/`error` before
dequeuing the next. This reuses
`Administration.projectionRebuildStreamHandler` and its concurrency guard
verbatim — no new server route, no new server-side "rebuild these N
projections" orchestration to get right. The tradeoff is that "Rebuild all"
takes N times as long as parallel rebuilds would (six projections rebuilt
one after another instead of six connections writing at once) — accepted
deliberately, since parallel writers to the shared connection would
contend with each other in exactly the way the connection-strategy decision
above says is fine to avoid rather than lean into, and rebuild is an
infrequent, log-scale-bounded operator action for a single-user app, not a
latency-sensitive path.

## Consequences

### Positive
- No new connection-lifecycle code (open/dispose/error-handling for a
  second `SqliteConnection`) to get right under a background `task {}`.
- The concurrency guard is four lines (`TryAdd`/`finally TryRemove`) with an
  atomic, lock-free implementation, exercised by the acceptance criterion
  without any new infrastructure.
- "Rebuild all" shares 100% of its server-side code path with "Rebuild one" —
  a bug fixed in the single-projection stream is fixed for both entry
  points automatically.

### Negative
- "Rebuild all" is sequential, not parallel — the six projections rebuild
  one at a time. For this app's current event volume (low thousands of
  events per the Health tab's own numbers) this is seconds, not minutes;
  revisit if event volume grows enough that sequential full rebuilds become
  operator-noticeable.
- The concurrency guard resets on server restart. A rebuild interrupted by
  a crash/restart leaves `rebuildingProjections` empty again (correct — the
  in-flight rebuild is gone with the process) but does not resume
  automatically; the operator has to notice the checkpoint didn't reach the
  store head and click Rebuild again. Acceptable for a manually-triggered
  admin action.

### Neutral
- The shared-connection choice is not a new decision so much as declining
  to introduce an exception to the existing one — worth recording anyway
  since the task's own notes explicitly asked the question, and a future
  worker revisiting connection strategy for a different task should know
  this was already considered and rejected here, not just defaulted to.

## Alternatives considered

- **Dedicated `SqliteConnection` per rebuild, opened in WAL mode.** The
  task's notes floated this as the "textbook" answer for a
  long-running background write. Rejected: it doesn't change SQLite's
  actual write-serialization behavior (WAL still means one writer at a
  time across *all* connections to the same file), introduces a connection
  lifecycle this codebase has no other precedent for, and the app's actual
  concurrency needs (single user, admin-triggered, infrequent) don't
  require it.
- **DB-persisted rebuild lock (a row/flag in a new or existing table).**
  Rejected as unnecessary ceremony for a guard that only needs
  process-lifetime durability; a `ConcurrentDictionary` already provides
  the atomicity a DB row would, without a schema or a query.
- **Queue instead of reject for a concurrent rebuild request.** The task's
  acceptance criteria explicitly allow either "rejected with a visible
  message" or "queued." Chose reject: a queued second request for the same
  projection is a redundant no-op once the first request finishes anyway
  (both would replay to the same store head), so queuing adds complexity
  (a per-projection queue, its own SSE semantics for "your request is
  waiting") to represent something a rejection message plus "click Rebuild
  again after it's done" already covers with no new state machine.
- **A second server route for "rebuild all" (e.g. one SSE stream
  multiplexing progress for all six projections).** Rejected: it would
  duplicate the guard/progress/error logic already in
  `projectionRebuildStreamHandler` for marginal gain (aggregate progress
  reporting), and the sequential client-side queue gets the same
  functional outcome — every projection rebuilt, one rejection-safe request
  at a time — from code that already exists and is already tested through
  the single-projection path.

## References

- `src/Server/Composition.fs` — `createConnection`, the shared `conn`
  threaded through `Api.create`/`Administration.create`; the retired
  startup-time forced rebuild (now just `Projection.startAllProjections`).
- `src/Server/Projection.fs` — `rebuildProjectionWithProgress`,
  `RebuildProgress`, `getCheckpointInfo`.
- `src/Server/EventStore.fs` — `getMaxGlobalPosition` (store head, for `Lag`
  and a rebuild's progress-bar denominator).
- `src/Server/Administration.fs` — `rebuildingProjections`,
  `projectionRebuildStreamHandler`, `buildProjectionStats`,
  `projectionTables`.
- `src/Client/Pages/AdminProjections/` — `Types.fs`/`State.fs`/`Views.fs`,
  in particular `State.runRebuildStream` and the
  `PendingRebuildAllQueue`/`Start_next_queued_rebuild` sequencing.
- `tests/Server.Tests/ProjectionRebuildTests.fs`,
  `tests/Server.Tests/AdministrationTests.fs` (`getProjectionStats` cases).
- ADR-0002 — event sourcing + CQRS, why projections are disposable/
  rebuildable in the first place.
- ADR-0007 — no auth, single-user/single-process deployment model, the
  premise this ADR's concurrency-profile reasoning leans on.
