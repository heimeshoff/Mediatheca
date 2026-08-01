module Mediatheca.Tests.AdministrationTests

open System.IO
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

/// administration-mz6kp (ADR-0033): schema-only bootstrap for a
/// `TestDb.TempDb`'s single long-lived `Connection` — mirrors what the old
/// `createInMemoryConnection` did on its single `:memory:` connection, now
/// run once against the fixture's file-backed connection before the
/// `Factory` is handed to `Administration.create`.
let private bootstrapAdmin (conn: SqliteConnection) =
    EventStore.initialize conn
    // Stream drill-in's projection panel (administration-v4y9g) dispatches to
    // each BC's projection getBySlug, which — like the running server, which
    // always has every projection initialized — expects these tables to
    // exist for any Movie-/Series-/Game-/Friend-/Catalog- stream.
    CastStore.initialize conn
    JellyfinStore.initialize conn
    // Image cache admin (administration-xx3mw) exercises game_journal_blocks,
    // an imperative table (GameJournal.fs) that's otherwise only created
    // lazily by Composition.fs at startup.
    GameJournal.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    CatalogProjection.handler.Init conn
    // Job runs console (administration-yamm5): table + startup reconciliation,
    // same as Composition.fs's init sequence, so any test exercising
    // getJobStatuses/runJobNow has the table ready.
    Administration.initializeJobRuns conn

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

let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    ContentBlockProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
]

/// Most Administration tests don't exercise the Jobs tab — an empty registry
/// keeps createApi/createImageApi callers unchanged. Job-runs-specific tests
/// (JobRunsTests.fs) build their own scheduledJobs/recorder directly.
// administration-tj8n2: makeJobRunRecorder now also takes the per-command
// SemaphoreSlim that guards the (real-deployment) dedicated job connection —
// a fresh, uncontended lock is enough for these tests, which don't exercise
// job-connection concurrency (JobRunsTests.fs / JobConnectionConcurrencyTests.fs do).
let private createApi (factory: unit -> SqliteConnection) =
    Administration.create factory noStoragePath noImagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder (factory ()) (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())

let private createImageApi (factory: unit -> SqliteConnection) imagesDir =
    Administration.create factory noStoragePath imagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder (factory ()) (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())

// ── Image cache admin (administration-xx3mw) test helpers ──

let private withTempImagesDir (f: string -> unit) : unit =
    let tempRoot = Path.Combine(Path.GetTempPath(), "mediatheca-images-test-" + System.Guid.NewGuid().ToString("N"))
    let imagesDir = Path.Combine(tempRoot, "images")
    Directory.CreateDirectory(imagesDir) |> ignore
    try
        f imagesDir
    finally
        Directory.Delete(tempRoot, true)

let private writeImageFile (imagesDir: string) (relativePath: string) (byteCount: int) : unit =
    let fullPath = Path.Combine(imagesDir, relativePath.Replace('/', Path.DirectorySeparatorChar))
    let dir = Path.GetDirectoryName(fullPath)
    if not (Directory.Exists(dir)) then Directory.CreateDirectory(dir) |> ignore
    File.WriteAllBytes(fullPath, Array.create byteCount 0uy)

let private insertMoviePosterRef (conn: SqliteConnection) (slug: string) (posterRef: string) : unit =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO movie_list (slug, name, year, poster_ref) VALUES (@slug, @slug, 2000, @ref)"
    cmd.Parameters.AddWithValue("@slug", slug) |> ignore
    cmd.Parameters.AddWithValue("@ref", posterRef) |> ignore
    cmd.ExecuteNonQuery() |> ignore

let private insertContentBlock (conn: SqliteConnection) (blockId: string) (movieSlug: string) (imageRef: string) : unit =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO content_blocks (block_id, movie_slug, block_type, image_ref) VALUES (@id, @slug, 'screenshot', @ref)"
    cmd.Parameters.AddWithValue("@id", blockId) |> ignore
    cmd.Parameters.AddWithValue("@slug", movieSlug) |> ignore
    cmd.Parameters.AddWithValue("@ref", imageRef) |> ignore
    cmd.ExecuteNonQuery() |> ignore

let private insertSeriesEpisode (conn: SqliteConnection) (seriesSlug: string) (season: int) (episode: int) (stillRef: string) : unit =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO series_episode_cache (series_slug, season_number, episode_number, still_ref) VALUES (@s, @se, @ep, @ref)"
    cmd.Parameters.AddWithValue("@s", seriesSlug) |> ignore
    cmd.Parameters.AddWithValue("@se", season) |> ignore
    cmd.Parameters.AddWithValue("@ep", episode) |> ignore
    cmd.Parameters.AddWithValue("@ref", stillRef) |> ignore
    cmd.ExecuteNonQuery() |> ignore

/// series-m7fdk: the season-poster counterpart to `insertSeriesEpisode`,
/// against the renamed `series_season_cache` (formerly `series_seasons`).
let private insertSeriesSeasonPoster (conn: SqliteConnection) (seriesSlug: string) (season: int) (posterRef: string) : unit =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO series_season_cache (series_slug, season_number, poster_ref) VALUES (@s, @se, @ref)"
    cmd.Parameters.AddWithValue("@s", seriesSlug) |> ignore
    cmd.Parameters.AddWithValue("@se", season) |> ignore
    cmd.Parameters.AddWithValue("@ref", posterRef) |> ignore
    cmd.ExecuteNonQuery() |> ignore

/// Simulates a cast member's image being dropped (the realistic path to "no
/// row references it" — `cast_members` itself is the ref-bearing row per
/// ADR-0025, so clearing its own image_ref is what makes the file orphan;
/// deleting the row outright would trip movie_cast's FK).
let private clearCastMemberImageRef (conn: SqliteConnection) (id: int64) : unit =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "UPDATE cast_members SET image_ref = NULL WHERE id = @id"
    cmd.Parameters.AddWithValue("@id", id) |> ignore
    cmd.ExecuteNonQuery() |> ignore

