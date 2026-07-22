---
id: administration-tj8n2
title: Scheduled-job catch-up timers race on the shared SqliteConnection and crash the process
status: backlog
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [scheduled-jobs, sqlite, concurrency, reliability]
related_adrs: [0026, 0027]
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

Root cause: `ScheduledJobs.startTimer` (`src/Server/ScheduledJobs.fs`)
registers one `System.Threading.Timer` per `JobSpec`, and **both** jobs
(Steam playtime sync, Series TMDB refresh) are configured with the same 5s
"catch-up" initial delay (`Composition.fs`'s `scheduledJobs` list). Both
timers fire on separate ThreadPool threads at essentially the same instant,
and each calls `Administration.insertRunningRow` (`Administration.fs`
~line 486), which does `conn.CreateCommand()` / `use ... = ...` (disposal) on
the **same shared `SqliteConnection`** instance built once in
`Composition.buildApp`. `Microsoft.Data.Sqlite.SqliteConnection` is not
thread-safe for concurrent command creation/disposal from multiple threads —
the race corrupts the connection's internal open-command list, and the
resulting unhandled exception on a ThreadPool thread is process-fatal in
.NET (not caught by any ASP.NET Core middleware, since it's not on a request
thread).

Since an unhandled exception in .NET Core terminates the entire process,
this is not merely "one job run recorded wrong" — it kills the whole server,
including all in-flight HTTP requests, on (apparently reliably) every cold
start. It reproduced on the very first manual repro attempt.

administration-da908 worked around this **for its own e2e harness only** via
an opt-in `MEDIATHECA_DISABLE_SCHEDULED_JOBS=1` env var
(`Composition.fs`) that skips `ScheduledJobs.startAll` entirely — unset (the
default, every normal dev/Docker run) behavior is untouched. That is a
harness accommodation, not a fix; the underlying race is still live for
every real deployment.

## What
Fix the race so the two catch-up timers (and any future scheduled jobs) can
safely record job runs concurrently. Candidate approaches (pick one during
refinement — none prescribed here):
- Give `ScheduledJobs`/`Administration`'s job-run recorder its own
  `SqliteConnection`, separate from the request-serving `conn` (SQLite
  supports multiple connections to the same WAL-mode file).
- Serialize all `insertRunningRow`/`completeRun`/`failRun` access behind a
  lock (simplest, but widens a hot path's critical section slightly — the
  calls are all short single-statement inserts/updates, so likely fine).
- Stagger the catch-up delays per job (e.g. 5s, 6s, 7s...) — cheapest, but
  papers over the underlying thread-safety violation rather than fixing it,
  and any future concurrent `conn` access (not just catch-up) would still be
  unsafe.

## Acceptance criteria
- [ ] Two (or more) scheduled jobs whose catch-up windows overlap no longer
      crash the process — reproduce today's crash first (empty `DATA_DIR`,
      `dotnet run`, wait >5s) to confirm the fix actually closes it.
- [ ] `MEDIATHECA_DISABLE_SCHEDULED_JOBS` (administration-da908) can be
      retired once this is fixed, if the e2e harness no longer needs it —
      confirm and remove if so, or leave it if the harness still prefers
      jobs off for unrelated determinism reasons.
- [ ] Expecto suite and `npm run build` remain green.

## Notes
See ADR-0027 (Playwright e2e harness) for the harness-side workaround and
where this was found.
