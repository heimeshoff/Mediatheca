module Mediatheca.Tests.DashboardLingerTests

// intelligence-b1nz5: a finished movie / retired game lingers on its All-tab
// dashboard rail for 7 days, marked finished/retired, the same way a
// finished series already lingers on "Next episode"
// (`SeriesProjection.getDashboardSeriesNextUp`). Covers:
//   - `MovieProjection.getAllTabMoviesToWatch` (All tab; lingers)
//   - `MovieProjection.getMoviesToWatch` (Movies tab; stays strict)
//   - `GameProjection`'s `retired_at` column: handler write/clear, the
//     idempotent startup backfill, and `getGamesInFocus` (All tab; lingers)
//   - `Api.getDashboardCardItems` returning the same lingering items as the
//     collapsed `getDashboardAllTab` payload

open System
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private isoDaysAgo (days: int) : string =
    DateTime.UtcNow.AddDays(-(float days)).ToString("yyyy-MM-dd")

// ── Movies ──

let private createMovieConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    conn

let private movieData (name: string) (year: int) (tmdbId: int) : Movies.MovieAddedData = {
    Name = name
    Year = year
    Runtime = Some 100
    Overview = ""
    Genres = []
    PosterRef = None
    BackdropRef = None
    TmdbId = tmdbId
    TmdbRating = None
}

let mutable private nextTmdbId = 900000

/// Every seeded movie needs a distinct `TmdbId` — `MovieProjection` silently
/// skips a `Movie_added_to_library` whose tmdb_id is already held by another
/// slug (its ghost-event guard).
let private addMovie (conn: SqliteConnection) (slug: string) (name: string) (year: int) =
    nextTmdbId <- nextTmdbId + 1
    EventStore.appendToStream conn (Movies.streamId slug) -1L
        [ Movies.Serialization.toEventData (Movies.Movie_added_to_library (movieData name year nextTmdbId)) ] |> ignore
    Projection.runProjection conn MovieProjection.handler

let private setInFocus (conn: SqliteConnection) (slug: string) =
    let pos = EventStore.readStream conn (Movies.streamId slug) |> List.length |> int64
    EventStore.appendToStream conn (Movies.streamId slug) (pos - 1L)
        [ Movies.Serialization.toEventData Movies.Movie_in_focus_set ] |> ignore
    Projection.runProjection conn MovieProjection.handler

let private recordWatch (conn: SqliteConnection) (slug: string) (date: string) =
    let pos = EventStore.readStream conn (Movies.streamId slug) |> List.length |> int64
    let sessionData: Movies.WatchSessionRecordedData = {
        SessionId = Guid.NewGuid().ToString("N")
        Date = date
        Duration = None
        FriendSlugs = []
    }
    EventStore.appendToStream conn (Movies.streamId slug) (pos - 1L)
        [ Movies.Serialization.toEventData (Movies.Watch_session_recorded sessionData) ] |> ignore
    Projection.runProjection conn MovieProjection.handler

