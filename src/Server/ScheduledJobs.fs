namespace Mediatheca.Server

open System
open System.Threading

/// Generic scheduled-job runner. Allows multiple jobs to register for a daily
/// run at a configurable local-time hour. All jobs fire 5 seconds after startup
/// (catch-up), then self-reschedule to the next configured local hour after
/// each run — so the daily fire does not drift after restarts.
///
/// administration-yamm5 / ADR-0026: every fire — scheduled or an operator's
/// "Run now" — is recorded as a durable job_runs row. This module owns the
/// terminal-outcome guarantee (a row can never be left `running`) and the
/// concurrent-trigger refusal; it does NOT own the table or the guard state
/// itself — this module compiles before Administration.fs (Server.fsproj),
/// so it reaches into the store through the injected `JobRunRecorder` seam,
/// implemented by `Administration.makeJobRunRecorder`.
module ScheduledJobs =

    /// What a job run amounted to, once it returns without throwing. `Ok`
    /// means it did its work; `Skipped` means it ran but declined to act
    /// (e.g. an API key isn't configured) — a config gap, not a failure, and
    /// rendered distinctly from an uncaught-exception `error` on the tab.
    type JobDisposition =
        | Ok
        | Skipped

    /// A job body's result: its disposition plus the one-line human summary
    /// (the counts the body already formats for stderr) that becomes the
    /// job_runs row's `summary`.
    type JobRunOutcome = {
        Disposition: JobDisposition
        Summary: string
    }

    type JobSpec = {
        /// Human-readable name of the job (used in logs and as job_runs.job_name).
        Name: string
        /// Local-time hour (0-23) at which the job should run daily.
        Hour: int
        /// Async body that runs the job. Exceptions are caught by tryStartJob
        /// and resolve the run's row to `error`.
        Run: unit -> Async<JobRunOutcome>
    }

    /// Injected recorder seam (ADR-0026). Administration.fs owns job_runs
    /// (schema + CRUD) and the name-keyed in-memory concurrency guard;
    /// `makeJobRunRecorder` builds one of these per connection, and
    /// Composition.fs passes the SAME instance to both the scheduled timer
    /// (via `startAll`) and manual run-now (via `Administration.create`), so
    /// both trigger sources share one guard dictionary and one connection.
    type JobRunRecorder = {
        /// Atomically claim the job name; false if it's already in flight
        /// (under either trigger).
        TryClaim: string -> bool
        /// Always called in `finally`, regardless of outcome.
        Release: string -> unit
        /// jobName -> trigger -> new `running` row id.
        BeginRun: string -> string -> int64
        /// Resolve a row to a terminal, non-error status.
        Complete: int64 -> JobDisposition -> string -> unit
        /// Resolve a row to `error` (an uncaught exception in the job body).
        Fail: int64 -> string -> unit
    }

    /// Shared choke point for both trigger sources. Claims the name-keyed
    /// guard; on success, opens a `running` row and returns an unstarted
    /// async body whose try/finally guarantees the row reaches a terminal
    /// status no matter what (a clean `Ok`/`Skipped` return resolves via
    /// `Complete`; an uncaught exception resolves via `Fail`) and the guard
    /// slot is always released. Returns `Error ()` immediately — no row
    /// written — if the job is already in flight under either trigger.
    /// Uses `Result.Ok`/`Result.Error` fully qualified — `JobDisposition`
    /// above deliberately shadows the unqualified `Ok` name within this file.
    let tryStartJob (recorder: JobRunRecorder) (spec: JobSpec) (trigger: string) : Result<int64 * Async<unit>, unit> =
        if not (recorder.TryClaim spec.Name) then
            Result.Error ()
        else
            let runId = recorder.BeginRun spec.Name trigger
            let body =
                async {
                    try
                        try
                            let! outcome = spec.Run ()
                            recorder.Complete runId outcome.Disposition outcome.Summary
                        with ex ->
                            recorder.Fail runId ex.Message
                    finally
                        recorder.Release spec.Name
                }
            Result.Ok (runId, body)

    /// Compute the next local DateTime at which a job scheduled for `hour`
    /// should next run, relative to `now` (local).
    let nextRun (now: DateTime) (hour: int) : DateTime =
        let today = DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Local)
        if now > today then today.AddDays(1.0) else today

    /// Start a background timer for a single job. Fires once 5 seconds after
    /// startup as a catch-up, then self-reschedules to the next local `Hour`
    /// after each run. Returns the Timer (callers should keep a reference to
    /// prevent GC). Awaits the run's body (via `tryStartJob`) before
    /// rescheduling, so the next fire is always computed after the previous
    /// one finished; a job already in flight from a manual "Run now" is
    /// skipped rather than run concurrently.
    let private startTimer (recorder: JobRunRecorder) (spec: JobSpec) : Timer =
        let mutable timerRef : Timer = Unchecked.defaultof<Timer>
        let onFire _ =
            async {
                match tryStartJob recorder spec "scheduled" with
                | Result.Ok (_, body) ->
                    eprintfn "[ScheduledJobs] Running '%s'..." spec.Name
                    do! body
                    eprintfn "[ScheduledJobs] '%s' complete." spec.Name
                | Result.Error () ->
                    eprintfn "[ScheduledJobs] '%s' skipped: already running" spec.Name
                let next = nextRun DateTime.Now spec.Hour
                let delay = next - DateTime.Now
                let ms = max 1L (int64 delay.TotalMilliseconds)
                eprintfn "[ScheduledJobs] '%s' next run scheduled at %s local (in %.1f hours)"
                    spec.Name (next.ToString("yyyy-MM-dd HH:mm")) delay.TotalHours
                if not (isNull timerRef) then
                    timerRef.Change(ms, Timeout.Infinite) |> ignore
            } |> Async.StartImmediate

        let initialDelay = TimeSpan.FromSeconds(5.0)
        let next = nextRun DateTime.Now spec.Hour
        eprintfn "[ScheduledJobs] Registered '%s': catch-up in 5s, then daily at %02d:00 local (next: %s)"
            spec.Name spec.Hour (next.ToString("yyyy-MM-dd HH:mm"))
        timerRef <- new Timer(TimerCallback(onFire), null, initialDelay, Timeout.InfiniteTimeSpan)
        timerRef

    /// Start all registered jobs. Returns the list of Timers so the caller can
    /// keep them alive for the lifetime of the application.
    let startAll (recorder: JobRunRecorder) (jobs: JobSpec list) : Timer list =
        eprintfn "[ScheduledJobs] Starting %d scheduled job(s)..." jobs.Length
        jobs |> List.map (startTimer recorder)