[<Tests>]
let administrationTests =
    testList "Administration" [

        testCase "getEventPage returns events served through IAdminApi" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" """{"name":"Dune"}""" ] |> ignore
            let api = createApi db.Factory

            let query: EventPageQuery = { Filter = EventFilter.empty; Before = None; PageSize = 100 }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal (List.length page.Events) 1 "Should return the one appended event"
            Expect.equal page.Events.[0].StreamId "movies-dune-2021" "Stream id should match"
            Expect.equal page.Events.[0].EventType "MovieAdded" "Event type should match"
            Expect.equal page.TotalMatches 1 "Total matches should count the one event"
            Expect.isFalse page.HasMore "Single event should not have more pages"

        testCase "getEventPage resolves BoundedContext filter to a stream_id prefix" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let query: EventPageQuery = {
                Filter = { EventFilter.empty with BoundedContext = Some "Movies" }
                Before = None
                PageSize = 100
            }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal page.TotalMatches 1 "Only the Movie- stream event should match"
            Expect.equal page.Events.[0].StreamId "Movie-dune" "Should be the movie event"

        testCase "getBoundedContexts returns the known bounded context names" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let api = createApi db.Factory

            let contexts = api.getBoundedContexts () |> Async.RunSynchronously

            Expect.contains contexts "Movies" "Should include Movies"
            Expect.contains contexts "Friends" "Should include Friends"

        testCase "getEventStreams returns distinct stream ids through IAdminApi" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "friends-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let streams = api.getEventStreams () |> Async.RunSynchronously

            Expect.contains streams "movies-dune-2021" "Should include movies stream"
            Expect.contains streams "friends-alice" "Should include friends stream"

        testCase "getEventsAfter returns only events after the given position through IAdminApi" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            let api = createApi db.Factory
            let firstPage = api.getEventPage { Filter = EventFilter.empty; Before = None; PageSize = 100 } |> Async.RunSynchronously
            let seenPosition = firstPage.Events.[0].GlobalPosition

            EventStore.appendToStream conn "friends-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore

            let tail = api.getEventsAfter { Filter = EventFilter.empty; After = seenPosition; Limit = 100 } |> Async.RunSynchronously

            Expect.equal (List.length tail) 1 "Should return only the newly appended event"
            Expect.equal tail.[0].StreamId "friends-alice" "Should be the newly appended event"

        testCase "getEventsAfter resolves BoundedContext filter to a stream_id prefix" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let tail = api.getEventsAfter { Filter = { EventFilter.empty with BoundedContext = Some "Movies" }; After = 0L; Limit = 100 } |> Async.RunSynchronously

            Expect.equal (List.length tail) 1 "Only the Movie- stream event should match"
            Expect.equal tail.[0].StreamId "Movie-dune" "Should be the movie event"

        testCase "getEventTypes returns distinct event types through IAdminApi" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "movies-dune-2021" 0L [ makeEvent "MovieRated" "{}" ] |> ignore
            let api = createApi db.Factory

            let types = api.getEventTypes () |> Async.RunSynchronously

            Expect.contains types "MovieAdded" "Should include MovieAdded"
            Expect.contains types "MovieRated" "Should include MovieRated"

        testCase "getStreamDetail returns the stream's events in order with formatted labels and cross-links" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Movies.streamId "the-matrix-1999"
            let addedData: Movies.MovieAddedData = {
                Name = "The Matrix"; Year = 1999; Runtime = None; Overview = ""
                Genres = []; PosterRef = None; BackdropRef = None; TmdbId = 603; TmdbRating = None
            }
            let addedEvent = Movies.Serialization.toEventData (Movies.Movie_added_to_library addedData)
            let recommendedEvent = Movies.Serialization.toEventData (Movies.Movie_recommended_by "alice")
            EventStore.appendToStream conn streamId -1L [ addedEvent ] |> ignore
            EventStore.appendToStream conn streamId 0L [ recommendedEvent ] |> ignore
            let api = createApi db.Factory

            let detail = api.getStreamDetail streamId |> Async.RunSynchronously

            Expect.equal detail.StreamId streamId "Stream id should match"
            Expect.equal (List.length detail.Entries) 2 "Should return both events"
            Expect.equal detail.Entries.[0].EventType "Movie_added_to_library" "First event should be the earliest by stream position"
            Expect.equal detail.Entries.[0].FormattedLabel (Some "Added to library") "First event should be formatted"
            Expect.equal detail.Entries.[1].EventType "Movie_recommended_by" "Second event should be the recommendation"
            Expect.equal detail.Entries.[1].FormattedLabel (Some "Recommendation added") "Second event should be formatted"
            Expect.equal detail.Entries.[1].CrossLinks [ { Kind = "Friend"; TargetStreamId = "Friend-alice" } ] "Recommendation event should cross-link to the friend's stream"

        testCase "getStreamDetail marks events with no known formatter as unformatted raw JSON" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Movies.streamId "the-matrix-1999"
            EventStore.appendToStream conn streamId -1L [ makeEvent "Some_unknown_event" """{"foo":"bar"}""" ] |> ignore
            let api = createApi db.Factory

            let detail = api.getStreamDetail streamId |> Async.RunSynchronously

            Expect.equal (List.length detail.Entries) 1 "Should return the one event"
            Expect.isNone detail.Entries.[0].FormattedLabel "Unknown event type should have no formatted label"
            Expect.equal detail.Entries.[0].Data """{"foo":"bar"}""" "Raw data should still be available"

        testCase "getStreamDetail formats Game_rawg_id_set with the RAWG id and rating (administration-qk3f7)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Games.streamId "some-game"
            EventStore.appendToStream conn streamId -1L
                [ Games.Serialization.toEventData (Games.Game_rawg_id_set (12345, Some 4.2)) ] |> ignore
            let api = createApi db.Factory

            let detail = api.getStreamDetail streamId |> Async.RunSynchronously

            Expect.equal (List.length detail.Entries) 1 "Should return the one event"
            Expect.equal detail.Entries.[0].FormattedLabel (Some "RAWG ID set") "Game_rawg_id_set should now have a formatted label"
            Expect.equal detail.Entries.[0].FormattedDetails [ "RAWG ID: 12345"; $"Rating: {4.2}" ] "Details should reflect the RAWG id and rating"

        testCase "getStreamDetail dispatches the projection panel by stream prefix" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Movies.streamId "the-matrix-1999"
            let addedData: Movies.MovieAddedData = {
                Name = "The Matrix"; Year = 1999; Runtime = None; Overview = ""
                Genres = [ "Action" ]; PosterRef = None; BackdropRef = None; TmdbId = 603; TmdbRating = Some 8.7
            }
            EventStore.appendToStream conn streamId -1L [ Movies.Serialization.toEventData (Movies.Movie_added_to_library addedData) ] |> ignore
            Projection.runProjection conn MovieProjection.handler
            let api = createApi db.Factory

            let detail = api.getStreamDetail streamId |> Async.RunSynchronously

            Expect.equal (List.length detail.ProjectionRows) 1 "Should have one projection row for the Movie stream"
            let row = detail.ProjectionRows.[0]
            Expect.equal row.Kind "Movie" "Projection row kind should be Movie"
            Expect.contains row.Fields ("Name", "The Matrix") "Fields should include the movie name"
            Expect.equal row.DetailLink (Some ("movies", "the-matrix-1999")) "Should link to the movie detail page"

        testCase "getStreamDetail returns no projection row for a stream prefix with no projection dispatch" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = "ContentBlocks-the-matrix-1999"
            EventStore.appendToStream conn streamId -1L [ makeEvent "Content_block_added" """{"blockType":"text"}""" ] |> ignore
            let api = createApi db.Factory

            let detail = api.getStreamDetail streamId |> Async.RunSynchronously

            Expect.isEmpty detail.ProjectionRows "ContentBlocks streams have no projection panel dispatch"

        testCase "getHealthStats total event count matches a direct SQL count" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously
            use countCmd = conn.CreateCommand()
            countCmd.CommandText <- "SELECT COUNT(*) FROM events"
            let directCount = countCmd.ExecuteScalar() :?> int64 |> int

            Expect.equal stats.TotalEventCount directCount "Health total should match direct SQL count"
            Expect.equal stats.TotalEventCount 3 "Should be 3 events total"

        testCase "getHealthStats per-bounded-context counts are consistent with direct SQL and sum to the total" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "legacy-unprefixed-stream" -1L [ makeEvent "LegacyThing" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously
            let moviesCount = stats.BoundedContextCounts |> List.find (fun c -> c.BoundedContext = "Movies") |> fun c -> c.Count
            let friendsCount = stats.BoundedContextCounts |> List.find (fun c -> c.BoundedContext = "Friends") |> fun c -> c.Count
            let otherCount = stats.BoundedContextCounts |> List.tryFind (fun c -> c.BoundedContext = "Other") |> Option.map (fun c -> c.Count) |> Option.defaultValue 0

            Expect.equal moviesCount 2 "Movie- prefix should count 2 events"
            Expect.equal friendsCount 1 "Friend- prefix should count 1 event"
            Expect.equal otherCount 1 "Unmatched stream should land in Other"
            Expect.equal (stats.BoundedContextCounts |> List.sumBy (fun c -> c.Count)) stats.TotalEventCount "Per-BC counts should sum to the total"

        testCase "getHealthStats top streams are ordered by event count descending" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}"; makeEvent "MovieWatched" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.equal stats.TopStreams.[0].StreamId "Movie-dune" "Movie-dune has the most events"
            Expect.equal stats.TopStreams.[0].Count 3 "Movie-dune has 3 events"

        testCase "getHealthStats distinct event type count and top event types match direct data" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            EventStore.appendToStream conn "Movie-arrival" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.equal stats.DistinctEventTypeCount 2 "MovieAdded and MovieRated are the only two types"
            Expect.equal stats.TopEventTypes.[0].EventType "MovieAdded" "MovieAdded (2 occurrences) should rank above MovieRated (1)"
            Expect.equal stats.TopEventTypes.[0].Count 2 "MovieAdded occurs twice"

        testCase "getHealthStats daily counts cover a 90-day window and bucket today's events correctly" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}"; makeEvent "MovieRated" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously
            let today = System.DateTime.UtcNow.Date.ToString("yyyy-MM-dd")
            let todayCount = stats.DailyCounts |> List.find (fun d -> d.Date = today) |> fun d -> d.Count

            Expect.equal (List.length stats.DailyCounts) 90 "Should cover a 90-day window"
            Expect.equal todayCount 2 "Today's bucket should have the 2 events just appended"
            Expect.isTrue (stats.DailyCounts |> List.forall (fun d -> d.Count >= 0)) "All counts should be non-negative"

        testCase "getHealthStats storage stats reflect the actual data dir" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let tempRoot = Path.Combine(Path.GetTempPath(), "mediatheca-health-test-" + System.Guid.NewGuid().ToString("N"))
            let imagesDir = Path.Combine(tempRoot, "images")
            Directory.CreateDirectory(imagesDir) |> ignore
            let dbPath = Path.Combine(tempRoot, "mediatheca.db")
            File.WriteAllBytes(dbPath, Array.create 1024 0uy)
            File.WriteAllBytes(Path.Combine(imagesDir, "poster1.jpg"), Array.create 512 0uy)
            File.WriteAllBytes(Path.Combine(imagesDir, "poster2.jpg"), Array.create 256 0uy)

            try
                let api = Administration.create db.Factory dbPath imagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder conn (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())
                let stats = api.getHealthStats () |> Async.RunSynchronously

                Expect.equal stats.Storage.DbSizeBytes 1024L "DB size should match the file on disk"
                Expect.equal stats.Storage.ImagesSizeBytes 768L "Images size should sum both files"
                Expect.equal stats.Storage.ImagesFileCount 2 "Images file count should be 2"
                Expect.equal stats.Storage.WalSizeBytes 0L "No WAL sidecar was written, so its size is 0"
            finally
                Directory.Delete(tempRoot, true)

        // ── Unknown-event report (administration-gxd6e) ──

        testCase "getHealthStats unhandled list flags a fabricated event type bypassing Serialization.toEventData, with the correct count" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            // makeEvent/appendToStream builds EventData directly — none of the
            // BCs' Serialization.toEventData helpers are involved, matching the
            // task's "bypassing all Serialization.toEventData helpers" phrasing.
            EventStore.appendToStream conn "Movie-dune" -1L [
                makeEvent "Totally_unknown_event_type" "{}"
                makeEvent "Totally_unknown_event_type" "{}"
            ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously
            let row = stats.UnhandledEventTypes |> List.tryFind (fun r -> r.EventType = "Totally_unknown_event_type")

            Expect.isSome row "Fabricated unknown event type should appear in the unhandled list"
            Expect.equal row.Value.Count 2 "Count should match the two occurrences inserted directly"

        testCase "getHealthStats a real, currently-handled event type appears in neither the unhandled nor the unformattable list" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let addedData: Movies.MovieAddedData = {
                Name = "The Matrix"; Year = 1999; Runtime = None; Overview = ""
                Genres = []; PosterRef = None; BackdropRef = None; TmdbId = 603; TmdbRating = None
            }
            EventStore.appendToStream conn (Movies.streamId "the-matrix-1999") -1L
                [ Movies.Serialization.toEventData (Movies.Movie_added_to_library addedData) ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.isEmpty (stats.UnhandledEventTypes |> List.filter (fun r -> r.EventType = "Movie_added_to_library")) "A handled type must not appear in the unhandled list — guards against a registry entry silently drifting out of sync"
            Expect.isEmpty (stats.UnformattableEventTypes |> List.filter (fun r -> r.EventType = "Movie_added_to_library")) "A type with a real formatter case must not appear in the unformattable list"

        testCase "getHealthStats Game_rawg_id_set appears in neither the unhandled nor the unformattable list (administration-qk3f7: drift closed)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            // Games.Serialization.handledEventTypes lists "Game_rawg_id_set" (the
            // deserializer recognizes it — Games.fs:555), and
            // EventFormatting.formatGameEvent now has a matching case
            // (administration-qk3f7), closing the one real handled-but-
            // unformattable drift the unknown-event report caught. This is the
            // Games-BC parallel of the Movie_added_to_library "appears in
            // neither list" test above: handled ⟺ formattable now holds for
            // every real event type in the store.
            EventStore.appendToStream conn (Games.streamId "some-game") -1L
                [ Games.Serialization.toEventData (Games.Game_rawg_id_set (12345, Some 4.2)) ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.isEmpty (stats.UnhandledEventTypes |> List.filter (fun r -> r.EventType = "Game_rawg_id_set")) "Game_rawg_id_set is handled by Games' deserializer, so it must not appear in the unhandled list"
            Expect.isEmpty (stats.UnformattableEventTypes |> List.filter (fun r -> r.EventType = "Game_rawg_id_set")) "Game_rawg_id_set now has a formatter case, so it must not appear in the unformattable list"

        testCase "getHealthStats unhandled list flags an event type whose stream prefix matches no known bounded context" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn "legacy-unprefixed-stream" -1L [ makeEvent "LegacyThing" "{}" ] |> ignore
            let api = createApi db.Factory

            let stats = api.getHealthStats () |> Async.RunSynchronously

            Expect.isNonEmpty (stats.UnhandledEventTypes |> List.filter (fun r -> r.EventType = "LegacyThing")) "An event type on a stream matching no known BC prefix should be flagged unhandled"

        testCase "getProjectionStats lists all registered projections with checkpoint, lag, and row counts" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Friends.streamId "marco"
            EventStore.appendToStream conn streamId -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            let api = createApi db.Factory

            let stats = api.getProjectionStats () |> Async.RunSynchronously

            Expect.equal (List.length stats) (List.length allProjectionHandlers) "Should list every registered projection handler"
            let friendStats = stats |> List.find (fun s -> s.Name = "FriendProjection")
            Expect.equal friendStats.CheckpointPosition 1L "Checkpoint should be at position 1 after catching up on the one event"
            Expect.equal friendStats.Lag 0L "Lag should be 0 once caught up to the store head"
            Expect.isSome friendStats.UpdatedAt "A projection that has checkpointed should report an updated_at"
            Expect.isFalse friendStats.IsRebuilding "No rebuild is in flight"
            let friendListCount = friendStats.TableCounts |> List.find (fun t -> t.TableName = "friend_list")
            Expect.equal friendListCount.RowCount 1 "friend_list should have the one row for Marco"

        testCase "getProjectionStats reports lag when a projection has not caught up to newer events" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn (Friends.streamId "marco") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            // A second event arrives, but the projection is never re-run to catch up on it.
            EventStore.appendToStream conn (Friends.streamId "alice") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
            let api = createApi db.Factory

            let stats = api.getProjectionStats () |> Async.RunSynchronously
            let friendStats = stats |> List.find (fun s -> s.Name = "FriendProjection")

            Expect.equal friendStats.CheckpointPosition 1L "Checkpoint should still be at position 1"
            Expect.equal friendStats.Lag 1L "Lag should be 1: store head (2) minus checkpoint (1)"

        testCase "getProjectionStats reports no checkpoint yet for a projection that has never caught up" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let api = createApi db.Factory

            let stats = api.getProjectionStats () |> Async.RunSynchronously
            let friendStats = stats |> List.find (fun s -> s.Name = "FriendProjection")

            Expect.equal friendStats.CheckpointPosition 0L "Checkpoint should default to 0 before any catch-up"
            Expect.isNone friendStats.UpdatedAt "A projection that has never checkpointed has no updated_at"

        // ── Image cache admin (administration-xx3mw) — see ADR-0025 ──

        testCase "imageRefColumns covers every ref-bearing (table, column) pair with a column that exists in the schema" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection

            Expect.equal (List.length Administration.imageRefColumns) 15 "Registry should list all fifteen ref-bearing columns"

            for (table, column) in Administration.imageRefColumns do
                use cmd = conn.CreateCommand()
                cmd.CommandText <- sprintf "PRAGMA table_info(%s)" table
                use reader = cmd.ExecuteReader()
                let columns = [ while reader.Read() do yield reader.GetString(1) ]
                Expect.contains columns column (sprintf "%s.%s should exist in the schema" table column)

        testCase "getImageCacheStats reports total size/count and a per-subfolder breakdown that sums to the total" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/a.jpg" 100
                writeImageFile imagesDir "backdrops/b.jpg" 200
                writeImageFile imagesDir "loose.txt" 50

                let api = createImageApi db.Factory imagesDir
                let stats = api.getImageCacheStats () |> Async.RunSynchronously

                Expect.equal stats.TotalFileCount 3 "Three files total"
                Expect.equal stats.TotalBytes 350L "Sizes should sum to 350 bytes"
                Expect.equal (stats.Subfolders |> List.sumBy (fun s -> s.SizeBytes)) stats.TotalBytes "Subfolder rows should sum to the total bytes"
                Expect.equal (stats.Subfolders |> List.sumBy (fun s -> s.FileCount)) stats.TotalFileCount "Subfolder rows should sum to the total file count"
                let rootRow = stats.Subfolders |> List.tryFind (fun s -> s.Subfolder = "(root)")
                Expect.isSome rootRow "A loose file directly under images/ should be reported under (root)"
                Expect.equal rootRow.Value.SizeBytes 50L "(root) row should report the loose file's size")

        testCase "listOrphanedImages is blocked while a checkpoint-tracked projection lags behind the store head" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn (Friends.streamId "marco") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            // A second event arrives, but the projection is never re-run — FriendProjection now lags.
            EventStore.appendToStream conn (Friends.streamId "alice") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
            let api = createApi db.Factory

            match api.listOrphanedImages () |> Async.RunSynchronously with
            | OrphanScanBlocked reason -> Expect.stringContains reason "FriendProjection" "Reason should name the lagging projection"
            | OrphanScanReady _ -> failwith "Expected the scan to be blocked while FriendProjection lags"

        testCase "purgeOrphanedImages is blocked while a checkpoint-tracked projection lags behind the store head" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn (Friends.streamId "marco") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            EventStore.appendToStream conn (Friends.streamId "alice") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
            let api = createApi db.Factory

            match api.purgeOrphanedImages PurgeAll |> Async.RunSynchronously with
            | PurgeBlocked reason -> Expect.stringContains reason "FriendProjection" "Reason should name the lagging projection"
            | PurgeDone _ -> failwith "Expected the purge to be blocked while FriendProjection lags"

        testCase "getImageCacheStats remains available while a projection is dirty — stats need no not-dirty guard" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn (Friends.streamId "marco") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            // FriendProjection is never caught up — dirty by lag — but stats don't gate on it.
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/a.jpg" 10
                let api = createImageApi db.Factory imagesDir
                let stats = api.getImageCacheStats () |> Async.RunSynchronously
                Expect.equal stats.TotalFileCount 1 "Stats should compute normally despite the dirty projection")

        testCase "a content_blocks.image_ref (movie journal) is never flagged orphan" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            insertContentBlock conn "block-1" "dune" "content/movie-journal-1.jpg"
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "content/movie-journal-1.jpg" 10
                let api = createImageApi db.Factory imagesDir
                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.isEmpty (orphans |> List.filter (fun o -> o.RelativePath = "content/movie-journal-1.jpg")) "Referenced movie journal image should not be orphan"
                | OrphanScanBlocked reason -> failwith reason)

        testCase "a game_journal_blocks.image_ref (game journal) is never flagged orphan" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let block: JournalBlockDto = {
                Id = "block-1"; ParentId = None; BlockType = JournalBlockTypes.image
                Content = ""; Checked = false; Collapsed = false; Language = None; Url = None
                ImageRef = Some "content/game-journal-1.jpg"; Caption = None; Position = 0; Width = 1.0
            }
            GameJournal.save conn "some-game" [ block ] |> ignore
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "content/game-journal-1.jpg" 10
                let api = createImageApi db.Factory imagesDir
                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.isEmpty (orphans |> List.filter (fun o -> o.RelativePath = "content/game-journal-1.jpg")) "Referenced game journal image should not be orphan"
                | OrphanScanBlocked reason -> failwith reason)

        testCase "a series_episode_cache.still_ref is never flagged orphan" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            insertSeriesEpisode conn "the-wire" 1 2 "stills/the-wire-s01e02.jpg"
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "stills/the-wire-s01e02.jpg" 10
                let api = createImageApi db.Factory imagesDir
                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.isEmpty (orphans |> List.filter (fun o -> o.RelativePath = "stills/the-wire-s01e02.jpg")) "Referenced episode still should not be orphan"
                | OrphanScanBlocked reason -> failwith reason)

        // series-m7fdk (data-loss regression, deliberately its own standalone
        // assertion, not folded into the orphan-scan test above): a stale
        // `imageRefColumns` entry still naming `series_episodes`/
        // `series_seasons` after the rename would make `tableExists` report
        // false for both, so `getReferencedImageRefs` would silently return
        // an empty set for every episode still and season poster in the
        // library — and the ADR-0025 orphan purge reads "referenced by
        // nothing" as license to hard-delete the file. This test calls
        // `getReferencedImageRefs` directly against the renamed cache tables
        // and asserts it is non-empty, so a regression here fails loudly
        // instead of only showing up as "my episode stills vanished after a
        // purge".
        testCase "getReferencedImageRefs (ADR-0025 data-loss regression) returns a non-empty set for a renamed episode still and season poster" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            insertSeriesEpisode conn "the-wire" 1 2 "stills/the-wire-s01e02.jpg"
            insertSeriesSeasonPoster conn "the-wire" 1 "posters/the-wire-s01.jpg"

            let referenced = Administration.getReferencedImageRefs conn

            Expect.isNonEmpty referenced
                "getReferencedImageRefs must return a non-empty set once series_episode_cache/series_season_cache hold live stills/posters"
            Expect.isTrue (Set.contains "stills/the-wire-s01e02.jpg" referenced)
                "series_episode_cache.still_ref must be counted as a live reference after the rename"
            Expect.isTrue (Set.contains "posters/the-wire-s01.jpg" referenced)
                "series_season_cache.poster_ref must be counted as a live reference after the rename"

        testCase "a cast/<id>.jpg is not flagged orphan while a cast_members row references it, and is flagged once no row does" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let castId = CastStore.upsertCastMember conn "Actor" 999 (Some "cast/999.jpg")
            // Shared by more than one movie — sharing doesn't change how liveness is derived
            // (cast_members.image_ref alone is the ref-bearing column per ADR-0025).
            CastStore.addMovieCast conn "Movie-dune" castId "Self" 0 true
            CastStore.addMovieCast conn "Movie-arrival" castId "Self" 0 true
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "cast/999.jpg" 10
                let api = createImageApi db.Factory imagesDir

                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.isEmpty (orphans |> List.filter (fun o -> o.RelativePath = "cast/999.jpg")) "Shared cast image should not be orphan while cast_members references it"
                | OrphanScanBlocked reason -> failwith reason

                clearCastMemberImageRef conn castId

                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.isNonEmpty (orphans |> List.filter (fun o -> o.RelativePath = "cast/999.jpg")) "Cast image should be orphan once no cast_members row references it"
                | OrphanScanBlocked reason -> failwith reason)

        testCase "path comparison is separator-normalized and case-sensitive: a case-mismatched name is treated as orphan" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            insertMoviePosterRef conn "dune" "posters/dune.jpg"
            withTempImagesDir (fun imagesDir ->
                // The on-disk file's actual casing differs from the stored ref's —
                // ordinal comparison must not fold case (ADR-0025: matches the
                // case-sensitive Linux deploy target). Only one file is ever written
                // here (dev runs on a case-insensitive Windows filesystem, so writing
                // both "dune.jpg" and "Dune.jpg" would collide onto the same file).
                writeImageFile imagesDir "posters/Dune.jpg" 10
                let api = createImageApi db.Factory imagesDir

                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, _) ->
                    Expect.equal (List.length orphans) 1 "Case-mismatched file should be flagged orphan even though a same-named ref exists"
                    Expect.equal orphans.[0].RelativePath "posters/Dune.jpg" "Relative path should be forward-slash normalized and preserve on-disk casing"
                | OrphanScanBlocked reason -> failwith reason)

        testCase "a genuinely unreferenced file, including a stray non-image file, appears in the orphan list with correct path and size" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/orphan-poster.jpg" 100
                writeImageFile imagesDir "notes.txt" 30
                let api = createImageApi db.Factory imagesDir

                match api.listOrphanedImages () |> Async.RunSynchronously with
                | OrphanScanReady (orphans, totalBytes) ->
                    let poster = orphans |> List.find (fun o -> o.RelativePath = "posters/orphan-poster.jpg")
                    Expect.equal poster.SizeBytes 100L "Orphaned poster should report its correct size"
                    let stray = orphans |> List.find (fun o -> o.RelativePath = "notes.txt")
                    Expect.equal stray.SizeBytes 30L "Stray non-image file should report its correct size"
                    Expect.equal stray.Subfolder "(root)" "Loose file should be reported under (root)"
                    Expect.equal totalBytes 130L "Total orphan bytes should sum both files"
                | OrphanScanBlocked reason -> failwith reason)

        testCase "purging a specific subset deletes exactly that subset and nothing else" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/orphan1.jpg" 10
                writeImageFile imagesDir "posters/orphan2.jpg" 20
                let api = createImageApi db.Factory imagesDir

                match api.purgeOrphanedImages (PurgeSpecific [ "posters/orphan1.jpg" ]) |> Async.RunSynchronously with
                | PurgeDone (deletedCount, bytesFreed, skipped) ->
                    Expect.equal deletedCount 1 "Only the requested file should be deleted"
                    Expect.equal bytesFreed 10L "Bytes freed should match the deleted file's size"
                    Expect.isEmpty skipped "Nothing should be skipped"
                    Expect.isFalse (File.Exists(Path.Combine(imagesDir, "posters", "orphan1.jpg"))) "orphan1 should be deleted"
                    Expect.isTrue (File.Exists(Path.Combine(imagesDir, "posters", "orphan2.jpg"))) "orphan2 should be untouched"
                | PurgeBlocked reason -> failwith reason)

        testCase "purging PurgeAll deletes every currently-detected orphan and leaves referenced files alone" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            insertMoviePosterRef conn "dune" "posters/dune.jpg"
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/dune.jpg" 10
                writeImageFile imagesDir "posters/orphan1.jpg" 20
                writeImageFile imagesDir "posters/orphan2.jpg" 30
                let api = createImageApi db.Factory imagesDir

                match api.purgeOrphanedImages PurgeAll |> Async.RunSynchronously with
                | PurgeDone (deletedCount, bytesFreed, skipped) ->
                    Expect.equal deletedCount 2 "Both orphans should be deleted"
                    Expect.equal bytesFreed 50L "Bytes freed should sum both orphans"
                    Expect.isEmpty skipped "Nothing should be skipped"
                    Expect.isTrue (File.Exists(Path.Combine(imagesDir, "posters", "dune.jpg"))) "Referenced poster should remain"
                    Expect.isFalse (File.Exists(Path.Combine(imagesDir, "posters", "orphan1.jpg"))) "orphan1 should be deleted"
                    Expect.isFalse (File.Exists(Path.Combine(imagesDir, "posters", "orphan2.jpg"))) "orphan2 should be deleted"
                | PurgeBlocked reason -> failwith reason)

        testCase "purge re-derives at commit: a path that became referenced since the scan is skipped, not deleted" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/became-referenced.jpg" 10
                let api = createImageApi db.Factory imagesDir
                // Simulate the file becoming referenced after the client's held scan
                // but before the purge call commits.
                insertMoviePosterRef conn "newly-added" "posters/became-referenced.jpg"

                match api.purgeOrphanedImages (PurgeSpecific [ "posters/became-referenced.jpg" ]) |> Async.RunSynchronously with
                | PurgeDone (deletedCount, _, skipped) ->
                    Expect.equal deletedCount 0 "Should not delete a file that became referenced before commit"
                    Expect.contains skipped "posters/became-referenced.jpg" "Should report the now-referenced path as skipped"
                    Expect.isTrue (File.Exists(Path.Combine(imagesDir, "posters", "became-referenced.jpg"))) "File should remain on disk"
                | PurgeBlocked reason -> failwith reason)

        testCase "purge skips a requested path that's already vanished from disk, without erroring" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                let api = createImageApi db.Factory imagesDir

                match api.purgeOrphanedImages (PurgeSpecific [ "posters/gone.jpg" ]) |> Async.RunSynchronously with
                | PurgeDone (deletedCount, bytesFreed, skipped) ->
                    Expect.equal deletedCount 0 "Nothing to delete"
                    Expect.equal bytesFreed 0L "No bytes freed"
                    Expect.contains skipped "posters/gone.jpg" "Should report the already-vanished path as skipped"
                | PurgeBlocked reason -> failwith reason)

        testCase "purge returns actual deleted count and bytes freed, and stats reflect the smaller total afterward" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/orphan.jpg" 100
                let api = createImageApi db.Factory imagesDir
                let statsBefore = api.getImageCacheStats () |> Async.RunSynchronously

                match api.purgeOrphanedImages PurgeAll |> Async.RunSynchronously with
                | PurgeDone (deletedCount, bytesFreed, _) ->
                    Expect.equal deletedCount 1 "One file deleted"
                    Expect.equal bytesFreed 100L "Bytes freed should match the deleted file's size"
                    let statsAfter = api.getImageCacheStats () |> Async.RunSynchronously
                    Expect.equal statsAfter.TotalFileCount (statsBefore.TotalFileCount - 1) "File count should shrink by 1"
                    Expect.equal statsAfter.TotalBytes (statsBefore.TotalBytes - 100L) "Total bytes should shrink by 100"
                | PurgeBlocked reason -> failwith reason)

        testCase "purge is filesystem-only: total event count is unchanged across a purge" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            EventStore.appendToStream conn (Friends.streamId "marco") -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            let eventCountBefore = EventStore.getTotalEventCount conn

            withTempImagesDir (fun imagesDir ->
                writeImageFile imagesDir "posters/orphan.jpg" 10
                let api = createImageApi db.Factory imagesDir
                api.purgeOrphanedImages PurgeAll |> Async.RunSynchronously |> ignore

                let eventCountAfter = EventStore.getTotalEventCount conn
                Expect.equal eventCountAfter eventCountBefore "Purge must never touch the event store")

        // ── Compensating-event composer (administration-xjmda, ADR-0032) ──

        testCase "getCompensatingEventTypes returns the union of event types across every stream sharing the same bounded-context prefix" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamA = Movies.streamId "movie-a"
            let streamB = Movies.streamId "movie-b"
            EventStore.appendToStream conn streamA -1L [ makeEvent "Movie_added_to_library" "{}" ] |> ignore
            EventStore.appendToStream conn streamB -1L [ makeEvent "Movie_categorized" "{}" ] |> ignore
            let api = createApi db.Factory

            let typesFromA = api.getCompensatingEventTypes streamA |> Async.RunSynchronously
            let typesFromB = api.getCompensatingEventTypes streamB |> Async.RunSynchronously

            Expect.contains typesFromA "Movie_added_to_library" "Should include the type only present on stream A"
            Expect.contains typesFromA "Movie_categorized" "Should include the type only present on sibling stream B"
            Expect.equal typesFromA typesFromB "The union is the same regardless of which stream in the BC is asked"

        testCase "getCompensatingEventTemplate pre-fills from the target stream itself when an instance exists there" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamA = Movies.streamId "movie-a"
            let streamB = Movies.streamId "movie-b"
            EventStore.appendToStream conn streamB -1L [ makeEvent "Personal_rating_set" """{"rating":5}""" ] |> ignore
            EventStore.appendToStream conn streamA -1L [ makeEvent "Personal_rating_set" """{"rating":8}""" ] |> ignore
            let api = createApi db.Factory

            let template = api.getCompensatingEventTemplate streamA "Personal_rating_set" |> Async.RunSynchronously

            Expect.isSome template "Should find a template"
            Expect.equal template.Value.Data """{"rating":8}""" "Should clone the target stream's own instance, not the sibling's"
            Expect.isFalse template.Value.FromOtherStream "Template came from the target stream itself"

        testCase "getCompensatingEventTemplate falls back to the most recent BC-prefix-wide instance when none exists on the target stream" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamA = Movies.streamId "movie-a"
            let streamB = Movies.streamId "movie-b"
            EventStore.appendToStream conn streamB -1L [ makeEvent "Personal_rating_set" """{"rating":5}""" ] |> ignore
            EventStore.appendToStream conn streamA -1L [ makeEvent "Movie_categorized" """{"genres":[]}""" ] |> ignore
            let api = createApi db.Factory

            let template = api.getCompensatingEventTemplate streamA "Personal_rating_set" |> Async.RunSynchronously

            Expect.isSome template "Should fall back to the BC-wide instance"
            Expect.equal template.Value.Data """{"rating":5}""" "Should clone the sibling stream's instance"
            Expect.isTrue template.Value.FromOtherStream "Template came from a sibling stream, not the target"

        testCase "appendCompensatingEvent stores the re-serialized canonical form, not the operator's raw edited bytes" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Games.streamId "some-game"
            let api = createApi db.Factory
            // Games.Serialization.deserialize's decodeGameStatus folds the legacy
            // wire value "Playing" into the InFocus case (Games.fs:361) — a real
            // divergence between what an operator might type and what the
            // canonical re-serialized form is (Games.fs:351's encodeGameStatus).
            let rawEdited = """{"status":"Playing"}"""

            match api.previewCompensatingEvent streamId "Game_status_changed" rawEdited |> Async.RunSynchronously with
            | Error e -> failwith e
            | Ok preview ->
                Expect.equal preview.CanonicalData """{"status":"InFocus"}""" "Preview should show the canonicalized form, not the raw legacy value"

                match api.appendCompensatingEvent streamId "Game_status_changed" rawEdited preview.ExpectedPosition |> Async.RunSynchronously with
                | Error e -> failwith e
                | Ok () ->
                    let stored = EventStore.readStream conn streamId |> List.head
                    Expect.equal stored.Data """{"status":"InFocus"}""" "Stored bytes must be the round-tripped canonical form"
                    Expect.notEqual stored.Data rawEdited "Stored bytes must differ from the operator's raw edited input"

        testCase "appendCompensatingEvent refuses a payload that fails to deserialize: no row inserted, error surfaced" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Friends.streamId "alice"
            let api = createApi db.Factory
            let countBefore = EventStore.getTotalEventCount conn

            // Friend_added's decoder requires a "name" field (Friends.fs:105) —
            // an empty object never deserializes.
            let result = api.appendCompensatingEvent streamId "Friend_added" "{}" -1L |> Async.RunSynchronously

            match result with
            | Ok () -> failwith "Expected refusal for an undeserializable payload"
            | Error _ -> ()
            Expect.equal (EventStore.getTotalEventCount conn) countBefore "No row should have been inserted for a refused payload"

        testCase "appendCompensatingEvent surfaces a concurrency conflict rather than silently overwriting when another append lands between preview and commit" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Friends.streamId "alice"
            EventStore.appendToStream conn streamId -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
            let api = createApi db.Factory

            match api.previewCompensatingEvent streamId "Friend_updated" """{"name":"Alice Corrected"}""" |> Async.RunSynchronously with
            | Error e -> failwith e
            | Ok preview ->
                Expect.equal preview.ExpectedPosition 0L "Expected position should be the stream's current position (one event so far)"

                // Another path appends to the stream between the preview's read
                // and the eventual commit.
                EventStore.appendToStream conn streamId 0L
                    [ Friends.Serialization.toEventData (Friends.Friend_updated { Name = "Alice Elsewhere"; ImageRef = None; CropOffsetX = None; CropOffsetY = None; CropZoom = None }) ]
                |> ignore

                match api.appendCompensatingEvent streamId "Friend_updated" """{"name":"Alice Corrected"}""" preview.ExpectedPosition |> Async.RunSynchronously with
                | Ok () -> failwith "Expected a concurrency conflict, not a silent overwrite"
                | Error msg -> Expect.stringContains msg "position" "Error should describe the concurrency conflict"

                let events = EventStore.readStream conn streamId
                Expect.equal (List.length events) 2 "Only the 'another path' append should have landed; the stale commit must be refused"

        testCase "appendCompensatingEvent runs projection catch-up so the affected BC's projection reflects the new event with no separate rebuild step" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let streamId = Friends.streamId "alice"
            EventStore.appendToStream conn streamId -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Alice"; ImageRef = None }) ] |> ignore
            Projection.runProjection conn FriendProjection.handler
            let api = createApi db.Factory

            match api.previewCompensatingEvent streamId "Friend_updated" """{"name":"Alice Corrected"}""" |> Async.RunSynchronously with
            | Error e -> failwith e
            | Ok preview ->
                api.appendCompensatingEvent streamId "Friend_updated" """{"name":"Alice Corrected"}""" preview.ExpectedPosition
                |> Async.RunSynchronously |> ignore

                use cmd = conn.CreateCommand()
                cmd.CommandText <- "SELECT name FROM friend_list WHERE slug = @slug"
                cmd.Parameters.AddWithValue("@slug", "alice") |> ignore
                let name = cmd.ExecuteScalar() :?> string

                Expect.equal name "Alice Corrected" "friend_list should reflect the corrective event with no manual rebuild call"

        testCase "a composer-appended event is indistinguishable from an organic one except for metadata" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            let organicStream = Friends.streamId "organic"
            let composerStream = Friends.streamId "composer-target"
            EventStore.appendToStream conn organicStream -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Organic"; ImageRef = None }) ] |> ignore
            EventStore.appendToStream conn composerStream -1L [ Friends.Serialization.toEventData (Friends.Friend_added { Name = "Composer Target"; ImageRef = None }) ] |> ignore
            let api = createApi db.Factory

            match api.previewCompensatingEvent composerStream "Friend_updated" """{"name":"Composer Corrected"}""" |> Async.RunSynchronously with
            | Error e -> failwith e
            | Ok preview ->
                api.appendCompensatingEvent composerStream "Friend_updated" """{"name":"Composer Corrected"}""" preview.ExpectedPosition
                |> Async.RunSynchronously |> ignore

                // The organic counterpart: the SAME event type, appended the normal way.
                EventStore.appendToStream conn organicStream 0L
                    [ Friends.Serialization.toEventData (Friends.Friend_updated { Name = "Organic Corrected"; ImageRef = None; CropOffsetX = None; CropOffsetY = None; CropZoom = None }) ]
                |> ignore

                let organicRow = EventStore.readStream conn organicStream |> List.last
                let composerRow = EventStore.readStream conn composerStream |> List.last

                Expect.equal composerRow.EventType organicRow.EventType "Same event type"
                Expect.equal composerRow.StreamPosition organicRow.StreamPosition "Same stream-position sequencing (position 1, the second event on each stream)"
                Expect.equal organicRow.Metadata "{}" "Organic event carries the normal empty metadata"
                Expect.equal composerRow.Metadata "{\"source\":\"admin-console\"}" "Composer event is marked with admin-console provenance"
                Expect.notEqual composerRow.Metadata organicRow.Metadata "Metadata is the only permitted difference between an organic and a composer-appended event"
    ]
