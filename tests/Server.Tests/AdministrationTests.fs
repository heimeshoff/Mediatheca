module Mediatheca.Tests.AdministrationTests

open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

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

/// In-memory test databases have no backing file, so a nonexistent path is a
/// legitimate stand-in for dbPath/imagesDir wherever a test doesn't care
/// about storage stats specifically.
let private noStoragePath = "test-fixtures-do-not-exist/nowhere.db"
let private noImagesDir = "test-fixtures-do-not-exist/images"

let private createApi conn = Administration.create conn noStoragePath noImagesDir

[<Tests>]
let administrationTests =
    testList "Administration" [

        testCase "getEventPage returns events served through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" """{"name":"Dune"}""" ] |> ignore
            let api = createApi conn

            let query: EventPageQuery = { Filter = EventFilter.empty; Before = None; PageSize = 100 }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal (List.length page.Events) 1 "Should return the one appended event"
            Expect.equal page.Events.[0].StreamId "movies-dune-2021" "Stream id should match"
            Expect.equal page.Events.[0].EventType "MovieAdded" "Event type should match"
            Expect.equal page.TotalMatches 1 "Total matches should count the one event"
            Expect.isFalse page.HasMore "Single event should not have more pages"

        testCase "getEventPage resolves BoundedContext filter to a stream_id prefix" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi conn

            let query: EventPageQuery = {
                Filter = { EventFilter.empty with BoundedContext = Some "Movies" }
                Before = None
                PageSize = 100
            }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal page.TotalMatches 1 "Only the Movie- stream event should match"
            Expect.equal page.Events.[0].StreamId "Movie-dune" "Should be the movie event"

        testCase "getBoundedContexts returns the known bounded context names" <| fun _ ->
            let conn = createInMemoryConnection ()
            let api = createApi conn

            let contexts = api.getBoundedContexts () |> Async.RunSynchronously

            Expect.contains contexts "Movies" "Should include Movies"
            Expect.contains contexts "Friends" "Should include Friends"

        testCase "getEventStreams returns distinct stream ids through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "friends-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi conn

            let streams = api.getEventStreams () |> Async.RunSynchronously

            Expect.contains streams "movies-dune-2021" "Should include movies stream"
            Expect.contains streams "friends-alice" "Should include friends stream"

        testCase "getEventTypes returns distinct event types through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "movies-dune-2021" 0L [ makeEvent "MovieRated" "{}" ] |> ignore
            let api = createApi conn

            let types = api.getEventTypes () |> Async.RunSynchronously

            Expect.contains types "MovieAdded" "Should include MovieAdded"
            Expect.contains types "MovieRated" "Should include MovieRated"

        testCase "getHealthStats total event count matches a direct SQL count" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi conn

            let stats = api.getHealthStats () |> Async.RunSynchronously
            use countCmd = conn.CreateCommand()
            countCmd.CommandText <- "SELECT COUNT(*) FROM events"
            let directCount = countCmd.ExecuteScalar() :?> int64 |> int

            Expect.equal stats.TotalEventCount directCount "Health total should match direct SQL count"
            Expect.equal stats.TotalEventCount 3 "Should be 3 events total"

        testCase "getHealthStats per-bounded-context counts are consistent with direct SQL and sum to the total" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "legacy-unprefixed-stream" -1L [ makeEvent "LegacyThing" "{}" ] |> ignore
            let api = createApi conn

            let stats = api.getHealthStats () |> Async.RunSynchronously
            let moviesCount = stats.BoundedContextCounts |> List.find (fun c -> c.BoundedContext = "Movies") |> fun c -> c.Count
            let friendsCount = stats.BoundedContextCounts |> List.find (fun c -> c.BoundedContext = "Friends") |> fun c -> c.Count
            let otherCount = stats.BoundedContextCounts |> List.tryFind (fun c -> c.BoundedContext = "Other") |> Option.map (fun c -> c.Count) |> Option.defaultValue 0

            Expect.equal moviesCount 2 "Movie- prefix should count 2 events"
            Expect.equal friendsCount 1 "Friend- prefix should count 1 event"
            Expect.equal otherCount 1 "Unmatched stream should land in Other"
            Expect.equal (stats.BoundedContextCounts |> List.sumBy (fun c -> c.Count)) stats.TotalEventCount "Per-BC counts should sum to the total"

        testCase "getHealthStats top streams are ordered by event count descending" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}"; makeEvent "MovieWatched" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi conn

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.equal stats.TopStreams.[0].StreamId "Movie-dune" "Movie-dune has the most events"
            Expect.equal stats.TopStreams.[0].Count 3 "Movie-dune has 3 events"

        testCase "getHealthStats distinct event type count and top event types match direct data" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Movie-arrival" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            let api = createApi conn

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.equal stats.DistinctEventTypeCount 2 "MovieAdded and MovieRated are the only two types"
            Expect.equal stats.TopEventTypes.[0].EventType "MovieAdded" "MovieAdded (2 occurrences) should rank above MovieRated (1)"
            Expect.equal stats.TopEventTypes.[0].Count 2 "MovieAdded occurs twice"

        testCase "getHealthStats daily counts cover a 90-day window and bucket today's events correctly" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            let api = createApi conn

            let stats = api.getHealthStats () |> Async.RunSynchronously
            let today = System.DateTime.UtcNow.Date.ToString("yyyy-MM-dd")
            let todayCount = stats.DailyCounts |> List.find (fun d -> d.Date = today) |> fun d -> d.Count

            Expect.equal (List.length stats.DailyCounts) 90 "Should cover a 90-day window"
            Expect.equal todayCount 2 "Today's bucket should have the 2 events just appended"
            Expect.isTrue (stats.DailyCounts |> List.forall (fun d -> d.Count >= 0)) "All counts should be non-negative"

        testCase "getHealthStats storage stats reflect the actual data dir" <| fun _ ->
            let conn = createInMemoryConnection ()
            let tempRoot = Path.Combine(Path.GetTempPath(), "mediatheca-health-test-" + System.Guid.NewGuid().ToString("N"))
            let imagesDir = Path.Combine(tempRoot, "images")
            Directory.CreateDirectory(imagesDir) |> ignore
            let dbPath = Path.Combine(tempRoot, "mediatheca.db")
            File.WriteAllBytes(dbPath, Array.create 1024 0uy)
            File.WriteAllBytes(Path.Combine(imagesDir, "poster1.jpg"), Array.create 512 0uy)
            File.WriteAllBytes(Path.Combine(imagesDir, "poster2.jpg"), Array.create 256 0uy)

            try
                let api = Administration.create conn dbPath imagesDir
                let stats = api.getHealthStats () |> Async.RunSynchronously

                Expect.equal stats.Storage.DbSizeBytes 1024L "DB size should match the file on disk"
                Expect.equal stats.Storage.ImagesSizeBytes 768L "Images size should sum both files"
                Expect.equal stats.Storage.ImagesFileCount 2 "Images file count should be 2"
                Expect.equal stats.Storage.WalSizeBytes 0L "No WAL sidecar was written, so its size is 0"
            finally
                Directory.Delete(tempRoot, true)
    ]