let movieTests =
    testList "All-tab Movies to Watch lingers on a recently-watched movie (intelligence-b1nz5)" [

        testCase "a movie watched within the last 7 days appears on the All tab, marked finished, even though it was never In Focus or on Jellyfin" <| fun _ ->
            let conn = createMovieConnection ()
            addMovie conn "just-watched-2020" "Just Watched" 2020
            recordWatch conn "just-watched-2020" (isoDaysAgo 2)

            let allTab = MovieProjection.getAllTabMoviesToWatch conn
            let item = allTab |> List.tryFind (fun m -> m.Slug = "just-watched-2020")
            Expect.isSome item "a movie watched 2 days ago still lingers on the All tab"
            Expect.isTrue item.Value.IsFinished "it is marked finished"

        testCase "a movie whose latest watch session is 8+ days old does not appear on the All tab" <| fun _ ->
            let conn = createMovieConnection ()
            addMovie conn "long-ago-2020" "Long Ago" 2020
            recordWatch conn "long-ago-2020" (isoDaysAgo 8)

            let allTab = MovieProjection.getAllTabMoviesToWatch conn
            Expect.isFalse (allTab |> List.exists (fun m -> m.Slug = "long-ago-2020")) "8 days is past the 7-day linger window"

        testCase "unwatched movies that qualify today (In Focus) still appear on the All tab, unchanged" <| fun _ ->
            let conn = createMovieConnection ()
            addMovie conn "in-focus-2021" "In Focus Movie" 2021
            setInFocus conn "in-focus-2021"

            let allTab = MovieProjection.getAllTabMoviesToWatch conn
            let item = allTab |> List.tryFind (fun m -> m.Slug = "in-focus-2021")
            Expect.isSome item "the unwatched, in-focus movie still appears"
            Expect.isFalse item.Value.IsFinished "it is not a lingering/finished item"
            Expect.isTrue item.Value.InFocus "still carries its InFocus flag"

        testCase "the Movies tab's own Movies to Watch query still excludes every watched movie" <| fun _ ->
            let conn = createMovieConnection ()
            addMovie conn "just-watched-2020" "Just Watched" 2020
            recordWatch conn "just-watched-2020" (isoDaysAgo 1)

            let moviesTab = MovieProjection.getMoviesToWatch conn
            Expect.isFalse (moviesTab |> List.exists (fun m -> m.Slug = "just-watched-2020")) "the Movies tab stays strictly unwatched-only"

        testCase "lingering items sort ahead of the rail's other items, most recently finished first" <| fun _ ->
            let conn = createMovieConnection ()
            addMovie conn "unwatched-2019" "Unwatched" 2019
            setInFocus conn "unwatched-2019"
            addMovie conn "watched-3-days-ago-2020" "Watched 3 Days Ago" 2020
            recordWatch conn "watched-3-days-ago-2020" (isoDaysAgo 3)
            addMovie conn "watched-1-day-ago-2021" "Watched 1 Day Ago" 2021
            recordWatch conn "watched-1-day-ago-2021" (isoDaysAgo 1)

            let slugs = MovieProjection.getAllTabMoviesToWatch conn |> List.map (fun m -> m.Slug)
            Expect.equal slugs [ "watched-1-day-ago-2021"; "watched-3-days-ago-2020"; "unwatched-2019" ]
                "most-recently-finished first, then the rail's existing (unwatched) order"
    ]

// ── Games ──

let private createGameConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    conn

let private sampleGameData (name: string) : Games.GameAddedData = {
    Name = name
    Year = 2020
    Genres = []
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private addGame (conn: SqliteConnection) (slug: string) (name: string) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library (sampleGameData name)) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private changeStatus (conn: SqliteConnection) (slug: string) (status: GameStatus) =
    let pos = EventStore.readStream conn (Games.streamId slug) |> List.length |> int64
    EventStore.appendToStream conn (Games.streamId slug) (pos - 1L)
        [ Games.Serialization.toEventData (Games.Game_status_changed status) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private retiredAtOf (conn: SqliteConnection) (slug: string) : string option =
    conn
    |> Db.newCommand "SELECT retired_at FROM game_list WHERE slug = @slug"
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("retired_at")) then None else Some (rd.ReadString "retired_at"))
    |> Option.flatten

let private latestGameStatusChangedTimestamp (conn: SqliteConnection) (slug: string) : string =
    conn
    |> Db.newCommand "SELECT timestamp FROM events WHERE stream_id = @stream_id AND event_type = 'Game_status_changed' ORDER BY stream_position DESC LIMIT 1"
    |> Db.setParams [ "stream_id", SqlType.String (Games.streamId slug) ]
    |> Db.querySingle (fun rd -> rd.ReadString "timestamp")
    |> Option.get

let gameHandlerTests =
    testList "Game_status_changed maintains game_list.retired_at (intelligence-b1nz5)" [

        testCase "Retired sets retired_at to that event's own timestamp" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "a-game-2020" "A Game"
            changeStatus conn "a-game-2020" Retired

            let expected = latestGameStatusChangedTimestamp conn "a-game-2020"
            Expect.equal (retiredAtOf conn "a-game-2020") (Some expected)
                "retired_at matches the raw events.timestamp text exactly (format parity)"

        testCase "a later status change clears retired_at" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "a-game-2020" "A Game"
            changeStatus conn "a-game-2020" Retired
            Expect.isSome (retiredAtOf conn "a-game-2020") "sanity: retired_at was set"

            changeStatus conn "a-game-2020" InFocus
            Expect.isNone (retiredAtOf conn "a-game-2020") "retired_at is cleared on any other status"
    ]

