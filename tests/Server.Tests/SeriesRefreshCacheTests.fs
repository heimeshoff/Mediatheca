module Mediatheca.Tests.SeriesRefreshCacheTests

// series-r2xhv: `SeriesRefresh.applyToProjection` cuts over to cache-only
// writes, `Series_refreshed` narrows to fire only on a real airing-status
// transition, and the season/episode cache seed for `Series_added_to_library`
// (and its cleanup for `Series_removed_from_library`) moves from projection
// replay to the command site. These tests exercise the real `refreshOne`
// function end-to-end against a stubbed TMDB HTTP client (never the real
// network) so the "previousStatus comes from the aggregate, not the read
// model" guarantee is proven against actual code, not just eyeballed.

open System
open System.Data
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SeriesProjection.handler.Init conn
    conn

let private mkEpisode (num: int) : Series.EpisodeImportData = {
    EpisodeNumber = num
    Name = $"Episode {num}"
    Overview = ""
    Runtime = None
    AirDate = None
    StillRef = None
    TmdbRating = None
}

let private mkSeason (num: int) (episodeCount: int) : Series.SeasonImportData = {
    SeasonNumber = num
    Name = $"Season {num}"
    Overview = ""
    PosterRef = None
    AirDate = None
    Episodes = [ for i in 1 .. episodeCount -> mkEpisode i ]
}

/// Insert a minimal series_list + series_detail row directly, bypassing the
/// event log — used to prove `applyToProjection` no longer writes these
/// tables regardless of what's already sitting in them.
let private seedListAndDetail (conn: SqliteConnection) (slug: string) (tmdbId: int) (name: string) (status: string) =
    conn
    |> Db.newCommand "INSERT INTO series_list (slug, name, year, status) VALUES (@slug, @name, 2010, @status)"
    |> Db.setParams [ "slug", SqlType.String slug; "name", SqlType.String name; "status", SqlType.String status ]
    |> Db.exec
    conn
    |> Db.newCommand "INSERT INTO series_detail (slug, name, year, tmdb_id, status) VALUES (@slug, @name, 2010, @tmdb_id, @status)"
    |> Db.setParams [ "slug", SqlType.String slug; "name", SqlType.String name; "tmdb_id", SqlType.Int32 tmdbId; "status", SqlType.String status ]
    |> Db.exec

let private readDetailField (conn: SqliteConnection) (slug: string) (field: string) : string option =
    conn
    |> Db.newCommand (sprintf "SELECT %s as v FROM series_detail WHERE slug = @slug" field)
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "v")

let private readListField (conn: SqliteConnection) (slug: string) (field: string) : string option =
    conn
    |> Db.newCommand (sprintf "SELECT %s as v FROM series_list WHERE slug = @slug" field)
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "v")

let private countRows (conn: SqliteConnection) (table: string) (slug: string) : int =
    conn
    |> Db.newCommand (sprintf "SELECT COUNT(*) as c FROM %s WHERE series_slug = @slug" table)
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "c")
    |> Option.defaultValue 0

// ── Stubbed TMDB HTTP client (no real network) ──

type private StubHttpMessageHandler(responseFor: string -> string) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        let json = responseFor (request.RequestUri.ToString())
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(json)
        Task.FromResult<HttpResponseMessage>(response)

let private tmdbConfig : Tmdb.TmdbConfig = { ApiKey = "test-key"; ImageBaseUrl = "https://image.tmdb.org/t/p" }

