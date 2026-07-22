---
id: 0028
title: Scheduled jobs get a dedicated SqliteConnection plus a per-command SemaphoreSlim, correcting ADR-0024/0026's shared-connection safety premise
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-tj8n2]
related_research: []
---

# ADR 0028: Scheduled jobs get a dedicated SqliteConnection plus a per-command SemaphoreSlim, correcting ADR-0024/0026's shared-connection safety premise

## Context

administration-da908 (the Playwright e2e harness spike) surfaced a real,
process-fatal production bug while standing up its `webServer`: starting the
real server against a fresh, empty `DATA_DIR` and leaving it running past the
~5s mark reliably crashed the whole process with an unhandled
`System.ArgumentOutOfRangeException` inside
`Microsoft.Data.Sqlite.SqliteConnection.RemoveCommand`, called from
`SqliteCommand.Dispose`, called from `Administration.insertRunningRow`.

**Root cause.** `ScheduledJobs.startTimer` registers one `System.Threading.Timer`
per `JobSpec`, and both jobs (Steam playtime sync, Series TMDB refresh) share a
5-second catch-up delay after startup. Both timers fire on separate ThreadPool
threads at essentially the same instant and both touch the single shared
`SqliteConnection` (`conn`, opened once in `Composition.buildApp`).
`Microsoft.Data.Sqlite.SqliteConnection` is not thread-safe for concurrent
command creation/disposal from multiple threads — the race corrupts the
connection's internal open-command list, and the resulting unhandled exception
on a ThreadPool thread is process-fatal in .NET (no request-thread middleware
catches it).

