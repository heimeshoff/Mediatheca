module Mediatheca.Tests.DashboardCardExpansionTests

open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

/// Dashboard card expansion: every query-backed dashboard card runs its
/// query capped (`Some n`) for the collapsed card and uncapped (`None`) for
/// the expanded one, through `IMediathecaApi.getDashboardCardItems`. Mirrors
/// `GameReleaseDateProjectionTests.fs`'s bootstrap / api-factory shape.

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private noImagesDir = "test-fixtures-do-not-exist/images"

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
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        [ ContentBlockProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

let private sampleGameData (name: string) (year: int) : Games.GameAddedData = {
    Name = name
    Year = year
    Genres = []
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private appendGameAdded (conn: SqliteConnection) (slug: string) (data: Games.GameAddedData) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library data) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private addTwelveGames (conn: SqliteConnection) =
    for i in 1 .. 12 do
        appendGameAdded conn (sprintf "game-%02d-2020" i) (sampleGameData (sprintf "Game %02d" i) 2020)

[<Tests>]
let tests =
    testList "Dashboard card expansion — the row limit lifts to None" [

        testCase "RowLimit.toSql maps None to SQLite's unbounded -1 and Some n to n" <| fun _ ->
            Expect.equal (RowLimit.toSql None) -1 "a negative LIMIT is 'no upper bound' in SQLite"
            Expect.equal (RowLimit.toSql (Some 5)) 5 "a capped query keeps its cap"

        testCase "RowLimit.truncate caps for Some n and keeps everything for None" <| fun _ ->
            Expect.equal (RowLimit.truncate (Some 2) [ 1; 2; 3 ]) [ 1; 2 ] "capped"
            Expect.equal (RowLimit.truncate None [ 1; 2; 3 ]) [ 1; 2; 3 ] "uncapped"

        testCase "getRecentlyAddedGames caps at Some 10 and returns every game for None" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            addTwelveGames db.Connection
            Expect.equal (GameProjection.getRecentlyAddedGames db.Connection (Some 10) |> List.length) 10 "the collapsed card's cap"
            Expect.equal (GameProjection.getRecentlyAddedGames db.Connection None |> List.length) 12 "the expanded card sees all of them"

        testCase "getDashboardCardItems GamesRecentlyAddedQuery returns the full list where getDashboardGamesTab caps at 10" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            addTwelveGames db.Connection

            let tab = api.getDashboardGamesTab () |> Async.RunSynchronously
            Expect.equal (List.length tab.RecentlyAdded) 10 "the Games tab's Recently Added card stays capped"

            match api.getDashboardCardItems GamesRecentlyAddedQuery |> Async.RunSynchronously with
            | GameItems items ->
                Expect.equal (List.length items) 12 "the expanded card gets every recently added game"
                Expect.equal (items |> List.head |> fun g -> g.Slug) "game-12-2020" "same newest-first order as the capped query"
            | other ->
                failtestf "expected GameItems, got %A" other

        testCase "getDashboardCardItems GamesUpcomingQuery answers in the GameItems shape even when nothing is upcoming" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            match api.getDashboardCardItems GamesUpcomingQuery |> Async.RunSynchronously with
            | GameItems items -> Expect.isEmpty items "no unreleased games were added"
            | other -> failtestf "expected GameItems, got %A" other
    ]
