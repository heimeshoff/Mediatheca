module Mediatheca.Tests.AdminSurgeryTests

// administration-wwc36 (ADR-0034): the event surgery escape hatch's full
// IAdminApi wiring — VACUUM INTO backup, preview+confirm, the checkpoint-
// rewind dirty signal, FTS resync, delete-gap tolerance, rename, keep-all
// backup stats, and the per-request-connection concurrency model (ADR-0033).
// EventStore.fs's own primitives (vacuumIntoBackup, editEventData,
// deleteEventRow, renameEventTypeRows, rebuildFtsIndex) are unit-tested in
// isolation in EventSurgeryTests.fs — this file exercises them through the
// real `Administration.create` surface, with a REAL file-backed dbPath (not
// the `noStoragePath` stand-in most of AdministrationTests.fs uses), since
// VACUUM INTO needs a real sibling directory to write `backups/` into.

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Net.Http
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

/// Unlike `AdministrationTests.createApi` (which passes a nonexistent
/// `noStoragePath`), event surgery needs a REAL dbPath so `VACUUM INTO`'s
/// sibling `backups/` directory lands somewhere real and cleans up with the
/// rest of the fixture.
let private createSurgeryApi (factory: unit -> SqliteConnection) (dbPath: string) : IAdminApi =
    Administration.create factory dbPath noImagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder (factory ()) (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())

let private backupsDirFor (dbPath: string) = Path.Combine(Path.GetDirectoryName(dbPath), "backups")

let private cleanupBackups (dbPath: string) =
    let dir = backupsDirFor dbPath
    if Directory.Exists(dir) then try Directory.Delete(dir, true) with _ -> ()

let private globalPositionOf (result: EventStore.AppendResult) : int64 =
    match result with
    | EventStore.Success gp -> gp
    | EventStore.ConcurrencyConflict(expected, actual) ->
        failtest (sprintf "Expected append to succeed, got ConcurrencyConflict(expected=%d, actual=%d)" expected actual)

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