**Not a startup-only artifact.** Both jobs default to `Hour = 4`
(`playtime_sync_hour`/`series_refresh_hour`, both `Option.defaultValue 4`), and
`ScheduledJobs.nextRun` computes an exact `DateTime(y, m, d, 4, 0, 0, Local)`
with no jitter. So on default configuration the two regular daily timers also
fire at exactly `04:00:00.000` local every night — the 5s catch-up merely made
the collision trivially reproducible on demand. The race also extends past the
recorder (`insertRunningRow`/`completeRun`/`failRun`) into the job bodies
themselves (`PlaytimeTracker.runSync`, `SeriesRefresh.runNightlyJob`, and
`SeriesRefresh.refreshOne`'s DB touches), which run on the same shared `conn`
for seconds at a stretch doing real reads/writes.

**This corrects a stated premise in ADR-0024 and ADR-0026.** Both ADRs
reasoned that WAL mode + a 5s `busy_timeout` make the single shared `conn`
"adequate" / "safe" for concurrent use, given the app's single-user,
single-process profile (ADR-0007). That reasoning conflates two different
things: SQLite's file-level write serialization *across separate connections*
(what WAL + `busy_timeout` actually protect), and .NET client-side
thread-safety of *one connection object* touched by two threads at once
(what `Microsoft.Data.Sqlite.SqliteConnection` does not provide). The former
is fine; the latter is exactly what crashed the process. ADR-0024's
"single-writer model means a second writer connection still serializes
against the first" reasoning remains correct for *why a second connection
doesn't need to fight the first over writes* — it just doesn't establish
object-level thread-safety, which is the actual bug here.

`administration-da908` worked around this for its own e2e harness only, via an
opt-in `MEDIATHECA_DISABLE_SCHEDULED_JOBS=1` env var that skipped
`ScheduledJobs.startAll` entirely. That was a harness accommodation, not a
fix — the race was still live for every real dev/Docker deployment.

## Decision

### A dedicated `SqliteConnection` for all scheduled-job DB access

`Composition.buildApp` opens one additional connection to the same database
file (`jobConn = createConnection dbPath`), used exclusively by: the job-runs
recorder (`Administration.makeJobRunRecorder`) and both job bodies
(`PlaytimeTracker.runSync`, `SeriesRefresh.runNightlyJob`, including their
private per-series/per-game helpers). It is never shared with request threads
or the general-purpose `conn`. SQLite supports multiple connections to the
same WAL-mode file cheaply, and this separation alone closes **job×request**:
jobs no longer touch the same connection *object* that request threads use, so
whatever a request handler does to `conn` can never race a job's use of
`jobConn` at the ADO.NET object level (they can still interleave at the SQLite
file level, which WAL + `busy_timeout` already handle, per ADR-0024).

### A `SemaphoreSlim(1, 1)` acquired around each job's individual DB-touching sections — never across an awaited HTTP call

`jobDbLock`, one instance built alongside `jobConn`, is threaded into the
recorder and both job bodies. It closes **job×job** (both the catch-up
collision and the recurring same-Hour nightly collision): whichever job's
thread is mid-command on `jobConn` holds the lock; the other blocks briefly
rather than corrupting the connection. Deliberately **not** a lock around the
whole job body — that would violate ADR-0026's explicit "two different jobs
can run concurrently." Each production call site acquires the lock only for a
contiguous, already-synchronous run of SQL commands (a single helper-function
call in `PlaytimeTracker.runSync`, or one series' pre-fetch/post-fetch DB
sections in `SeriesRefresh`'s job-only `refreshOneForJob`), releasing it before
any `let!`/`do!` that awaits actual network I/O — so the two jobs' HTTP calls
(Steam/RAWG/TMDB fetches, image downloads) still fully overlap; only their
brief DB moments serialize.

`SeriesRefresh.refreshOne` (the function `runNightlyJob` used before this fix)
is also called, unlocked, from the request-serving `conn` by the manual
"Refresh from TMDB" action in the series detail page (`Api.fs`) — that request
path has no job×job race to guard against, so `refreshOne`'s signature was left
untouched rather than adding a lock parameter only one caller needs.
`SeriesRefresh.fs` instead gained a private, job-only sibling,
`refreshOneForJob`, that reimplements the same three-phase shape (sync DB
pre-fetch → unlocked HTTP fetch → sync DB post-fetch) with `jobLock` around
the two DB phases. This duplicates roughly the tail of `refreshOne`'s event-
append logic, accepted as a small, contained, well-documented cost in exchange
for not touching a function shared with the request path (and not touching
`Api.fs`'s manual-refresh call site) for a job-only concern.

`PlaytimeTracker.runSync`'s own private helper `createGameFromSteam` is used
only by `runSync` (not shared with any request path), so it was extended in
place with the same `jobLock` parameter and locks its own DB-only sections
(slug generation, the final event-append chain) without duplicating any logic.

### `triggerPlaytimeSync` (the pre-existing manual "sync now" button on `IMediathecaApi`, `Api.fs`) gets its own uncontended lock

`PlaytimeTracker.runSync`'s new `jobLock` parameter is a required part of its
signature, so `Api.fs`'s existing manual-trigger call site (unrelated to
scheduled jobs; runs on the request-serving `conn`) needed *a* semaphore to
compile. It gets its own, freshly-created, never-shared-with-jobs
`SemaphoreSlim(1, 1)` (`manualSyncTriggerLock`) — functionally a no-op today
(nothing else contends for it), existing only to satisfy the signature and to
keep two overlapping manual-trigger clicks on `conn` from racing each other.
This does **not** close the manual-trigger-vs-scheduled-job case (they're on
different connections already, so no crash risk) nor the broader
request×request question — both untouched here, the latter explicitly
deferred to **administration-cx92m**.

### `MEDIATHECA_DISABLE_SCHEDULED_JOBS` is retired

The env var existed solely to dodge this exact crash for the Playwright e2e
harness. With the race fixed for real, the harness no longer needs an escape
hatch — `Composition.fs` starts jobs unconditionally now, same as every other
environment, and `playwright.config.ts` no longer sets the variable.

### Regression test: real temp-file connection, concurrent execution, explicit same-Hour case

`tests/Server.Tests/JobConnectionConcurrencyTests.fs` drives the real,
unmodified `ScheduledJobs.tryStartJob` choke point and the real, unmodified
`Administration.makeJobRunRecorder`, against a real temp-file
`SqliteConnection` (not `:memory:`), with five `JobSpec`s fired concurrently on
their own background `Task`s — two of them sharing `Hour = 4` (the real
production default for both real jobs), reproducing the actual recurring
nightly collision, not just the catch-up window. Job bodies are test-local
closures performing genuine, repeated SQLite command creation/execution/
disposal through the same lock+connection pattern the production job bodies
use (this codebase has no HTTP-mocking infrastructure to drive
`PlaytimeTracker.runSync`/`SeriesRefresh.runNightlyJob` through their full
business logic without live Steam/TMDB credentials, so the test exercises the
mechanism directly rather than the specific domain logic). Asserts no
exception across all concurrent runs, exactly one terminal `ok` `job_runs` row
per job, and that every individual locked write landed (no silently dropped or
duplicated command under load). A second test stress-tests the recorder alone
across 20 concurrently-firing job names. The manual repro (empty `DATA_DIR`,
`dotnet run`, wait >5s) was also re-run and confirmed the server survives past
the catch-up mark with both jobs' log lines visibly interleaved (proof they
ran concurrently) and `/health` still responding afterward.

## Consequences

### Positive
- The exact crash (`insertRunningRow` on a corrupted `SqliteConnection`
  command list) cannot recur — jobs never share a connection *object* with
  each other or with request threads.
- ADR-0026's "two different jobs can run concurrently" is preserved by
  construction: the lock only ever guards a brief, already-synchronous DB
  section, never an awaited HTTP call.
- The regression test is a permanent, CI-run proof of the fix, replacing "wait
  5s and watch it not crash" with something the Expecto suite runs every time,
  including the same-Hour case the catch-up window alone wouldn't have caught.
- `MEDIATHECA_DISABLE_SCHEDULED_JOBS` — a real, if narrow, source of e2e/
  production behavior divergence — is gone.

### Negative / accepted tradeoffs
- `SeriesRefresh.fs` now carries two similar implementations of "refresh one
  series" (`refreshOne` for the request path, `refreshOneForJob` for the job
  path) rather than one. Accepted to avoid touching `refreshOne`'s signature
  (and its `Api.fs` call site) for a job-only concern; a future change to the
  series-refresh event-append logic must be made in both places.
