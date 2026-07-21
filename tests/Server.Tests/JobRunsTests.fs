module Mediatheca.Tests.JobRunsTests

// Job runs console (administration-yamm5 / ADR-0026): the terminal-outcome
// guarantee, the concurrent-trigger refusal, the skipped-vs-error
// distinction, and startup crash reconciliation. These exercise
// `ScheduledJobs.tryStartJob` (the shared choke point both the scheduled
// timer and manual "Run now" go through) and `Administration`'s recorder/
// table/reconciliation directly, plus `IAdminApi.runJobNow`/`getJobStatuses`
// for the fire-and-forget + poll shape.

open System
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private createConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    Administration.initializeJobRuns conn
    conn

let private noStoragePath = "test-fixtures-do-not-exist/nowhere.db"
let private noImagesDir = "test-fixtures-do-not-exist/images"

/// Reads job_runs rows for one job, newest first, straight off the table —
/// bypassing IAdminApi's DTO mapping so these tests assert on the
/// server-internal record (trigger, status, timestamps) directly.
let private readRows (conn: SqliteConnection) (jobName: string) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT trigger, status, summary, started_at, finished_at FROM job_runs WHERE job_name = @n ORDER BY id DESC"
    cmd.Parameters.AddWithValue("@n", jobName) |> ignore
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do
        yield
            reader.GetString(0),
            reader.GetString(1),
            (if reader.IsDBNull(2) then None else Some (reader.GetString(2))),
            reader.GetString(3),
            (if reader.IsDBNull(4) then None else Some (reader.GetString(4))) ]

let private okSpec (name: string) : ScheduledJobs.JobSpec =
    { Name = name
      Hour = 4
      Run = fun () -> async { return { Disposition = ScheduledJobs.JobDisposition.Ok; Summary = "did the thing" } } }

let private skippedSpec (name: string) : ScheduledJobs.JobSpec =
    { Name = name
      Hour = 4
      Run = fun () -> async { return { Disposition = ScheduledJobs.JobDisposition.Skipped; Summary = "not configured" } } }

let private throwingSpec (name: string) : ScheduledJobs.JobSpec =
    { Name = name
      Hour = 4
      Run = fun () -> async { return failwith "boom" } }

/// A job whose body blocks on `gate` until the test releases it — lets tests
/// observe the row mid-`running` before resolving it to terminal.
let private blockingSpec (name: string) (gate: ManualResetEventSlim) : ScheduledJobs.JobSpec =
    { Name = name
      Hour = 4
      Run = fun () -> async {
          gate.Wait()
          return { Disposition = ScheduledJobs.JobDisposition.Ok; Summary = "done" }
      } }

/// Polls `readRows` until the single row for `jobName` leaves `running`, or
/// gives up after ~2s. The job body resolves on its own `Async.Start`ed
/// task, not synchronously with the test's `gate.Set()`, so a short poll —
/// not an immediate read — is the correct way to observe the resolution.
let private waitForTerminal (conn: SqliteConnection) (jobName: string) : bool =
    let mutable resolved = false
    let mutable attempts = 0
    while not resolved && attempts < 200 do
        match readRows conn jobName with
        | (_, status, _, _, _) :: _ when status <> "running" -> resolved <- true
        | _ ->
            Thread.Sleep(10)
            attempts <- attempts + 1
    resolved

