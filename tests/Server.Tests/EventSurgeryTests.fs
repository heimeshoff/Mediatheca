module Mediatheca.Tests.EventSurgeryTests

// administration-wwc36 (ADR-0034): the raw-log escape hatch's primitives —
// VACUUM INTO backup, the edit/delete/rename mutation statements, and the
// events_fts rebuild-on-mutate idiom. This file exercises EventStore.fs's new
// functions in isolation (no Administration.fs / IAdminApi wiring, no
// backups/dirty-checkpoint guardrail orchestration — that's covered at the
// Administration level in AdministrationTests.fs).

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    conn

let private makeEvent eventType data : EventStore.EventData = {
    EventType = eventType
    Data = data
    Metadata = "{}"
}

let private tempBackupPath () =
    Path.Combine(Path.GetTempPath(), sprintf "mediatheca-surgery-backup-test-%s.db" (Guid.NewGuid().ToString("N")))

let private deleteIfExists (path: string) =
    if File.Exists(path) then try File.Delete(path) with _ -> ()

/// Unwraps a successful append's global_position, failing the test loudly on
/// an unexpected concurrency conflict — every call site here appends to a
/// stream it just created, so a conflict would indicate a real test bug.
let private globalPositionOf (result: EventStore.AppendResult) : int64 =
    match result with
    | EventStore.Success gp -> gp
    | EventStore.ConcurrencyConflict(expected, actual) ->
        failtest (sprintf "Expected append to succeed, got ConcurrencyConflict(expected=%d, actual=%d)" expected actual)