let gameBackfillTests =
    testList "Startup backfill fills retired_at for already-retired games (intelligence-b1nz5)" [

        testCase "a Retired game whose retired_at is NULL gets the timestamp of its latest Retired/Completed event" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "legacy-retired-2020" "Legacy Retired"
            changeStatus conn "legacy-retired-2020" Retired
            let expected = latestGameStatusChangedTimestamp conn "legacy-retired-2020"

            // Simulate a row that predates this column: NULL it back out.
            conn |> Db.newCommand "UPDATE game_list SET retired_at = NULL WHERE slug = 'legacy-retired-2020'" |> Db.exec
            Expect.isNone (retiredAtOf conn "legacy-retired-2020") "sanity: retired_at is NULL again"

            GameProjection.handler.Init conn

            Expect.equal (retiredAtOf conn "legacy-retired-2020") (Some expected)
                "backfilled from the latest Game_status_changed -> Retired event"

        testCase "retired -> InFocus -> retired again picks the SECOND retirement's timestamp" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "re-retired-2020" "Re-retired"
            changeStatus conn "re-retired-2020" Retired
            changeStatus conn "re-retired-2020" InFocus
            changeStatus conn "re-retired-2020" Retired
            let expected = latestGameStatusChangedTimestamp conn "re-retired-2020"

            conn |> Db.newCommand "UPDATE game_list SET retired_at = NULL WHERE slug = 're-retired-2020'" |> Db.exec
            GameProjection.handler.Init conn

            Expect.equal (retiredAtOf conn "re-retired-2020") (Some expected)
                "MAX(timestamp) over ISO-8601 text naturally picks the later of the two Retired events"

        testCase "non-Retired games are untouched by the backfill" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "in-focus-2020" "In Focus Game"
            changeStatus conn "in-focus-2020" InFocus

            GameProjection.handler.Init conn

            Expect.isNone (retiredAtOf conn "in-focus-2020") "never retired, retired_at stays NULL"

        testCase "a Retired row that already has retired_at is left untouched" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "already-set-2020" "Already Set"
            changeStatus conn "already-set-2020" Retired
            let projectedValue = retiredAtOf conn "already-set-2020"
            Expect.isSome projectedValue "sanity: the handler already set it"

            // Overwrite with a sentinel the backfill query could never produce.
            conn |> Db.newCommand "UPDATE game_list SET retired_at = '1999-01-01T00:00:00.0000000+00:00' WHERE slug = 'already-set-2020'" |> Db.exec

            GameProjection.handler.Init conn

            Expect.equal (retiredAtOf conn "already-set-2020") (Some "1999-01-01T00:00:00.0000000+00:00")
                "WHERE retired_at IS NULL means an already-populated row is never overwritten"

        testCase "running the backfill (via createTables) twice is a no-op" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "a-game-2020" "A Game"
            changeStatus conn "a-game-2020" Retired

            conn |> Db.newCommand "UPDATE game_list SET retired_at = NULL WHERE slug = 'a-game-2020'" |> Db.exec
            GameProjection.handler.Init conn
            let afterFirstRun = retiredAtOf conn "a-game-2020"
            GameProjection.handler.Init conn
            let afterSecondRun = retiredAtOf conn "a-game-2020"

            Expect.equal afterSecondRun afterFirstRun "a repeat run changes nothing once backfilled"
    ]