[<Tests>]
let jobRunsTests =
    testList "JobRuns" [

        testCase "a scheduled-trigger run writes a job_runs row with trigger='scheduled', a terminal status, summary, and both timestamps" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let spec = okSpec "Job A"

            match ScheduledJobs.tryStartJob recorder spec "scheduled" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) ->
                body |> Async.RunSynchronously
                match readRows conn "Job A" with
                | [ (trigger, status, summary, startedAt, finishedAt) ] ->
                    Expect.equal trigger "scheduled" "Trigger should be scheduled"
                    Expect.equal status "ok" "Status should resolve to ok"
                    Expect.equal summary (Some "did the thing") "Summary should be recorded"
                    Expect.isNotEmpty startedAt "started_at should be set"
                    Expect.isSome finishedAt "finished_at should be set once terminal"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "a manual (run now) trigger writes an otherwise-identical row with trigger='manual'" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let spec = okSpec "Job B"

            match ScheduledJobs.tryStartJob recorder spec "manual" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) ->
                body |> Async.RunSynchronously
                match readRows conn "Job B" with
                | [ (trigger, status, _, _, _) ] ->
                    Expect.equal trigger "manual" "Trigger should be manual"
                    Expect.equal status "ok" "Status should resolve to ok"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "runJobNow returns before the job completes, and the running row it created resolves to a terminal status once the job finishes" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            use gate = new ManualResetEventSlim(false)
            let spec = blockingSpec "Job C" gate
            let api = Administration.create conn noStoragePath noImagesDir [] [ spec ] recorder

            match api.runJobNow "Job C" |> Async.RunSynchronously with
            | RunJobRejected -> failwith "Expected the run to start"
            | RunJobStarted _runId ->
                // Fire-and-forget: runJobNow already returned, but the gated
                // body hasn't run yet — the row must still be `running`.
                match readRows conn "Job C" with
                | [ (_, status, summary, _, finishedAt) ] ->
                    Expect.equal status "running" "Row should still be running right after runJobNow returns"
                    Expect.isNone summary "Summary should be empty while running"
                    Expect.isNone finishedAt "finished_at should be empty while running"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

                gate.Set()
                Expect.isTrue (waitForTerminal conn "Job C") "Row should resolve to a terminal status after the job finishes"
                match readRows conn "Job C" with
                | [ (_, status, summary, _, finishedAt) ] ->
                    Expect.equal status "ok" "Status should resolve to ok"
                    Expect.equal summary (Some "done") "Summary should be recorded"
                    Expect.isSome finishedAt "finished_at should be set"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "a run that declined to act resolves to 'skipped', distinct from 'error'" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let spec = skippedSpec "Job D"

            match ScheduledJobs.tryStartJob recorder spec "scheduled" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) ->
                body |> Async.RunSynchronously
                match readRows conn "Job D" with
                | [ (_, status, summary, _, _) ] ->
                    Expect.equal status "skipped" "Status should be skipped, not error"
                    Expect.notEqual status "error" "Skipped must render distinctly from error"
                    Expect.equal summary (Some "not configured") "Summary should carry the skip reason"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "an uncaught exception in the job body resolves the row to 'error' with the exception message, never leaving it running" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let spec = throwingSpec "Job E"

            match ScheduledJobs.tryStartJob recorder spec "scheduled" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) ->
                body |> Async.RunSynchronously
                match readRows conn "Job E" with
                | [ (_, status, summary, _, finishedAt) ] ->
                    Expect.equal status "error" "Status should be error"
                    Expect.equal summary (Some "boom") "Summary should carry the exception message"
                    Expect.isSome finishedAt "finished_at should be set even on an uncaught exception — never left running"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "a second concurrent trigger of the same job (manual-while-scheduled) is refused: no second row, running job unaffected" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            use gate = new ManualResetEventSlim(false)
            let spec = blockingSpec "Job F" gate

            match ScheduledJobs.tryStartJob recorder spec "scheduled" with
            | Error () -> failwith "Expected the first (scheduled) trigger to succeed"
            | Ok (_firstRunId, firstBody) ->
                Async.Start firstBody

                // TryClaim/BeginRun run synchronously inside tryStartJob (before
                // Async.Start), so the guard and the running row already exist —
                // no timing race for the refusal itself.
                let secondAttempt = ScheduledJobs.tryStartJob recorder spec "manual"
                Expect.isTrue (Result.isError secondAttempt) "A concurrent manual trigger of the same job should be refused"

                match readRows conn "Job F" with
                | [ (_, status, _, _, _) ] ->
                    Expect.equal status "running" "The single row should still be the first run, still running"
                | rows -> failwithf "Expected exactly one row (no second row written), got %d" (List.length rows)

                gate.Set()
                Expect.isTrue (waitForTerminal conn "Job F") "The first run should still resolve to a terminal status once released"

        testCase "on server startup, any running row is reconciled to interrupted with a finished timestamp" <| fun _ ->
            let conn = createConn ()
            // Simulate a crash: a running row with no in-memory guard held (a
            // fresh process has never claimed anything) — the exact scenario
            // initializeJobRuns's reconciliation targets.
            use insertCmd = conn.CreateCommand()
            insertCmd.CommandText <- "INSERT INTO job_runs (job_name, trigger, status, summary, started_at, finished_at) VALUES ('Orphaned job', 'scheduled', 'running', NULL, @startedAt, NULL)"
            insertCmd.Parameters.AddWithValue("@startedAt", DateTime.UtcNow.ToString("o")) |> ignore
            insertCmd.ExecuteNonQuery() |> ignore

            // Re-run initialization (createConn already ran it once, before this
            // row existed) to exercise the reconciliation path itself.
            Administration.initializeJobRuns conn

            match readRows conn "Orphaned job" with
            | [ (_, status, summary, _, finishedAt) ] ->
                Expect.equal status "interrupted" "Orphaned running row should be reconciled to interrupted"
                Expect.isSome summary "Summary should explain the interruption"
                Expect.isSome finishedAt "finished_at should be set by reconciliation"
            | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

        testCase "reconciliation only runs at startup: a row that's genuinely running (guard held) is left alone by a later read" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            use gate = new ManualResetEventSlim(false)
            let spec = blockingSpec "Job H" gate

            match ScheduledJobs.tryStartJob recorder spec "manual" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) ->
                Async.Start body
                // A read-path call (getJobStatuses) must not itself reconcile —
                // only initializeJobRuns does, and only at startup.
                let api = Administration.create conn noStoragePath noImagesDir [] [ spec ] recorder
                api.getJobStatuses () |> Async.RunSynchronously |> ignore

                match readRows conn "Job H" with
                | [ (_, status, _, _, _) ] -> Expect.equal status "running" "A genuinely in-flight run must not be reconciled by a mere read"
                | rows -> failwithf "Expected exactly one row, got %d" (List.length rows)

                gate.Set()
                Expect.isTrue (waitForTerminal conn "Job H") "The run should still resolve normally afterward"

        testCase "getJobStatuses reports next-fire time, last outcome, and recent-run history per job" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let spec = okSpec "Job G"
            match ScheduledJobs.tryStartJob recorder spec "scheduled" with
            | Error () -> failwith "Expected the trigger to succeed"
            | Ok (_, body) -> body |> Async.RunSynchronously

            let api = Administration.create conn noStoragePath noImagesDir [] [ spec ] recorder
            let statuses = api.getJobStatuses () |> Async.RunSynchronously

            Expect.equal (List.length statuses) 1 "Should report exactly the one registered job"
            let status = statuses.[0]
            Expect.equal status.JobName "Job G" "Job name should match"
            Expect.isNotEmpty status.NextFireAt "NextFireAt should be populated"
            Expect.isSome status.LastRun "LastRun should be the run just recorded"
            Expect.equal status.LastRun.Value.Status RunStatusOk "Last run's status should be ok"
            Expect.equal (List.length status.RecentRuns) 1 "Recent runs should include the one run"

        testCase "runJobNow for an unregistered job name is rejected" <| fun _ ->
            let conn = createConn ()
            let recorder = Administration.makeJobRunRecorder conn
            let api = Administration.create conn noStoragePath noImagesDir [] [] recorder

            match api.runJobNow "No such job" |> Async.RunSynchronously with
            | RunJobRejected -> ()
            | RunJobStarted _ -> failwith "An unregistered job name should be rejected, not started"
    ]
