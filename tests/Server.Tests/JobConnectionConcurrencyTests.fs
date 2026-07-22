module Mediatheca.Tests.JobConnectionConcurrencyTests

// administration-tj8n2 (ADR-0028): regression coverage for the scheduled-job
// connection race. `ScheduledJobs.startTimer`'s two real timers (Steam
// playtime sync, Series TMDB refresh) both fire ~5s after a cold start
// (catch-up) AND both default to the same daily `Hour` (04:00 local) with no
// jitter, so they can genuinely fire at the same instant every night. Before
// this fix, both touched the ONE shared `SqliteConnection` from separate
// ThreadPool threads — `Microsoft.Data.Sqlite.SqliteConnection` is not
// thread-safe for concurrent command creation/disposal, and the resulting
// unhandled exception on a background thread crashed the whole process.
//
// This test proves the fix's actual mechanism (a dedicated job connection
// plus a `SemaphoreSlim(1,1)` acquired around each individual DB-touching
// section) survives real concurrent load on a real temp-file SQLite
// connection — including the exact same-hour collision — with no exception
// and fully correct `job_runs` rows. It drives the real, unmodified
// production choke point (`ScheduledJobs.tryStartJob`) and the real,
// unmodified recorder (`Administration.makeJobRunRecorder`); the job bodies
// are test-local closures that perform genuine SQLite commands through the
// SAME lock/connection pair `PlaytimeTracker.runSync`/
// `SeriesRefresh.runNightlyJob` use in production (see their own
// `withLock` helpers) — exercising the mechanism without needing live
// Steam/TMDB HTTP configuration, which this codebase has no mocking
// infrastructure for.

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

/// A real temp-file SQLite connection (not `:memory:`) — the acceptance
/// criterion's explicit ask, and closer to the real deployment's WAL-mode
/// file-backed connection than an in-memory one.
let private createFileConn () : SqliteConnection * string =
    let path = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-job-lock-test-%s.db" (Guid.NewGuid().ToString("N")))
    let conn = new SqliteConnection($"Data Source={path}")
    conn.Open()
    EventStore.initialize conn
    Administration.initializeJobRuns conn
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "CREATE TABLE job_touch_log (job_name TEXT NOT NULL, seq INTEGER NOT NULL)"
    cmd.ExecuteNonQuery() |> ignore
    conn, path

let private cleanup (conn: SqliteConnection) (path: string) =
    conn.Dispose()
    for suffix in [ ""; "-wal"; "-shm" ] do
        let f = path + suffix
        if File.Exists(f) then try File.Delete(f) with _ -> ()

let private readJobRunsRow (conn: SqliteConnection) (jobName: string) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT trigger, status, summary FROM job_runs WHERE job_name = @n"
    cmd.Parameters.AddWithValue("@n", jobName) |> ignore
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do
        yield reader.GetString(0), reader.GetString(1), (if reader.IsDBNull(2) then None else Some (reader.GetString(2))) ]

let private countTouchLogRows (conn: SqliteConnection) (jobName: string) : int64 =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(*) FROM job_touch_log WHERE job_name = @n"
    cmd.Parameters.AddWithValue("@n", jobName) |> ignore
    cmd.ExecuteScalar() :?> int64

/// A job whose body performs genuine, repeated SQLite command creation /
/// execution / disposal on the shared job connection, serialized through
/// `jobLock` exactly the way `PlaytimeTracker.runSync`/
/// `SeriesRefresh.runNightlyJob`'s own `withLock` helpers do — never held
/// across the `Async.Sleep`, so concurrent jobs' "network I/O" (simulated by
/// the sleep) still overlaps; only the brief DB moments serialize.
let private makeDbTouchingSpec
    (conn: SqliteConnection)
    (jobLock: SemaphoreSlim)
    (name: string)
    (hour: int)
    (iterations: int)
    : ScheduledJobs.JobSpec =
    { Name = name
      Hour = hour
      Run = fun () ->
        async {
            for i in 1 .. iterations do
                jobLock.Wait()
                try
                    use cmd = conn.CreateCommand()
                    cmd.CommandText <- "INSERT INTO job_touch_log (job_name, seq) VALUES (@n, @s)"
                    cmd.Parameters.AddWithValue("@n", name) |> ignore
                    cmd.Parameters.AddWithValue("@s", i) |> ignore
                    cmd.ExecuteNonQuery() |> ignore
                finally
                    jobLock.Release() |> ignore
                // Not locked — simulates the awaited HTTP gap real job
                // bodies have between their own locked DB sections, and
                // widens the window for a race to manifest if the fix were
                // missing.
                do! Async.Sleep 1
            return { Disposition = ScheduledJobs.JobDisposition.Ok; Summary = sprintf "%d touches" iterations }
        } }