let gamesInFocusQueryTests =
    testList "getGamesInFocus lingers on a recently-retired game (intelligence-b1nz5)" [

        testCase "a game retired within the last 7 days appears, marked retired; InFocus games still appear" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "in-focus-2020" "In Focus Game"
            changeStatus conn "in-focus-2020" InFocus
            addGame conn "just-retired-2020" "Just Retired"
            changeStatus conn "just-retired-2020" Retired

            let items = GameProjection.getGamesInFocus conn
            let inFocusItem = items |> List.tryFind (fun g -> g.Slug = "in-focus-2020")
            let retiredItem = items |> List.tryFind (fun g -> g.Slug = "just-retired-2020")
            Expect.isSome inFocusItem "InFocus games still appear"
            Expect.isFalse inFocusItem.Value.IsRetired "not marked retired"
            Expect.isSome retiredItem "a game retired moments ago lingers on the rail"
            Expect.isTrue retiredItem.Value.IsRetired "marked retired"

        testCase "a game retired 8+ days ago, or with retired_at NULL, does not appear" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "retired-long-ago-2020" "Retired Long Ago"
            changeStatus conn "retired-long-ago-2020" Retired
            conn
            |> Db.newCommand "UPDATE game_list SET retired_at = @retired_at WHERE slug = 'retired-long-ago-2020'"
            |> Db.setParams [ "retired_at", SqlType.String (isoDaysAgo 8 + "T00:00:00.0000000+00:00") ]
            |> Db.exec

            addGame conn "retired-null-2020" "Retired Null"
            changeStatus conn "retired-null-2020" Retired
            conn |> Db.newCommand "UPDATE game_list SET retired_at = NULL WHERE slug = 'retired-null-2020'" |> Db.exec

            let slugs = GameProjection.getGamesInFocus conn |> List.map (fun g -> g.Slug)
            Expect.isFalse (List.contains "retired-long-ago-2020" slugs) "8 days is past the 7-day linger window"
            Expect.isFalse (List.contains "retired-null-2020" slugs) "a NULL retired_at never qualifies"

        testCase "lingering retired items sort ahead of InFocus items, most recently retired first" <| fun _ ->
            let conn = createGameConnection ()
            addGame conn "in-focus-2020" "In Focus Game"
            changeStatus conn "in-focus-2020" InFocus
            addGame conn "retired-3-days-ago-2020" "Retired 3 Days Ago"
            changeStatus conn "retired-3-days-ago-2020" Retired
            conn
            |> Db.newCommand "UPDATE game_list SET retired_at = @retired_at WHERE slug = 'retired-3-days-ago-2020'"
            |> Db.setParams [ "retired_at", SqlType.String (isoDaysAgo 3 + "T00:00:00.0000000+00:00") ]
            |> Db.exec
            addGame conn "retired-1-day-ago-2020" "Retired 1 Day Ago"
            changeStatus conn "retired-1-day-ago-2020" Retired
            conn
            |> Db.newCommand "UPDATE game_list SET retired_at = @retired_at WHERE slug = 'retired-1-day-ago-2020'"
            |> Db.setParams [ "retired_at", SqlType.String (isoDaysAgo 1 + "T00:00:00.0000000+00:00") ]
            |> Db.exec

            let slugs = GameProjection.getGamesInFocus conn |> List.map (fun g -> g.Slug)
            Expect.equal slugs [ "retired-1-day-ago-2020"; "retired-3-days-ago-2020"; "in-focus-2020" ]
                "most-recently-retired first, then the rail's existing (InFocus) order"
    ]

// ── getDashboardCardItems parity with the collapsed payload ──

open System.Net.Http

let private apiBootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private createApi (factory: unit -> SqliteConnection) : IMediathecaApi =
    Api.create
        factory
        (new HttpClient())
        (Qbittorrent.createHttpClient ())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> ({ UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" } : OpenLibrary.OpenLibraryConfig))
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        LocalCopyRemoval.defaultMountRoots
        "test-fixtures-do-not-exist/images"
        [ ContentBlockProjection.handler; MovieProjection.handler; SeriesProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

let cardItemsParityTests =
    testList "getDashboardCardItems matches the collapsed All-tab payload for the lingering rails (intelligence-b1nz5)" [

        testCase "AllMoviesToWatchQuery returns the same lingering items as getDashboardAllTab.MoviesToWatch" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            addMovie db.Connection "just-watched-2020" "Just Watched" 2020
            recordWatch db.Connection "just-watched-2020" (isoDaysAgo 1)

            let allTab = api.getDashboardAllTab () |> Async.RunSynchronously
            match api.getDashboardCardItems AllMoviesToWatchQuery |> Async.RunSynchronously with
            | MoviesToWatchItems items ->
                Expect.equal (items |> List.map (fun m -> m.Slug, m.IsFinished)) (allTab.MoviesToWatch |> List.map (fun m -> m.Slug, m.IsFinished))
                    "the expanded card sees exactly the same lingering set as the collapsed rail"
            | other -> failtestf "expected MoviesToWatchItems, got %A" other

        testCase "GamesInFocusQuery returns the same lingering items as getDashboardAllTab.GamesInFocus" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            addGame db.Connection "just-retired-2020" "Just Retired"
            changeStatus db.Connection "just-retired-2020" Retired

            let allTab = api.getDashboardAllTab () |> Async.RunSynchronously
            match api.getDashboardCardItems GamesInFocusQuery |> Async.RunSynchronously with
            | GamesInFocusItems items ->
                Expect.equal (items |> List.map (fun g -> g.Slug, g.IsRetired)) (allTab.GamesInFocus |> List.map (fun g -> g.Slug, g.IsRetired))
                    "the expanded card sees exactly the same lingering set as the collapsed rail"
            | other -> failtestf "expected GamesInFocusItems, got %A" other
    ]

[<Tests>]
let tests =
    testList "Dashboard linger (intelligence-b1nz5)" [
        movieTests
        gameHandlerTests
        gameBackfillTests
        gamesInFocusQueryTests
        cardItemsParityTests
    ]
