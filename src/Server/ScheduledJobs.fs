namespace Mediatheca.Server

open System
open System.Threading

/// Generic scheduled-job runner. Allows multiple jobs to register for a daily
/// run at a configurable local-time hour. All jobs fire 5 seconds after startup
/// (catch-up), then self-reschedule to the next configured local hour after
/// each run — so the daily fire does not drift after restarts.
module ScheduledJobs =

    type JobSpec = {
        /// Human-readable name of the job (used in logs).
        Name: string
        /// Local-time hour (0-23) at which the job should run daily.
        Hour: int
        /// Async body that runs the job. Exceptions are caught and logged.
        Run: unit -> Async<unit>
    }

    let private runJobSafe (spec: JobSpec) : Async<unit> =
        async {
            try
                eprintfn "[ScheduledJobs] Running '%s'..." spec.Name
                do! spec.Run ()
                eprintfn "[ScheduledJobs] '%s' complete." spec.Name
            with ex ->
                eprintfn "[ScheduledJobs] '%s' failed: %s" spec.Name ex.Message
        }

    /// Compute the next local DateTime at which a job scheduled for `hour`
    /// should next run, relative to `now` (local).
    let nextRun (now: DateTime) (hour: int) : DateTime =
        let today = DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Local)
        if now > today then today.AddDays(1.0) else today

    /// Start a background timer for a single job. Fires once 5 seconds after
    /// startup as a catch-up, then self-reschedules to the next local `Hour`
    /// after each run. Returns the Timer (callers should keep a reference to
    /// prevent GC).
    let private startTimer (spec: JobSpec) : Timer =
        let mutable timerRef : Timer = Unchecked.defaultof<Timer>
        let onFire _ =
            async {
                do! runJobSafe spec
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
    let startAll (jobs: JobSpec list) : Timer list =
        eprintfn "[ScheduledJobs] Starting %d scheduled job(s)..." jobs.Length
        jobs |> List.map startTimer