/// Fires `tryStartJob` for `spec` on its own real background Task (not the
/// calling thread), synchronously running the returned body to completion —
/// simulating one of `ScheduledJobs.startTimer`'s `Timer` callbacks firing.
let private fireOnOwnThread (recorder: ScheduledJobs.JobRunRecorder) (spec: ScheduledJobs.JobSpec) : Task =
    Task.Run(fun () ->
        match ScheduledJobs.tryStartJob recorder spec "scheduled" with
        | Result.Ok (_, body) -> body |> Async.RunSynchronously
        | Result.Error () -> failwithf "Expected '%s' to claim the guard (no prior run in flight)" spec.Name)

[<Tests>]
let jobConnectionConcurrencyTests =
    testList "JobConnectionConcurrency" [

        testCase "N jobs firing at once on a real temp-file connection — including two sharing the identical Hour (the real nightly 04:00 collision) — all complete with no exception and correct job_runs rows" <| fun _ ->
            let conn, path = createFileConn ()
            try
                let jobLock = new SemaphoreSlim(1, 1)
                let recorder = Administration.makeJobRunRecorder conn jobLock

                // Five jobs fire "at once" (the catch-up collision). Two of
                // them — "Steam playtime sync" and "Series TMDB refresh",
                // named after the real jobs — share Hour = 4, the real
                // production default for both, reproducing the exact
                // recurring nightly same-hour collision, not just the
                // catch-up window.
                let specs =
                    [ makeDbTouchingSpec conn jobLock "Steam playtime sync" 4 25
                      makeDbTouchingSpec conn jobLock "Series TMDB refresh" 4 25
                      makeDbTouchingSpec conn jobLock "Job C" 2 15
                      makeDbTouchingSpec conn jobLock "Job D" 9 15
                      makeDbTouchingSpec conn jobLock "Job E" 4 15 ]

                let exceptions = System.Collections.Concurrent.ConcurrentBag<exn>()
                let tasks =
                    specs
                    |> List.map (fun spec ->
                        Task.Run(fun () ->
                            try
                                (fireOnOwnThread recorder spec).Wait()
                            with ex ->
                                exceptions.Add(ex)))
                    |> List.toArray

                Task.WaitAll(tasks, TimeSpan.FromSeconds(30.0)) |> ignore

                Expect.isEmpty (exceptions |> List.ofSeq) "No job should throw — the connection race must not crash any run"

                for spec in specs do
                    match readJobRunsRow conn spec.Name with
                    | [ (trigger, status, summary) ] ->
                        Expect.equal trigger "scheduled" (sprintf "%s: trigger should be scheduled" spec.Name)
                        Expect.equal status "ok" (sprintf "%s: status should resolve to ok, never left running or errored" spec.Name)
                        Expect.isSome summary (sprintf "%s: summary should be recorded" spec.Name)
                    | rows -> failwithf "%s: expected exactly one job_runs row, got %d" spec.Name (List.length rows)

                    // Every single locked write landed — the per-command
                    // lock didn't silently drop or duplicate a write under
                    // concurrent load.
                    let expectedTouches =
                        match spec.Name with
                        | "Steam playtime sync" | "Series TMDB refresh" -> 25L
                        | _ -> 15L
                    Expect.equal (countTouchLogRows conn spec.Name) expectedTouches
                        (sprintf "%s: every locked DB write should have landed exactly once" spec.Name)
            finally
                cleanup conn path

        testCase "the recorder alone (BeginRun/Complete) survives many concurrent job names hammering it at once, on a real temp-file connection" <| fun _ ->
            let conn, path = createFileConn ()
            try
                let jobLock = new SemaphoreSlim(1, 1)
                let recorder = Administration.makeJobRunRecorder conn jobLock
                let jobCount = 20

                let exceptions = System.Collections.Concurrent.ConcurrentBag<exn>()
                let tasks =
                    [ 1 .. jobCount ]
                    |> List.map (fun i ->
                        let spec : ScheduledJobs.JobSpec =
                            { Name = sprintf "Recorder stress job %d" i
                              Hour = 4 // all identical Hour, same-hour collision at recorder granularity
                              Run = fun () -> async { return { Disposition = ScheduledJobs.JobDisposition.Ok; Summary = "done" } } }
                        Task.Run(fun () ->
                            try
                                (fireOnOwnThread recorder spec).Wait()
                            with ex ->
                                exceptions.Add(ex)))
                    |> List.toArray

                Task.WaitAll(tasks, TimeSpan.FromSeconds(30.0)) |> ignore

                Expect.isEmpty (exceptions |> List.ofSeq) "No concurrent recorder call should throw"

                for i in 1 .. jobCount do
                    let name = sprintf "Recorder stress job %d" i
                    match readJobRunsRow conn name with
                    | [ (_, status, _) ] -> Expect.equal status "ok" (sprintf "%s should resolve to ok" name)
                    | rows -> failwithf "%s: expected exactly one row, got %d" name (List.length rows)
            finally
                cleanup conn path
    ]