[<Tests>]
let adminSurgeryTests =
    testList "AdminSurgery" [

        testCase "editEvent backs up via VACUUM INTO before mutating; the backup file opens and its count matches the pre-mutation store" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"typo'd"}""" ] |> ignore
                let preMutationCount = EventStore.getTotalEventCount conn
                let api = createSurgeryApi db.Factory db.Path

                let target = (EventStore.readStream conn "books-1").[0]
                match api.editEvent target.GlobalPosition """{"title":"fixed"}""" "{}" |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected editEvent to succeed, got BackupFailed: %s" reason)
                | Applied(backupPath, affected) ->
                    Expect.equal affected 1 "Exactly one row should be reported as affected"
                    Expect.isTrue (File.Exists(backupPath)) "Backup file should exist"
                    use verifyConn = new SqliteConnection($"Data Source={backupPath}")
                    verifyConn.Open()
                    use cmd = verifyConn.CreateCommand()
                    cmd.CommandText <- "SELECT COUNT(*) FROM events"
                    let backedUpCount = cmd.ExecuteScalar() :?> int64
                    Expect.equal backedUpCount (int64 preMutationCount) "Backup should reflect the pre-mutation event count"
            finally
                cleanupBackups db.Path

        testCase "deleteEvent and renameEventType each also produce a matching backup" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [
                    makeEvent "BookAdded" "{}"
                    makeEvent "BookAdded" "{}"
                ] |> ignore
                let api = createSurgeryApi db.Factory db.Path
                let events = EventStore.readStream conn "books-1"

                let preDeleteCount = EventStore.getTotalEventCount conn
                match api.deleteEvent events.[0].GlobalPosition |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected deleteEvent to succeed, got BackupFailed: %s" reason)
                | Applied(backupPath, affected) ->
                    Expect.equal affected 1 "Exactly one row should be deleted"
                    use verifyConn = new SqliteConnection($"Data Source={backupPath}")
                    verifyConn.Open()
                    use cmd = verifyConn.CreateCommand()
                    cmd.CommandText <- "SELECT COUNT(*) FROM events"
                    Expect.equal (cmd.ExecuteScalar() :?> int64) (int64 preDeleteCount) "Delete's backup should reflect the pre-delete count"

                let preRenameCount = EventStore.getTotalEventCount conn
                match api.renameEventType "BookAdded" "Book_added" |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected renameEventType to succeed, got BackupFailed: %s" reason)
                | Applied(backupPath, affected) ->
                    Expect.equal affected 1 "The one surviving BookAdded row should be renamed"
                    use verifyConn = new SqliteConnection($"Data Source={backupPath}")
                    verifyConn.Open()
                    use cmd = verifyConn.CreateCommand()
                    cmd.CommandText <- "SELECT COUNT(*) FROM events"
                    Expect.equal (cmd.ExecuteScalar() :?> int64) (int64 preRenameCount) "Rename's backup should reflect the pre-rename count"
            finally
                cleanupBackups db.Path

        testCase "previewEventEdit / previewEventDelete return exactly the one targeted row; cancelling (never committing) leaves everything byte-for-byte unchanged" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"one"}"""; makeEvent "BookAdded" """{"title":"two"}""" ] |> ignore
                let api = createSurgeryApi db.Factory db.Path
                let target = (EventStore.readStream conn "books-1").[0]

                let beforeDump = fullDump conn

                let editPreview = api.previewEventEdit target.GlobalPosition |> Async.RunSynchronously
                match editPreview with
                | None -> failtest "Expected the edit preview to find the targeted row"
                | Some row ->
                    Expect.equal row.GlobalPosition target.GlobalPosition "Preview should target the exact global_position"
                    Expect.equal row.Data target.Data "Preview should carry the row's current data"

                let deletePreview = api.previewEventDelete target.GlobalPosition |> Async.RunSynchronously
                match deletePreview with
                | None -> failtest "Expected the delete preview to find the targeted row"
                | Some preview ->
                    Expect.equal preview.Event.GlobalPosition target.GlobalPosition "Delete preview should target the exact global_position"
                    Expect.equal preview.StreamCurrentPosition 1L "Delete preview should carry the stream's current (pre-delete) position, so the client can render the gap consequence"

                let afterDump = fullDump conn
                Expect.equal afterDump beforeDump "Preview-only calls (no commit) must leave events/projection_checkpoints byte-for-byte unchanged"

                Expect.equal (EventStore.getEventByGlobalPosition conn target.GlobalPosition |> Option.map (fun e -> e.Data)) (Some target.Data)
                    "Cancelling (never calling editEvent/deleteEvent) must leave the targeted row itself unchanged too"
            finally
                cleanupBackups db.Path

        testCase "previewEventEdit returns None for a nonexistent global_position" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let api = createSurgeryApi db.Factory db.Path
            let result = api.previewEventEdit 999999L |> Async.RunSynchronously
            Expect.isNone result "A nonexistent global_position should preview as None"

        testCase "previewEventTypeRename returns the exact count and a bounded sample" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                for i in 1 .. 5 do
                    EventStore.appendToStream conn (sprintf "books-%d" i) -1L [ makeEvent "BookAdded" (sprintf """{"n":%d}""" i) ] |> ignore
                let api = createSurgeryApi db.Factory db.Path

                let preview = api.previewEventTypeRename "BookAdded" |> Async.RunSynchronously
                Expect.equal preview.Count 5 "Exact count of rows at the old event_type"
                Expect.isLessThanOrEqual (List.length preview.Sample) preview.Count "Sample must never exceed the true count"
                Expect.isTrue (preview.Sample |> List.forall (fun r -> r.EventType = "BookAdded")) "Every sampled row should be of the previewed type"
            finally
                cleanupBackups db.Path

        testCase "editEvent re-syncs events_fts: old text stops matching, new text matches, via the real getEventPage search path" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"originaltoken"}""" ] |> ignore
                let api = createSurgeryApi db.Factory db.Path
                let target = (EventStore.readStream conn "books-1").[0]

                let searchFinds (term: string) =
                    let query: EventPageQuery = { Filter = { EventFilter.empty with Search = Some term }; Before = None; PageSize = 10 }
                    let page = api.getEventPage query |> Async.RunSynchronously
                    page.TotalMatches > 0

                Expect.isTrue (searchFinds "originaltoken") "Pre-edit text should be searchable"

                api.editEvent target.GlobalPosition """{"title":"replacedtoken"}""" "{}" |> Async.RunSynchronously |> ignore

                Expect.isFalse (searchFinds "originaltoken") "After the edit, the stale pre-edit text should no longer be found (FTS was re-synced)"
                Expect.isTrue (searchFinds "replacedtoken") "After the edit, the new text should be found"
            finally
                cleanupBackups db.Path

        testCase "deleteEvent re-syncs events_fts: the deleted event's text is never found again" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"vanishingtoken"}""" ] |> ignore
                let api = createSurgeryApi db.Factory db.Path
                let target = (EventStore.readStream conn "books-1").[0]

                let searchFinds (term: string) =
                    let query: EventPageQuery = { Filter = { EventFilter.empty with Search = Some term }; Before = None; PageSize = 10 }
                    (api.getEventPage query |> Async.RunSynchronously).TotalMatches > 0

                Expect.isTrue (searchFinds "vanishingtoken") "Pre-delete text should be searchable"
                api.deleteEvent target.GlobalPosition |> Async.RunSynchronously |> ignore
                Expect.isFalse (searchFinds "vanishingtoken") "After delete, the vanished event's text should never be found again"
            finally
                cleanupBackups db.Path

        testCase "any surgery mutation rewinds every checkpoint-tracked projection to dirty" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "Friend-alice" -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
                for handler in allProjectionHandlers do
                    Projection.runProjection conn handler
                Expect.isEmpty (Administration.isAnyProjectionDirty conn allProjectionHandlers (Administration.makeGuards ())) "Precondition: projections should be caught up before the mutation"

                let api = createSurgeryApi db.Factory db.Path
                let target = (EventStore.readStream conn "Friend-alice").[0]
                api.editEvent target.GlobalPosition target.Data "{}" |> Async.RunSynchronously |> ignore

                let dirty = Administration.isAnyProjectionDirty conn allProjectionHandlers (Administration.makeGuards ())
                for handler in allProjectionHandlers do
                    Expect.contains dirty handler.Name (sprintf "%s should be reported dirty after the surgery mutation" handler.Name)

                for handler in allProjectionHandlers do
                    let position, _ = Projection.getCheckpointInfo conn handler.Name
                    Expect.equal position 0L (sprintf "%s's checkpoint should be rewound to 0" handler.Name)
            finally
                cleanupBackups db.Path

        testCase "renameEventType updates every occurrence and getDistinctEventTypes never shows the old name afterward" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" "{}"; makeEvent "BookAdded" "{}" ] |> ignore
                EventStore.appendToStream conn "books-2" -1L [ makeEvent "BookAdded" "{}" ] |> ignore
                let api = createSurgeryApi db.Factory db.Path

                match api.renameEventType "BookAdded" "Book_added" |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected rename to succeed, got BackupFailed: %s" reason)
                | Applied(_, affected) -> Expect.equal affected 3 "All three rows should be renamed"

                let types = EventStore.getDistinctEventTypes conn
                Expect.contains types "Book_added" "Distinct event types should include the new name"
                Expect.isFalse (types |> List.contains "BookAdded") "Distinct event types should never show the old name after a rename"
                Expect.equal (api.getEventTypes () |> Async.RunSynchronously |> List.contains "BookAdded") false
                    "getEventTypes (the explorer's live filter source) should never show the old name either"
            finally
                cleanupBackups db.Path

        testCase "deleting an event mid-stream and running Rebuild-all yields a projection consistent with replaying only the surviving events" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                let slug = "delete-test-movie"
                let streamId = Movies.streamId slug
                let addedData: Movies.MovieAddedData = {
                    Name = "Delete Test"; Year = 2020; Runtime = None; Overview = ""
                    Genres = []; PosterRef = None; BackdropRef = None; TmdbId = 1; TmdbRating = None
                }
                EventStore.appendToStream conn streamId -1L [
                    Movies.Serialization.toEventData (Movies.Movie_added_to_library addedData)
                    Movies.Serialization.toEventData (Movies.Movie_categorized [ "Action" ])
                    Movies.Serialization.toEventData (Movies.Movie_categorized [ "Drama" ])
                ] |> ignore

                // An unrelated stream must be left entirely undisturbed by the
                // delete + rebuild — this is the "no other stream's projection
                // state is disturbed" half of the acceptance criterion.
                let otherSlug = "untouched-movie"
                let otherStreamId = Movies.streamId otherSlug
                let otherData: Movies.MovieAddedData = {
                    Name = "Untouched"; Year = 1999; Runtime = None; Overview = ""
                    Genres = [ "Comedy" ]; PosterRef = None; BackdropRef = None; TmdbId = 2; TmdbRating = None
                }
                EventStore.appendToStream conn otherStreamId -1L [ Movies.Serialization.toEventData (Movies.Movie_added_to_library otherData) ] |> ignore

                Projection.runProjection conn MovieProjection.handler

                let api = createSurgeryApi db.Factory db.Path
                let middleEvent = (EventStore.readStream conn streamId).[1] // Movie_categorized "Action"
                match api.deleteEvent middleEvent.GlobalPosition |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected delete to succeed, got BackupFailed: %s" reason)
                | Applied(_, affected) -> Expect.equal affected 1 "Exactly one row should be deleted"

                // Rebuild-all's own single-projection operation (administration-qjcp4, ADR-0024): drop+reinit+replay-from-0.
                Projection.rebuildProjectionWithProgress conn MovieProjection.handler (fun _ -> ())

                let genresOf (targetSlug: string) =
                    conn
                    |> Db.newCommand "SELECT genres FROM movie_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String targetSlug ]
                    |> Db.querySingle (fun rd -> rd.ReadString "genres")

                Expect.equal (genresOf slug) (Some """["Drama"]""")
                    "The rebuilt projection should reflect ONLY the surviving Movie_categorized event (Drama) — the deleted one (Action) must have no effect, exactly as if it had never been appended"
                Expect.equal (genresOf otherSlug) (Some """["Comedy"]""")
                    "An unrelated stream's projection state must be completely undisturbed by another stream's surgery + rebuild"
            finally
                cleanupBackups db.Path

        testCase "backup retention is keep-all: 3 surgeries leave 3 backup files, and getBackupStats matches an independent directory walk" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [
                    makeEvent "BookAdded" "{}"
                    makeEvent "BookAdded" "{}"
                    makeEvent "BookAdded" "{}"
                ] |> ignore
                let api = createSurgeryApi db.Factory db.Path
                let events = EventStore.readStream conn "books-1"

                api.editEvent events.[0].GlobalPosition "{}" "{}" |> Async.RunSynchronously |> ignore
                api.editEvent events.[1].GlobalPosition "{}" "{}" |> Async.RunSynchronously |> ignore
                api.editEvent events.[2].GlobalPosition "{}" "{}" |> Async.RunSynchronously |> ignore

                let dir = backupsDirFor db.Path
                let filesOnDisk = Directory.GetFiles(dir)
                Expect.equal filesOnDisk.Length 3 "Three surgeries should leave exactly three backup files (keep-all, no pruning)"

                let stats = api.getBackupStats () |> Async.RunSynchronously
                Expect.equal stats.Count filesOnDisk.Length "getBackupStats' count should match an independent directory walk"
                let expectedBytes = filesOnDisk |> Array.sumBy (fun f -> (FileInfo(f)).Length)
                Expect.equal stats.TotalBytes expectedBytes "getBackupStats' total bytes should match an independent directory walk"
            finally
                cleanupBackups db.Path

        testCase "getBackupStats reports zero before any surgery has ever run" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            try
                let api = createSurgeryApi db.Factory db.Path
                let stats = api.getBackupStats () |> Async.RunSynchronously
                Expect.equal stats.Count 0 "No backups/ directory yet — zero count"
                Expect.equal stats.TotalBytes 0L "No backups/ directory yet — zero bytes"
            finally
                cleanupBackups db.Path

        testCase "a surgery commit fired concurrently with a burst of addFriend calls on separate factory-drawn connections completes with zero SqliteConnection exceptions, and both effects land" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                EventStore.appendToStream conn "books-1" -1L [ makeEvent "BookAdded" """{"title":"concurrency target"}""" ] |> ignore
                let surgeryApi = createSurgeryApi db.Factory db.Path
                let mediathecaApi =
                    Api.create
                        db.Factory
                        (new HttpClient())
                        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
                        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
                        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
                        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
                        noImagesDir
                        []

                let target = (EventStore.readStream conn "books-1").[0]
                let friendCount = 20
                let exceptions = System.Collections.Concurrent.ConcurrentBag<exn>()
                let friendResults = System.Collections.Concurrent.ConcurrentBag<Result<string, string>>()
                let mutable surgeryResult : SurgeryResult option = None

                let friendTasks =
                    [ 1 .. friendCount ]
                    |> List.map (fun i ->
                        Task.Run(fun () ->
                            try
                                let result = mediathecaApi.addFriend (sprintf "Concurrent Friend %d" i) |> Async.RunSynchronously
                                friendResults.Add(result)
                            with ex ->
                                exceptions.Add(ex)))

                let surgeryTask =
                    Task.Run(fun () ->
                        try
                            surgeryResult <- Some (surgeryApi.editEvent target.GlobalPosition """{"title":"concurrency fixed"}""" "{}" |> Async.RunSynchronously)
                        with ex ->
                            exceptions.Add(ex))

                let allTasks = surgeryTask :: friendTasks |> List.toArray
                Task.WaitAll(allTasks, TimeSpan.FromSeconds(30.0)) |> ignore

                Expect.isEmpty (exceptions |> List.ofSeq)
                    "No concurrent surgery/addFriend call should throw — per-request connections must not crash any request"

                match surgeryResult with
                | None -> failtest "Surgery task should have completed"
                | Some (BackupFailed reason) -> failtest (sprintf "Expected surgery to succeed, got BackupFailed: %s" reason)
                | Some (Applied(_, affected)) -> Expect.equal affected 1 "The surgery's mutation should have landed"

                let oks = friendResults |> List.ofSeq |> List.choose (function Ok slug -> Some slug | Error _ -> None)
                Expect.equal (List.length oks) friendCount "Every concurrent addFriend should have succeeded"
                Expect.equal (oks |> List.distinct |> List.length) friendCount "Every friend should have a distinct slug — no lost or duplicated command"

                match EventStore.getEventByGlobalPosition conn target.GlobalPosition with
                | None -> failtest "The edited event should still exist"
                | Some row -> Expect.equal row.Data """{"title":"concurrency fixed"}""" "The surgery's edit should have landed despite the concurrent burst"
            finally
                cleanupBackups db.Path
    ]