[<Tests>]
let eventSurgeryTests =
    testList "EventSurgery" [

        testCase "vacuumIntoBackup snapshots the live store to a fresh file that opens and has the matching event count" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "books-1" -1L [
                makeEvent "BookAdded" """{"title":"one"}"""
                makeEvent "BookAdded" """{"title":"two"}"""
                makeEvent "BookAdded" """{"title":"three"}"""
            ] |> ignore
            let backupPath = tempBackupPath ()
            try
                match EventStore.vacuumIntoBackup conn backupPath with
                | Error reason -> failtest (sprintf "Expected backup to succeed, got: %s" reason)
                | Ok () ->
                    Expect.isTrue (File.Exists(backupPath)) "Backup file should exist on disk"
                    use verifyConn = new SqliteConnection($"Data Source={backupPath}")
                    verifyConn.Open()
                    use cmd = verifyConn.CreateCommand()
                    cmd.CommandText <- "SELECT COUNT(*) FROM events"
                    let count = cmd.ExecuteScalar() :?> int64
                    Expect.equal count 3L "Backup should contain exactly the events present at VACUUM INTO time"
            finally
                deleteIfExists backupPath

        testCase "vacuumIntoBackup returns Error when the target path can't be created" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"one"}""" ] |> ignore
            let badPath = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-surgery-nonexistent-dir-%s" (Guid.NewGuid().ToString("N")), "backup.db")
            match EventStore.vacuumIntoBackup conn badPath with
            | Ok () -> failtest "Expected backup to fail into a directory that doesn't exist"
            | Error _ -> ()

        testCase "editEventData updates one row's data and metadata by exact global_position" <| fun _ ->
            let conn = createInMemoryConnection ()
            let gp = globalPositionOf (EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"typo'd"}""" ])
            let affected = EventStore.editEventData conn gp """{"title":"fixed"}""" """{"source":"admin-console"}"""
            Expect.equal affected 1 "Exactly one row should be affected"
            match EventStore.getEventByGlobalPosition conn gp with
            | None -> failtest "Edited row should still exist"
            | Some row ->
                Expect.equal row.Data """{"title":"fixed"}""" "Data should reflect the edit"
                Expect.equal row.Metadata """{"source":"admin-console"}""" "Metadata should reflect the edit"

        testCase "editEventData affects zero rows for a nonexistent global_position" <| fun _ ->
            let conn = createInMemoryConnection ()
            let affected = EventStore.editEventData conn 999999L "{}" "{}"
            Expect.equal affected 0 "No row should be affected for a nonexistent position"

        testCase "deleteEventRow removes exactly the targeted row and leaves a gap, not a renumber" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "books-1" -1L [
                makeEvent "BookAdded" """{"title":"one"}"""
                makeEvent "BookAdded" """{"title":"two"}"""
                makeEvent "BookAdded" """{"title":"three"}"""
            ] |> ignore
            let all = EventStore.readStream conn "books-1"
            let middle = all.[1]
            let affected = EventStore.deleteEventRow conn middle.GlobalPosition
            Expect.equal affected 1 "Exactly one row should be deleted"
            let remaining = EventStore.readStream conn "books-1"
            Expect.equal (List.length remaining) 2 "Two rows should remain"
            Expect.equal remaining.[0].StreamPosition 0L "First remaining row keeps its original stream_position"
            Expect.equal remaining.[1].StreamPosition 2L "Last remaining row keeps its original stream_position — no renumbering"
            Expect.equal (EventStore.getStreamPosition conn "books-1") 2L "Stream position (MAX) should reflect the surviving rows, gap and all"

        testCase "renameEventTypeRows renames every occurrence and none remain at the old type" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" "{}"; makeEvent "BookAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "books-2" -1L [ makeEvent "BookAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "books-2" 0L [ makeEvent "BookRemoved" "{}" ] |> ignore
            let affected = EventStore.renameEventTypeRows conn "BookAdded" "Book_added"
            Expect.equal affected 3 "All three BookAdded rows should be renamed"
            Expect.equal (EventStore.countEventsOfType conn "BookAdded") 0 "Zero rows should remain at the old type"
            Expect.equal (EventStore.countEventsOfType conn "Book_added") 3 "All renamed rows should be under the new type"
            Expect.contains (EventStore.getDistinctEventTypes conn) "Book_added" "Distinct event types should reflect the new name"
            Expect.isFalse (EventStore.getDistinctEventTypes conn |> List.contains "BookAdded") "Distinct event types should never show the old name afterward"

        testCase "rebuildFtsIndex re-syncs search after an edit — old text disappears, new text is found" <| fun _ ->
            let conn = createInMemoryConnection ()
            let gp = globalPositionOf (EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"originaltoken"}""" ])

            let searchFinds (term: string) : bool =
                let filter = { EventStore.emptyQueryFilter with Search = Some term }
                let page, _, total = EventStore.queryEventPage conn filter None 10
                total > 0 && not (List.isEmpty page)

            Expect.isTrue (searchFinds "originaltoken") "Pre-edit text should be findable before the edit"

            EventStore.editEventData conn gp """{"title":"replacedtoken"}""" "{}" |> ignore
            // Before rebuild: external-content FTS5 has no UPDATE trigger, so
            // the OLD indexed text is still what MATCH searches against.
            Expect.isTrue (searchFinds "originaltoken") "Before rebuild, FTS should still (stale-)match the pre-edit text"
            Expect.isFalse (searchFinds "replacedtoken") "Before rebuild, FTS should not yet match the post-edit text"

            EventStore.rebuildFtsIndex conn

            Expect.isFalse (searchFinds "originaltoken") "After rebuild, the old text should no longer be found"
            Expect.isTrue (searchFinds "replacedtoken") "After rebuild, the new text should be found"

        testCase "rebuildFtsIndex re-syncs search after a delete — deleted event's text is no longer found" <| fun _ ->
            let conn = createInMemoryConnection ()
            let gp = globalPositionOf (EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"vanishingtoken"}""" ])

            let searchFinds (term: string) : bool =
                let filter = { EventStore.emptyQueryFilter with Search = Some term }
                let _, _, total = EventStore.queryEventPage conn filter None 10
                total > 0

            Expect.isTrue (searchFinds "vanishingtoken") "Pre-delete text should be findable"

            EventStore.deleteEventRow conn gp |> ignore
            EventStore.rebuildFtsIndex conn

            Expect.isFalse (searchFinds "vanishingtoken") "After delete + rebuild, the deleted event's text should never be found again"

        testCase "sampleEventsOfType returns a bounded, oldest-first sample" <| fun _ ->
            let conn = createInMemoryConnection ()
            for i in 1 .. 5 do
                EventStore.appendToStream conn (sprintf "books-%d" i) -1L [ makeEvent "BookAdded" (sprintf """{"n":%d}""" i) ] |> ignore
            let sample = EventStore.sampleEventsOfType conn "BookAdded" 3
            Expect.equal (List.length sample) 3 "Sample should be capped at the requested limit"
            Expect.equal (EventStore.countEventsOfType conn "BookAdded") 5 "Full count should be unaffected by the sample bound"
            Expect.isTrue (sample |> List.pairwise |> List.forall (fun (a, b) -> a.GlobalPosition < b.GlobalPosition)) "Sample should be ordered oldest-first"
    ]