- `refreshOneForJob`'s lock is held across `applyToProjection` plus the event-
  append chain for one series — coarser than "one SQL command," closer to
  "one series' worth of DB work." This is a deliberate, documented tradeoff:
  finer-grained locking would require decomposing `refreshOne`'s DB writes
  into more pieces than the shared logic naturally has, for a race window
  (two jobs' DB moments landing in the exact same instant) that per-series
  granularity already closes correctly. `PlaytimeTracker`'s locking is finer
  (per helper-function call) since its loop structure already separated sync
  DB work from the one awaited HTTP sub-step per game.
- One more long-lived connection + one more `SemaphoreSlim` for the process to
  hold; negligible for a single-user app already holding a shared `conn`, an
  event store, and an image cache.

### Neutral
- Request×request concurrency on the shared `conn` (unrelated to this fix) is
  unchanged and remains open — administration-cx92m.
- This ADR's correction of ADR-0024/0026's shared-connection-safety premise
  does not retroactively change either decision's other conclusions (rebuild
  streaming over `conn`, the recorder seam shape, the in-memory guards) — only
  the specific claim that one `SqliteConnection` object is safe for concurrent
  multi-threaded command creation/disposal.

## Alternatives considered

- **Stagger the catch-up delays (5s, 6s, 7s, ...).** Rejected: doesn't touch
  the same-Hour nightly collision (not a catch-up phenomenon), so it would
  read as "fixed" while the real recurring failure mode stayed live in
  production every night at 04:00.
- **A global lock on the existing shared `conn`, no new connection.** Viable
  and smaller — closes job×job for the same files touched — but leaves
  job×request open (jobs would still share a connection object with request
  threads). The dedicated connection's marginal cost (one more
  `SqliteConnection`, cheap per ADR-0024's own reasoning about multiple WAL
  connections) was judged worth the fuller closure.
- **Add a `jobLock: SemaphoreSlim option` parameter to `refreshOne` directly**
  (used by both the job path and the request path). Rejected: it would force
  a signature change onto `Api.fs`'s manual-refresh call site for a concern
  (job×job racing) that path doesn't have, and risks the two callers'
  locking behavior silently drifting apart later. The separate
  `refreshOneForJob` keeps the job-only concern contained to `SeriesRefresh.fs`.
- **Reconcile-on-read or a DB-persisted job lock instead of the in-memory
  guard.** Not reopened here — ADR-0026 already settled this; this ADR only
  changes the *connection and locking* story underneath the same guard shape.

## References

- `src/Server/Composition.fs` — `jobConn`, `jobDbLock`, the scheduled-jobs
  registry's `Run` bodies, `MEDIATHECA_DISABLE_SCHEDULED_JOBS` removal.
- `src/Server/ScheduledJobs.fs` — `tryStartJob`, `startTimer`'s catch-up delay
  and no-jitter `nextRun` (both unchanged by this fix, but the source of the
  same-Hour recurrence this fix must cover).
- `src/Server/Administration.fs` — `insertRunningRow`/`completeRun`/`failRun`
  (now `jobLock`-guarded), `makeJobRunRecorder` (now takes `jobLock`).
- `src/Server/PlaytimeTracker.fs` — `withLock`, `runSync`'s locked call sites,
  `createGameFromSteam`'s locked DB sections.
- `src/Server/SeriesRefresh.fs` — `withLock`, `refreshOneForJob` (new,
  job-only), `runNightlyJob`'s locked `getRefreshCandidates` call.
- `src/Server/Api.fs` — `manualSyncTriggerLock`, `triggerPlaytimeSync`'s call
  site (mechanical signature-compatibility change, not a behavior change).
- `playwright.config.ts` — `MEDIATHECA_DISABLE_SCHEDULED_JOBS` removed from
  the `webServer` env.
- `tests/Server.Tests/JobConnectionConcurrencyTests.fs` — the regression
  coverage described above.
- ADR-0003 — SQLite/WAL baseline this fix builds on, not replaces.
- ADR-0024 — the projection-rebuild connection-strategy reasoning whose
  "single shared `conn` is safe for concurrent use" premise this ADR narrows
  (the multi-connection WAL reasoning stands; the one-connection-two-threads
  claim does not).
- ADR-0026 — the job-runs recorder shape and "two different jobs can run
  concurrently" guarantee this fix preserves while changing the connection
  and locking underneath it.
- ADR-0027 — the Playwright e2e harness whose spike first surfaced this crash,
  and whose `MEDIATHECA_DISABLE_SCHEDULED_JOBS` workaround this ADR retires.
- administration-cx92m — the deferred, broader request×request connection-
  safety follow-up this fix does not attempt.