/// Builds a stub HttpClient that answers the series-details endpoint with
/// `tmdbStatus` (a raw TMDB status string, e.g. "Returning Series") and one
/// season (`seasonNumber`) of `episodeCount` episodes, and answers that
/// season's endpoint with matching episode data.
let private makeStubClient (tmdbId: int) (name: string) (tmdbStatus: string) (seasonNumber: int) (episodeCount: int) : HttpClient =
    let seasonJson =
        let episodesJson =
            [ 1 .. episodeCount ]
            |> List.map (fun e -> sprintf """{"episode_number":%d,"name":"Episode %d","overview":""}""" e e)
            |> String.concat ","
        sprintf """{"season_number":%d,"name":"Season %d","overview":"","episodes":[%s]}""" seasonNumber seasonNumber episodesJson
    let detailsJson =
        sprintf """{"id":%d,"name":"%s","overview":"","genres":[],"status":"%s","number_of_seasons":1,"number_of_episodes":%d,"seasons":[{"season_number":%d,"name":"Season %d","overview":"","episode_count":%d}]}"""
            tmdbId name tmdbStatus episodeCount seasonNumber seasonNumber episodeCount
    let responseFor (url: string) =
        if url.Contains("/season/") then seasonJson else detailsJson
    new HttpClient(new StubHttpMessageHandler(responseFor))

/// Seeds an Active series (via the real event + projection path) and returns
/// its stream id.
let private seedSeries (conn: SqliteConnection) (slug: string) (tmdbId: int) (status: string) : string =
    let seriesData: Series.SeriesAddedData = {
        Name = "Original Name"
        Year = 2010
        Overview = ""
        Genres = []
        Status = status
        PosterRef = None
        BackdropRef = None
        TmdbId = tmdbId
        TmdbRating = None
        EpisodeRuntime = None
        Seasons = []
    }
    let streamId = Series.streamId slug
    EventStore.appendToStream conn streamId -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ]
    |> ignore
    Projection.runProjection conn SeriesProjection.handler
    streamId

[<Tests>]
let applyToProjectionTests =
    testList "SeriesRefresh.applyToProjection cache-only writes" [

        testCase "writes only the cache tier, never series_list/series_detail" <| fun _ ->
            use conn = newConn ()
            seedListAndDetail conn "some-show" 42 "Original Name" "Returning"

            let result: SeriesRefresh.RefreshFetchResult = {
                Name = "Changed Name"
                Year = 2099
                Overview = "changed"
                Genres = [ "Drama" ]
                Status = "Ended"
                PosterRef = Some "posters/x.jpg"
                BackdropRef = None
                TmdbRating = Some 9.0
                EpisodeRuntime = Some 40
                Seasons = [ mkSeason 1 2 ]
                NewEpisodeCount = 2
            }
            SeriesRefresh.applyToProjection conn "some-show" result

            Expect.equal (readDetailField conn "some-show" "name") (Some "Original Name")
                "series_detail must be untouched by a refresh — TMDB name changes land in the cache only"
            Expect.equal (readDetailField conn "some-show" "status") (Some "Returning")
                "series_detail.status must be untouched by applyToProjection — status travels through the narrowed Series_refreshed event instead"
            Expect.equal (readListField conn "some-show" "name") (Some "Original Name")
                "series_list must be untouched by a refresh"

            Expect.equal (countRows conn "series_episode_cache" "some-show") 2 "The 2 refreshed episodes should land in the cache tier"
            Expect.equal (countRows conn "series_season_cache" "some-show") 1 "The 1 refreshed season should land in the cache tier"
    ]

