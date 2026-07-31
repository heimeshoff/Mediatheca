module Mediatheca.Tests.AdminWipeImportTests

// administration-n8kqw (ADR-0038): wipe-first event log import — overwriting
// a store that already has events. Reuses ADR-0034's three-guardrail
// protocol (VACUUM INTO backup first, preview+confirm, projections-dirty
// signal) with one inversion: the wipe, the re-import, the FTS rebuild, and
// the checkpoint rewind all share ONE transaction, so the transaction — not
// the backup file — is the primary restore path. These tests exercise
// `Administration.runWipeAndImport` (the transaction/backup protocol),
// `EventStore.getEventStoreSummary` (the preview query), and
// `Administration.decideAndClaimWipeImportGuard`/`wipeImportInFlight` (the
// mutual-exclusion guard, extracted as plain functions so both directions
// are testable without spinning up SSE/HTTP — see their doc comments in
// Administration.fs) directly, with a REAL file-backed dbPath (not the
// `noStoragePath` stand-in most of AdministrationTests.fs uses), since
// `VACUUM INTO` needs a real sibling directory to write `backups/` into —
// same fixture shape `AdminSurgeryTests.fs` established.
//
// Deliberately NOT covered here (see the task's Notes for the full
// reasoning): a concurrency test racing real threads against the
// transaction (this transaction holds the write lock for its whole
// duration by design); a forced-backup-failure test (`VACUUM INTO` is hard
// to force-fail against a real path, and the path is one shared `match` arm
// with wwc36's shipped `BackupFailed`); an SSE-handler-level test (`Sse.sseFrame`'s
// framing is covered by `SseTests.fs`; vrc56 added none for import-events —
// kept parity); a Playwright spec (administration-svq3t's destructive-spec
// gate is reserved for a follow-up task, per the builder's explicit note).

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrapAdmin (conn: SqliteConnection) =
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    GameJournal.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    CatalogProjection.handler.Init conn
    Administration.initializeJobRuns conn

let private makeEvent eventType data : EventStore.EventData = {
    EventType = eventType
    Data = data
    Metadata = "{}"
}

let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    ContentBlockProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
]

let private noImagesDir = "test-fixtures-do-not-exist/images"

/// Same shape as `AdminSurgeryTests.fs`'s `createSurgeryApi`: a REAL dbPath
/// so `VACUUM INTO`'s sibling `backups/` directory lands somewhere real.
let private createAdminApi (factory: unit -> SqliteConnection) (dbPath: string) : IAdminApi =
    Administration.create factory dbPath noImagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder (factory ()) (new System.Threading.SemaphoreSlim(1, 1))) (Administration.makeGuards ())

let private backupsDirFor (dbPath: string) = Path.Combine(Path.GetDirectoryName(dbPath), "backups")

let private cleanupBackups (dbPath: string) =
    let dir = backupsDirFor dbPath
    if Directory.Exists(dir) then try Directory.Delete(dir, true) with _ -> ()

let private fullDump (conn: SqliteConnection) : string =
    let dumpTable (table: string) (columns: string) =
        conn
        |> Db.newCommand (sprintf "SELECT %s FROM %s" columns table)
        |> Db.query (fun rd ->
            [ for i in 0 .. (columns.Split(',').Length - 1) -> rd.ReadString (columns.Split(',').[i].Trim()) ]
            |> String.concat "|")
        |> String.concat ";"
    String.concat "\n" [
        dumpTable "events" "global_position,stream_id,stream_position,event_type,data,metadata,timestamp"
        dumpTable "projection_checkpoints" "projection_name,last_position"
    ]

let private exportToString (conn: SqliteConnection) : string =
    use writer = new StringWriter()
    EventStore.exportNdjson conn writer
    writer.ToString()

let private noopOnBackup (_: string) = ()

