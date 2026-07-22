---
id: administration-tj8n2
title: Scheduled-job timers race on the shared SqliteConnection and crash the process — fix with a dedicated job connection plus a per-command lock
status: todo
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [scheduled-jobs, sqlite, concurrency, reliability]
related_adrs: [0003, 0024, 0026, 0027]
related_research: []
prior_art: []
---

## Why
Discovered as a side effect of standing up the Playwright e2e harness
(administration-da908): starting the real server (`dotnet run`, non-watch)
against a fresh, empty `DATA_DIR` and leaving it running past the ~5s mark
reliably crashes the whole process with an unhandled
`System.ArgumentOutOfRangeException` on a background thread:

```
System.ArgumentOutOfRangeException: Index was out of range...
   at Microsoft.Data.Sqlite.SqliteConnection.RemoveCommand(SqliteCommand command)
   at Microsoft.Data.Sqlite.SqliteCommand.Dispose(Boolean disposing)
   at Mediatheca.Server.Administration.insertRunningRow(...)
   at Mediatheca.Server.ScheduledJobs.tryStartJob(...)
```

**Root cause.** `ScheduledJobs.startTimer` (`src/Server/ScheduledJobs.fs`)
registers one `System.Threading.Timer` per `JobSpec`, and **both** jobs
(Steam playtime sync, Series TMDB refresh) are configured with the same 5s
"catch-up" initial delay (`TimeSpan.FromSeconds(5.0)`). Both timers fire on
separate ThreadPool threads at essentially the same instant, and each
ultimately touches the **single shared `SqliteConnection`** (`conn`) built
once in `Composition.buildApp` (`createConnection`). `insertRunningRow`
(`Administration.fs` ~line 486) does `conn.CreateCommand()` / `use` disposal;
`Microsoft.Data.Sqlite.SqliteConnection` is **not thread-safe** for concurrent
command creation/disposal from multiple threads — the race corrupts the
connection's internal open-command list, and the resulting unhandled exception
on a ThreadPool thread is process-fatal in .NET (not on a request thread, so
no ASP.NET Core middleware catches it).

**This is not a startup-only artifact — it recurs nightly.** (Surfaced during
refinement, verified in source.) Both jobs default to `Hour = 4`
(`Composition.fs` ~lines 222-230: `playtime_sync_hour` / `series_refresh_hour`
both `Option.defaultValue 4`), and `ScheduledJobs.nextRun` builds an exact
`DateTime(y, m, d, 4, 0, 0, Local)` with **no jitter**. So on default config
the two regular daily timers also fire at exactly `04:00:00.000` local every
night — the 5s catch-up merely makes the collision trivially reproducible on
demand. A fix framed only around the catch-up window would leave the recurring
nightly collision live.

**The race extends past the recorder into the job bodies.** Both job bodies
run on the same shared `conn` directly (`PlaytimeTracker.runSync conn ...`,
`SeriesRefresh.runNightlyJob conn ...`), doing real reads/writes for seconds at
a stretch. Fixing only `insertRunningRow`/`completeRun`/`failRun` closes the
exact observed exception but lets two concurrently-firing jobs race each other
*inside their own bodies* once both are past `BeginRun` — same class of crash,
less immediately reproducible.

administration-da908 worked around this **for its own e2e harness only** via
an opt-in `MEDIATHECA_DISABLE_SCHEDULED_JOBS=1` env var (`Composition.fs`) that
skips `ScheduledJobs.startAll` entirely — unset (the default, every normal
dev/Docker run) behavior is untouched. That is a harness accommodation, not a
fix; the underlying race is still live for every real deployment.

**Wider context (deferred, not silent).** The single shared `conn` is threaded
through the *entire* server — the domain API, the admin `IAdminApi`,
projections, the rebuild SSE handler, and both jobs. Concurrent HTTP requests
therefore also share one connection object, so request×request and request×job
races are technically live too (they haven't crashed only because a single
user rarely lands two DB-touching operations in the same instant, not because
the shared connection is structurally safe). This bug is deliberately scoped to
the **scheduled-job** races (job×job and job×request); the broader
request×request question is spun off to **administration-cx92m** as a
non-blocking follow-up. See ADR-0024/0026's connection-safety reasoning, which
this fix corrects.