[<Tests>]
let handleEventCacheOwnershipTests =
    testList "SeriesProjection.handleEvent no longer owns the season/episode cache" [

        testCase "Series_added_to_library replay does not seed the cache — that is now a command-time concern" <| fun _ ->
            use conn = newConn ()
            let seriesData: Series.SeriesAddedData = {
                Name = "Some Show"; Year = 2020; Overview = ""; Genres = []
                Status = "Returning"; PosterRef = None; BackdropRef = None
                TmdbId = 1; TmdbRating = None; EpisodeRuntime = None
                Seasons = [ mkSeason 1 2 ]
            }
            let streamId = Series.streamId "some-show"
            EventStore.appendToStream conn streamId -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ]
            |> ignore
            Projection.runProjection conn SeriesProjection.handler

            Expect.equal (countRows conn "series_episode_cache" "some-show") 0
                "Replaying Series_added_to_library must not populate the cache tier — that seed happens imperatively at Api.addSeriesToLibraryImpl instead"
            Expect.equal (countRows conn "series_season_cache" "some-show") 0
                "Same for the season cache"

            // The command-time seed itself works, using the same data:
            SeriesRefresh.upsertSeasonEpisodeCache conn "some-show" seriesData.Seasons
            Expect.equal (countRows conn "series_episode_cache" "some-show") 2 "The extracted command-time helper does seed the cache"

        testCase "Series_removed_from_library replay does not delete the cache — that is now a command-time concern" <| fun _ ->
            use conn = newConn ()
            let _ = seedSeries conn "some-show" 1 "Returning"
            SeriesRefresh.upsertSeasonEpisodeCache conn "some-show" [ mkSeason 1 2 ]
            Expect.equal (countRows conn "series_episode_cache" "some-show") 2 "Precondition: cache seeded"

            let streamId = Series.streamId "some-show"
            let currentPosition = EventStore.getStreamPosition conn streamId
            EventStore.appendToStream conn streamId currentPosition [ Series.Serialization.toEventData Series.Series_removed_from_library ]
            |> ignore
            Projection.runProjection conn SeriesProjection.handler

            Expect.equal (countRows conn "series_episode_cache" "some-show") 2
                "Replaying Series_removed_from_library must not delete the cache tier — that cleanup happens imperatively at Api.removeSeries instead"
    ]

[<Tests>]
let seriesRefreshedProjectionTests =
    testList "SeriesProjection Series_refreshed arm applies the transition" [

        testCase "a real transition updates series_list.status and series_detail.status" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 1 "Returning"
            let currentPosition = EventStore.getStreamPosition conn streamId
            let refreshedEvent = Series.Series_refreshed { PreviousStatus = Some "Returning"; NewStatus = Some "Ended" }
            EventStore.appendToStream conn streamId currentPosition [ Series.Serialization.toEventData refreshedEvent ]
            |> ignore
            Projection.runProjection conn SeriesProjection.handler

            Expect.equal (readListField conn "some-show" "status") (Some "Ended") "series_list.status should reflect the transition"
            Expect.equal (readDetailField conn "some-show" "status") (Some "Ended") "series_detail.status should reflect the transition"

        testCase "a no-transition (null-status) event applies nothing" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 1 "Returning"
            let currentPosition = EventStore.getStreamPosition conn streamId
            let noTransitionEvent = Series.Series_refreshed { PreviousStatus = None; NewStatus = None }
            EventStore.appendToStream conn streamId currentPosition [ Series.Serialization.toEventData noTransitionEvent ]
            |> ignore
            Projection.runProjection conn SeriesProjection.handler

            Expect.equal (readListField conn "some-show" "status") (Some "Returning") "status should be unaffected by a no-transition refresh"
            Expect.equal (readDetailField conn "some-show" "status") (Some "Returning") "status should be unaffected by a no-transition refresh"

        testCase "replaying Series_added_to_library + a null-status refresh + a real-transition refresh yields the transition's newStatus" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 1 "Returning"
            let p1 = EventStore.getStreamPosition conn streamId
            EventStore.appendToStream conn streamId p1 [ Series.Serialization.toEventData (Series.Series_refreshed { PreviousStatus = None; NewStatus = None }) ]
            |> ignore
            let p2 = EventStore.getStreamPosition conn streamId
            EventStore.appendToStream conn streamId p2 [ Series.Serialization.toEventData (Series.Series_refreshed { PreviousStatus = Some "Returning"; NewStatus = Some "Ended" }) ]
            |> ignore
            Projection.runProjection conn SeriesProjection.handler

            Expect.equal (readListField conn "some-show" "status") (Some "Ended") "series_list should hold the transition's newStatus"
            Expect.equal (readDetailField conn "some-show" "status") (Some "Ended") "series_detail should hold the transition's newStatus"
    ]

