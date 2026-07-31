module Mediatheca.Tests.AdminGuardOwnershipTests

// administration-jrflk (ADR-0035): the class-closing regression test. Before
// this task, `Administration.fs` held three name-keyed single-flight guards
// (`runningJobs`, `rebuildingProjections`, `driftCheckInProgress`) as
// module-level `ConcurrentDictionary`s — invisible in a server process
// (there's only ever one), but shared across the WHOLE test assembly, so any
// two test files that happened to use the same job/projection name collided
// on a guard neither of them knew they shared (see JobRunsTests.fs's history
// with JobConnectionConcurrencyTests.fs). Guards are now per-instance values
// constructed at the composition root and threaded explicitly to every
// consumer, so two independently-built owners can never see each other's
// claims. These tests assert exactly that shape: two independently built
// recorders (job guard) and two independently built `AdminGuards`
// (projection guards) both successfully claim the SAME name concurrently —
// a case a module-level singleton would have refused for the second claimant.
// A green full-suite run alone doesn't prove this; a module-level singleton
// would happen to pass too, as long as no two test files' claims for the
// same name overlapped in time. This test forces the overlap directly.

open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

[<Tests>]
let adminGuardOwnershipTests =
    testList "AdminGuardOwnership" [

        testCase "two independently built JobRunRecorders both successfully claim the same job name concurrently" <| fun _ ->
            // `TryClaim` never touches the database (only `BeginRun`/
            // `Complete`/`Fail` do), so a bare unopened-schema `:memory:`
            // connection per recorder is enough — this test is purely about
            // the in-memory guard's ownership, not job-run persistence.
            let makeRecorder () =
                let conn = new SqliteConnection("Data Source=:memory:")
                conn.Open()
                Administration.makeJobRunRecorder conn (new SemaphoreSlim(1, 1))

            let recorderOne = makeRecorder ()
            let recorderTwo = makeRecorder ()

            let firstClaimed = recorderOne.TryClaim "Shared Job Name"
            let secondClaimed = recorderTwo.TryClaim "Shared Job Name"

            Expect.isTrue firstClaimed "The first, independently-built recorder should claim the job name"
            Expect.isTrue secondClaimed "A second, independently-built recorder must claim the SAME job name too — a module-level singleton would have refused this"

        testCase "two independently built AdminGuards both successfully claim the same projection name concurrently" <| fun _ ->
            let guardsOne = Administration.makeGuards ()
            let guardsTwo = Administration.makeGuards ()

            let firstClaimed = guardsOne.RebuildingProjections.TryAdd("SharedProjectionName", ())
            let secondClaimed = guardsTwo.RebuildingProjections.TryAdd("SharedProjectionName", ())

            Expect.isTrue firstClaimed "The first, independently-built AdminGuards should claim the projection name"
            Expect.isTrue secondClaimed "A second, independently-built AdminGuards must claim the SAME projection name too — a module-level singleton would have refused this"
    ]