## What
Make scheduled-job DB access safe against concurrent execution — covering both
the 5s catch-up collision *and* the recurring same-hour (default 04:00) daily
collision — via a **dedicated connection plus a per-command lock** (chosen at
refinement over the two alternatives below):

- **Dedicated connection:** open one additional `SqliteConnection` at startup
  for all scheduled-job DB access — the job-runs recorder *and* both job bodies
  (`PlaytimeTracker.runSync`, `SeriesRefresh.runNightlyJob`) — separate from the
  request-serving `conn`. SQLite supports multiple connections to the same
  WAL-mode file, and `Microsoft.Data.Sqlite` pools the underlying native handle
  per connection string, so this is cheap. This closes job×request (jobs no
  longer share a connection object with request threads).
- **Per-command lock:** guard each discrete SQLite command on the job
  connection with a shared `SemaphoreSlim(1, 1)`, acquired around the
  individual command execution — **not** around the whole async job body. This
  closes job×job (both catch-up and nightly-same-hour) while preserving
  ADR-0026's explicit "two different jobs can run concurrently": their network
  I/O still overlaps; only their brief DB moments serialize.
- **Retire `MEDIATHECA_DISABLE_SCHEDULED_JOBS` if the harness no longer needs
  it** — see acceptance criteria.

Rejected alternatives (recorded so a worker doesn't re-litigate):
- *Stagger the catch-up delays (5s, 6s, 7s…).* Rejected: it doesn't touch the
  same-hour nightly collision (not a catch-up phenomenon), so it would read as
  "fixed" while the real recurring failure mode stays live in production.
- *A global lock on the existing shared `conn`, no new connection.* Viable and
  smaller, but for the same files touched it closes only job×job, not
  job×request. The dedicated connection's marginal cost is low enough to prefer
  the fuller fix.

This fix corrects a stated premise in ADR-0024/0026 (that WAL + `busy_timeout`
makes the single shared `conn` safe for concurrent multi-threaded access — that
reasoning conflates SQLite's file-level write serialization across *separate*
connections with .NET client-side thread-safety of *one* `SqliteConnection`
object), so it should be recorded as its own ADR. **Assign the ADR number at
authoring time** (next free number then — do not hard-code it here; 0027 is
already taken by the Playwright harness).

## Acceptance criteria
- [ ] **Automated regression test (Expecto):** N concurrent tasks/threads call
      the fixed recorder functions *and* exercise the job-execution path against
      a real temp-file SQLite connection simultaneously; asserts no exception is
      thrown and the resulting `job_runs` row counts/statuses are correct. This
      replaces "wait 5s and watch it not crash" with something CI runs every
      time.
- [ ] **Same-hour case is covered explicitly:** the test configures two
      `JobSpec`s for the *identical* `Hour` and fires them concurrently — the
      actual recurring production failure mode, not just the catch-up window.
- [ ] Manual repro (empty `DATA_DIR`, `dotnet run`, wait >5s) confirms the crash
      no longer occurs — retained as a smoke check that the automated test
      reflects reality, not as the primary gate.
- [ ] `MEDIATHECA_DISABLE_SCHEDULED_JOBS` decision is recorded either way:
      if removed, a Playwright run with jobs enabled is green; if kept, a
      one-line `Composition.fs` comment states the non-crash reason the harness
      still wants jobs off. [human-eye]
- [ ] Expecto suite and `npm run build` remain green.

## Notes
- The follow-up task **administration-cx92m** covers the broader question this
  surfaced: whether the single shared `conn` is safe under genuine
  request×request concurrency across the whole app. Non-blocking; references
  this fix's ADR as its motivation.
- See ADR-0027 (Playwright e2e harness) for the harness-side workaround and
  where this was found; ADR-0024/0026 for the connection-safety reasoning this
  fix corrects; ADR-0003 for the SQLite/WAL baseline.
- Files in scope: `Composition.fs` (new job connection + wiring),
  `ScheduledJobs.fs`, `Administration.fs` (recorder functions),
  `PlaytimeTracker.runSync`, `SeriesRefresh.runNightlyJob`.