[<Tests>]
let refreshOneTests =
    testList "SeriesRefresh.refreshOne (stubbed TMDB)" [

        testCase "no status transition: writes cache rows, zero rows change in series_list/series_detail, appends zero events" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 55 "Returning"
            let httpClient = makeStubClient 55 "Changed Name" "Returning Series" 1 2

            let outcome =
                SeriesRefresh.refreshOne conn httpClient tmdbConfig "unused" [ SeriesProjection.handler ] "some-show"
                |> Async.RunSynchronously

            match outcome with
            | Error e -> failtest $"Expected success but got: {e}"
            | Ok data ->
                Expect.equal data.NewEpisodeCount 2 "2 brand-new episodes should be reported"
                Expect.isNone data.PreviousStatus "No transition — PreviousStatus should be None"
                Expect.isNone data.NewStatus "No transition — NewStatus should be None"

            Expect.equal (countRows conn "series_episode_cache" "some-show") 2 "The 2 fetched episodes should land in the cache tier"
            Expect.equal (countRows conn "series_season_cache" "some-show") 1 "The 1 fetched season should land in the cache tier"
            Expect.equal (readDetailField conn "some-show" "name") (Some "Original Name") "series_detail.name must not change from a refresh"
            Expect.equal (readListField conn "some-show" "name") (Some "Original Name") "series_list.name must not change from a refresh"

            let events = EventStore.readStream conn streamId
            Expect.equal (List.length events) 1 "Only the original Series_added_to_library — zero Series_refreshed events appended"

        testCase "a status transition appends exactly one Series_refreshed and updates the projection through the handler" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 56 "Returning"
            let httpClient = makeStubClient 56 "Original Name" "Ended" 1 2

            let outcome =
                SeriesRefresh.refreshOne conn httpClient tmdbConfig "unused" [ SeriesProjection.handler ] "some-show"
                |> Async.RunSynchronously

            match outcome with
            | Error e -> failtest $"Expected success but got: {e}"
            | Ok data ->
                Expect.equal data.PreviousStatus (Some "Returning") "PreviousStatus should be the aggregate's prior status"
                Expect.equal data.NewStatus (Some "Ended") "NewStatus should be the TMDB-mapped new status"

            let events = EventStore.readStream conn streamId
            Expect.equal (List.length events) 2 "Series_added_to_library + exactly one Series_refreshed"
            Expect.equal events.[1].EventType "Series_refreshed" "The appended event should be Series_refreshed"

            Expect.equal (readListField conn "some-show" "status") (Some "Ended") "series_list.status should be updated through the projection handler"
            Expect.equal (readDetailField conn "some-show" "status") (Some "Ended") "series_detail.status should be updated through the projection handler"

        testCase "previousStatus is sourced from the aggregate, not a stale series_detail.status" <| fun _ ->
            use conn = newConn ()
            let streamId = seedSeries conn "some-show" 57 "Returning"
            // Simulate a lagging/stale projection: hand-corrupt series_detail.status
            // directly (no corresponding event), the same drift shape ADR-0043
            // describes. If refreshOne read this column instead of the aggregate,
            // it would report the wrong previousStatus.
            conn
            |> Db.newCommand "UPDATE series_detail SET status = @status WHERE slug = @slug"
            |> Db.setParams [ "status", SqlType.String "Ended"; "slug", SqlType.String "some-show" ]
            |> Db.exec

            let httpClient = makeStubClient 57 "Original Name" "Canceled" 1 1

            let outcome =
                SeriesRefresh.refreshOne conn httpClient tmdbConfig "unused" [ SeriesProjection.handler ] "some-show"
                |> Async.RunSynchronously

            match outcome with
            | Error e -> failtest $"Expected success but got: {e}"
            | Ok data ->
                Expect.equal data.PreviousStatus (Some "Returning")
                    "previousStatus must come from the aggregate's real status (Returning), not the stale series_detail column (Ended)"
                Expect.equal data.NewStatus (Some "Canceled") "NewStatus should be the TMDB-mapped new status"
    ]
