namespace Mediatheca.Server

open System
open System.Threading

/// Generic scheduled-job runner. Allows multiple jobs to register for a daily
/// run at a configurable UTC hour. All jobs fire 5 seconds after startup
/// (catch-up), then daily at the configured hour thereafter.
module ScheduledJobs =

    type JobSpec = {
        /// Human-readable name of the job (used in logs).
        Name: string
        /// UTC hour (0-23) at which the job should run daily.
        HourUtc: int
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

    /// Compute the next UTC DateTime at which a job scheduled for `hourUtc`
    /// should next run, relative to `now`.
    let nextRun (now: DateTime) (hourUtc: int) : DateTime =
        let today = DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc)
        if now > today then today.AddDays(1.0) else today

    /// Start a background timer for a single job. Returns the Timer (callers
    /// should keep a reference to prevent GC).
    let private startTimer (spec: JobSpec) : Timer =
        let callback _ =
            runJobSafe spec |> Async.StartImmediate

        let initialDelay = TimeSpan.FromSeconds(5.0)
        let dailyInterval = TimeSpan.FromHours(24.0)
        let next = nextRun DateTime.UtcNow spec.HourUtc
        let untilNext = next - DateTime.UtcNow
        eprintfn "[ScheduledJobs] Registered '%s': next scheduled run at %s UTC (in %.1f hours)"
            spec.Name (next.ToString("yyyy-MM-dd HH:mm")) untilNext.TotalHours
        new Timer(TimerCallback(callback), null, initialDelay, dailyInterval)

    /// Start all registered jobs. Returns the list of Timers so the caller can
    /// keep them alive for the lifetime of the application.
    let startAll (jobs: JobSpec list) : Timer list =
        eprintfn "[ScheduledJobs] Starting %d scheduled job(s)..." jobs.Length
        jobs |> List.map startTimer