[<Tests>]
let adminWipeImportTests =
    testList "AdminWipeImport" [

        testCase "runWipeAndImport backs up via VACUUM INTO before wiping anything; the backup's events content matches the pre-wipe store's full content" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"pre-wipe one"}"""; makeEvent "BookAdded" """{"title":"pre-wipe two"}""" ] |> ignore
                let preWipeDump = fullDump conn

                let ndjson = """{"globalPosition":1,"streamId":"new-stream","streamPosition":0,"eventType":"NewEvent","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
                use reader = new StringReader(ndjson)

                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeBackupFailed reason -> failtest (sprintf "Expected the wipe-and-import to succeed, got WipeBackupFailed: %s" reason)
                | Administration.WipeImportFailed(_, lineNumber, message) -> failtest (sprintf "Expected success, got WipeImportFailed(%d, %s)" lineNumber message)
                | Administration.WipeImportApplied(backupPath, _, _) ->
                    Expect.isTrue (File.Exists(backupPath)) "Backup file should exist"
                    use verifyConn = new SqliteConnection($"Data Source={backupPath}")
                    verifyConn.Open()
                    use integrityCmd = verifyConn.CreateCommand()
                    integrityCmd.CommandText <- "PRAGMA integrity_check"
                    Expect.equal (integrityCmd.ExecuteScalar() :?> string) "ok" "Backup should pass PRAGMA integrity_check"

                    let backupDump =
                        let dumpTable (table: string) (columns: string) =
                            verifyConn
                            |> Db.newCommand (sprintf "SELECT %s FROM %s" columns table)
                            |> Db.query (fun rd ->
                                [ for i in 0 .. (columns.Split(',').Length - 1) -> rd.ReadString (columns.Split(',').[i].Trim()) ]
                                |> String.concat "|")
                            |> String.concat ";"
                        String.concat "\n" [
                            dumpTable "events" "global_position,stream_id,stream_position,event_type,data,metadata,timestamp"
                            dumpTable "projection_checkpoints" "projection_name,last_position"
                        ]
                    Expect.equal backupDump preWipeDump "The backup taken before the wipe must reflect the store's full pre-wipe content, not merely its count"
            finally
                cleanupBackups db.Path

        testCase "a malformed line rolls back the whole wipe-and-import transaction: events + projection_checkpoints are byte-for-byte identical before and after" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"survives the rollback"}""" ] |> ignore
                let beforeDump = fullDump conn

                let validLine = """{"globalPosition":1,"streamId":"stream-a","streamPosition":0,"eventType":"E1","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
                let malformedLine = "this is not valid json"
                let ndjson = String.Join("\n", [ validLine; malformedLine ])
                use reader = new StringReader(ndjson)

                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeImportApplied _ -> failtest "A malformed line must not allow the wipe-and-import to apply"
                | Administration.WipeBackupFailed reason -> failtest (sprintf "Expected WipeImportFailed, got WipeBackupFailed: %s" reason)
                | Administration.WipeImportFailed(_, lineNumber, _) ->
                    Expect.equal lineNumber 2 "The malformed line is the 2nd non-blank line"

                let afterDump = fullDump conn
                Expect.equal afterDump beforeDump "A malformed line anywhere must roll back the wipe too, leaving the store exactly as it was"
            finally
                cleanupBackups db.Path

        testCase "a successful wipe-and-import replaces events content exactly, preserving global_position, and eventsDiscarded/eventsImported match the pre-wipe count and the NDJSON's row count" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" "{}"; makeEvent "BookAdded" "{}"; makeEvent "BookAdded" "{}" ] |> ignore
                let preWipeCount = EventStore.getTotalEventCount conn

                // Build the incoming NDJSON from a separate, freshly-seeded
                // in-memory store (the same "produced by exportNdjson" shape
                // vrc56's tests use) so its global_positions are independent
                // of the target store's own numbering.
                let sourceConn = new SqliteConnection("Data Source=:memory:")
                sourceConn.Open()
                EventStore.initialize sourceConn
                EventStore.appendToStream sourceConn "stream-x" -1L [ makeEvent "Noted" """{"note":"fresh content"}""" ] |> ignore
                EventStore.appendToStream sourceConn "stream-y" -1L [ makeEvent "Noted" """{"note":"more fresh content"}""" ] |> ignore
                let ndjson = exportToString sourceConn

                use reader = new StringReader(ndjson)
                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeBackupFailed reason -> failtest (sprintf "Expected success, got WipeBackupFailed: %s" reason)
                | Administration.WipeImportFailed(_, lineNumber, message) -> failtest (sprintf "Expected success, got WipeImportFailed(%d, %s)" lineNumber message)
                | Administration.WipeImportApplied(_, eventsDiscarded, eventsImported) ->
                    Expect.equal eventsDiscarded preWipeCount "eventsDiscarded should match the pre-wipe event count"
                    Expect.equal eventsImported 2 "eventsImported should match the NDJSON's row count"

                let exportedAfter = exportToString conn
                Expect.equal exportedAfter ndjson "Post-wipe store content must match the imported NDJSON exactly, global_position included"
            finally
                cleanupBackups db.Path

        testCase "a subsequent ordinary append after a wipe-and-import lands strictly above every imported position" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                // Give the pre-wipe (discarded) log a HIGHER max global_position
                // than the imported log will have, so sqlite_sequence not being
                // reset is actually exercised: a subsequent append must land
                // above the discarded max, not merely above the imported max.
                for i in 1 .. 5 do
                    EventStore.appendToStream conn (sprintf "books-%d" i) -1L [ makeEvent "BookAdded" "{}" ] |> ignore
                let discardedMax = EventStore.getMaxGlobalPosition conn

                let ndjson = """{"globalPosition":1,"streamId":"stream-a","streamPosition":0,"eventType":"E1","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
                use reader = new StringReader(ndjson)
                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeImportApplied _ -> ()
                | other -> failtest (sprintf "Expected the wipe-and-import to succeed, got %A" other)

                let importedMax = EventStore.getMaxGlobalPosition conn
                Expect.equal importedMax 1L "Imported store's head should be the imported log's own max"

                let appendResult = EventStore.appendToStream conn "stream-new" -1L [ makeEvent "NewEvent" "{}" ]
                match appendResult with
                | EventStore.Success newPos ->
                    Expect.isGreaterThan newPos importedMax "A new append must land above the imported max"
                    Expect.isGreaterThan newPos discardedMax "sqlite_sequence is deliberately not reset by deleteAllEvents, so a new append must also land above the DISCARDED max, not merely the imported one"
                | EventStore.ConcurrencyConflict _ -> failtest "Unexpected concurrency conflict"
            finally
                cleanupBackups db.Path

        testCase "events_fts is searchable for newly imported content and not searchable for discarded content" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"discardedmarginalia"}""" ] |> ignore

                let ndjson = """{"globalPosition":1,"streamId":"stream-a","streamPosition":0,"eventType":"Noted","data":"{\"note\":\"freshmarginalia\"}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
                use reader = new StringReader(ndjson)
                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeImportApplied _ -> ()
                | other -> failtest (sprintf "Expected the wipe-and-import to succeed, got %A" other)

                let searchFinds (term: string) =
                    let filter = { EventStore.emptyQueryFilter with Search = Some term }
                    let _, _, total = EventStore.queryEventPage conn filter None 10
                    total > 0

                Expect.isTrue (searchFinds "freshmarginalia") "The newly imported content should be found via FTS search"
                Expect.isFalse (searchFinds "discardedmarginalia") "The discarded (wiped) content must never be found again — a missing rebuildFtsIndex would leave it stale-searchable"
            finally
                cleanupBackups db.Path

        testCase "after a wipe-and-import, every registered projection's checkpoint is 0 and isAnyProjectionDirty reports all of them dirty" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "Friend-alice" -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
                for handler in allProjectionHandlers do
                    Projection.runProjection conn handler
                Expect.isEmpty (Administration.isAnyProjectionDirty conn allProjectionHandlers (Administration.makeGuards ())) "Precondition: projections should be caught up before the wipe"

                let ndjson = """{"globalPosition":1,"streamId":"stream-a","streamPosition":0,"eventType":"E1","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
                use reader = new StringReader(ndjson)
                match Administration.runWipeAndImport conn db.Path allProjectionHandlers noopOnBackup reader with
                | Administration.WipeImportApplied _ -> ()
                | other -> failtest (sprintf "Expected the wipe-and-import to succeed, got %A" other)

                for handler in allProjectionHandlers do
                    let position, _ = Projection.getCheckpointInfo conn handler.Name
                    Expect.equal position 0L (sprintf "%s's checkpoint should be rewound to 0" handler.Name)

                let dirty = Administration.isAnyProjectionDirty conn allProjectionHandlers (Administration.makeGuards ())
                for handler in allProjectionHandlers do
                    Expect.contains dirty handler.Name (sprintf "%s should be reported dirty after the wipe-and-import" handler.Name)
            finally
                cleanupBackups db.Path

        testCase "getWipeImportPreview returns discard-side stats matching a direct query against the store" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "books-2" -1L [ makeEvent "BookAdded" "{}"; makeEvent "BookAdded" "{}" ] |> ignore
            let api = createAdminApi db.Factory db.Path

            let preview = api.getWipeImportPreview () |> Async.RunSynchronously
            let directSummary = EventStore.getEventStoreSummary conn

            Expect.equal preview.EventCount directSummary.EventCount "EventCount should match a direct query"
            Expect.equal preview.EventCount 3 "Three events were appended across two streams"
            Expect.equal preview.DistinctStreamCount directSummary.DistinctStreamCount "DistinctStreamCount should match a direct query"
            Expect.equal preview.DistinctStreamCount 2 "Two distinct streams were used"
            Expect.equal preview.OldestTimestamp directSummary.OldestTimestamp "OldestTimestamp should match a direct query"
            Expect.equal preview.NewestTimestamp directSummary.NewestTimestamp "NewestTimestamp should match a direct query"
            Expect.isTrue preview.OldestTimestamp.IsSome "A non-empty store should have a real oldest timestamp"

        testCase "getWipeImportPreview returns None timestamps and zero counts for an empty store" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let api = createAdminApi db.Factory db.Path

            let preview = api.getWipeImportPreview () |> Async.RunSynchronously
            Expect.equal preview.EventCount 0 "An empty store has zero events"
            Expect.equal preview.DistinctStreamCount 0 "An empty store has zero distinct streams"
            Expect.isNone preview.OldestTimestamp "An empty store has no oldest timestamp"
            Expect.isNone preview.NewestTimestamp "An empty store has no newest timestamp"

        testCase "a wipe-import already in flight refuses a second concurrent wipe-import, with no claim made by the refused attempt" <| fun _ ->
            let guards = Administration.makeGuards ()

            let first = Administration.decideAndClaimWipeImportGuard guards
            Expect.equal first Administration.ClaimedWipeImport "The first attempt should claim the guard"

            let second = Administration.decideAndClaimWipeImportGuard guards
            Expect.equal second Administration.RefusedAlreadyImporting "A second concurrent attempt must be refused while the first is still claimed"

        testCase "a wipe-import in flight refuses a concurrent projection rebuild, and a rebuild in flight refuses a wipe-import — both directions" <| fun _ ->
            // Direction 1: RebuildingProjections non-empty -> wipe-import is
            // refused, and — load-bearing for the corrected guard order —
            // NO claim is ever made on WipeImportInProgress.
            let guardsOne = Administration.makeGuards ()
            guardsOne.RebuildingProjections.TryAdd("SomeProjection", ()) |> ignore
            let decision = Administration.decideAndClaimWipeImportGuard guardsOne
            Expect.equal decision Administration.RefusedRebuildInFlight "A rebuild in flight must refuse a wipe-import"
            Expect.isTrue guardsOne.WipeImportInProgress.IsEmpty "The refused wipe-import attempt must never have claimed WipeImportInProgress"

            // Direction 2: WipeImportInProgress non-empty -> a projection
            // rebuild is refused (the check `projectionRebuildStreamHandler`
            // performs via `wipeImportInFlight` before its own TryAdd).
            let guardsTwo = Administration.makeGuards ()
            let claim = Administration.decideAndClaimWipeImportGuard guardsTwo
            Expect.equal claim Administration.ClaimedWipeImport "Precondition: the wipe-import should have claimed its guard"
            Expect.isTrue (Administration.wipeImportInFlight guardsTwo) "A projection rebuild must see the wipe-import as in flight and refuse"

        testCase "/api/stream/import-events is unaffected: EventStore.importNdjson still refuses any non-empty store" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            EventStore.appendToStream conn "existing-stream" -1L [ makeEvent "AlreadyThere" "{}" ] |> ignore

            let ndjson = """{"globalPosition":1,"streamId":"s","streamPosition":0,"eventType":"E","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
            use reader = new StringReader(ndjson)

            match EventStore.importNdjson conn reader with
            | Ok _ -> failtest "The safe import route's underlying function must still refuse a non-empty store"
            | Error EventStore.StoreNotEmpty -> ()
            | Error other -> failtest (sprintf "Expected StoreNotEmpty, got %A" other)

            Expect.equal (EventStore.getTotalEventCount conn) 1 "The existing store must be untouched"
    ]
