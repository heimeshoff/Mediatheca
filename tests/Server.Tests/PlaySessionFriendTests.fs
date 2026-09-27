module Mediatheca.Tests.PlaySessionFriendTests

/// games-zex36 (ADR-0091): friends attached to an individual play session.
/// `GamesTests.fs`'s `playSessionFriendTests` covers the pure
/// `decide`/`evolve` shapes; this file covers the read side —
/// `PlaySessionProjection`'s new `game_play_session_friend` table (add,
/// remove, move, session-removal, and rebuild consistency) and the
/// `IMediathecaApi.getFriendMedia` wiring that replaces the old hard-coded
/// `Dates = []` for games (`Api.fs`).

open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    NotesProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ FriendProjection.handler; MovieProjection.handler; SeriesProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

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
        (fun () -> async { return Error "not wired in tests" })
        (fun () -> ({ BaseUrl = ""; ApiToken = ""; SelectedPlatformIds = [] } : RomM.RomMConfig))
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        "test-fixtures-do-not-exist/images"
        ""
        allProjectionHandlers

let private sampleGameData: Games.GameAddedData = {
    Name = "Test Game"
    Year = 2024
    Genres = [ "Action" ]
    Description = "A test game"
    ShortDescription = "Test"
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private gameSlug = "test-game-2024"

let private seedGame (conn: SqliteConnection) =
    let eventData = Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData)
    EventStore.appendToStream conn (Games.streamId gameSlug) -1L [ eventData ] |> ignore
    Projection.runProjection conn GameProjection.handler
    Projection.runProjection conn PlaySessionProjection.handler

let private seedFriend (conn: SqliteConnection) (name: string) : string =
    let slug = Slug.friendSlug name
    let eventData = Friends.Serialization.toEventData (Friends.Friend_added { Name = name; ImageRef = None })
    EventStore.appendToStream conn (Friends.streamId slug) -1L [ eventData ] |> ignore
    Projection.runProjection conn FriendProjection.handler
    slug

/// Mirrors `PlaytimeTrackerTests.fs`'s `runCmd` — decide, append, catch up
/// both Games-owned projections.
let private runCmd (conn: SqliteConnection) (slug: string) (cmd: Games.GameCommand) : Result<unit, string> =
    let streamId = Games.streamId slug
    let storedEvents = EventStore.readStream conn streamId
    let events = storedEvents |> List.choose Games.Serialization.fromStoredEvent
    let state = Games.reconstitute events
    let position = EventStore.getStreamPosition conn streamId
    match Games.decide state cmd with
    | Error e -> Error e
    | Ok newEvents ->
        if List.isEmpty newEvents then Ok ()
        else
            let eventDataList = newEvents |> List.map Games.Serialization.toEventData
            match EventStore.appendToStream conn streamId position eventDataList with
            | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
            | EventStore.Success _ ->
                Projection.runProjection conn GameProjection.handler
                Projection.runProjection conn PlaySessionProjection.handler
                Ok ()

let private friendRowsFor (conn: SqliteConnection) (slug: string) (day: string) : string list =
    conn
    |> Db.newCommand "SELECT friend_slug FROM game_play_session_friend WHERE game_slug = @slug AND date = @day ORDER BY friend_slug"
    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
    |> Db.query (fun rd -> rd.ReadString "friend_slug")

let private allFriendRows (conn: SqliteConnection) : (string * string * string) list =
    conn
    |> Db.newCommand "SELECT game_slug, date, friend_slug FROM game_play_session_friend ORDER BY game_slug, date, friend_slug"
    |> Db.query (fun rd -> rd.ReadString "game_slug", rd.ReadString "date", rd.ReadString "friend_slug")

[<Tests>]
let playSessionFriendProjectionTests =
    testList "PlaySessionProjection friends (games-zex36, ADR-0091)" [

        testCase "add, remove, move (onto a day with other friends), and session removal keep game_play_session_friend accurate, and survive a rebuild" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            seedGame conn
            let marco = seedFriend conn "Marco"
            let sarah = seedFriend conn "Sarah"

            match runCmd conn gameSlug (Games.Record_play_session ("2024-06-01", 60)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()

            match runCmd conn gameSlug (Games.Add_friend_to_play_session ("2024-06-01", marco)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            match runCmd conn gameSlug (Games.Add_friend_to_play_session ("2024-06-01", sarah)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            Expect.equal (friendRowsFor conn gameSlug "2024-06-01") [ marco; sarah ] "Both friends should be recorded on 2024-06-01"

            match runCmd conn gameSlug (Games.Remove_friend_from_play_session ("2024-06-01", sarah)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            Expect.equal (friendRowsFor conn gameSlug "2024-06-01") [ marco ] "Only marco should remain on 2024-06-01"

            // A second day, already carrying sarah, that 2024-06-01 will move onto.
            match runCmd conn gameSlug (Games.Record_play_session ("2024-06-02", 30)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            match runCmd conn gameSlug (Games.Add_friend_to_play_session ("2024-06-02", sarah)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()

            match runCmd conn gameSlug (Games.Move_play_session ("2024-06-01", "2024-06-02")) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            Expect.equal (friendRowsFor conn gameSlug "2024-06-01") [] "Origin day must have no rows left"
            Expect.equal (friendRowsFor conn gameSlug "2024-06-02") [ marco; sarah ] "Destination day must union both days' friends"

            match runCmd conn gameSlug (Games.Remove_play_session "2024-06-02") with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            Expect.equal (allFriendRows conn) [] "Removing the only remaining session must leave no friend rows at all"

            // Rebuild from the event log and compare every row.
            let beforeRebuild = allFriendRows conn
            Projection.rebuildProjection conn GameProjection.handler
            Projection.rebuildProjection conn PlaySessionProjection.handler
            let afterRebuild = allFriendRows conn
            Expect.equal afterRebuild beforeRebuild "game_play_session_friend rows must be identical after a full projection rebuild"

        testCase "getForGame/getBySlugAndDay fill Friends from the joined table" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            seedGame conn
            let marco = seedFriend conn "Marco"

            match runCmd conn gameSlug (Games.Record_play_session ("2024-06-01", 60)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()
            match runCmd conn gameSlug (Games.Add_friend_to_play_session ("2024-06-01", marco)) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()

            match PlaySessionProjection.getBySlugAndDay conn gameSlug "2024-06-01" with
            | Some dto -> Expect.equal (dto.Friends |> List.map (fun f -> f.Slug)) [ marco ] "getBySlugAndDay should report the session's friends"
            | None -> failtest "Expected a session row"

            match PlaySessionProjection.getForGame conn gameSlug with
            | [ dto ] -> Expect.equal (dto.Friends |> List.map (fun f -> f.Slug)) [ marco ] "getForGame should report the session's friends"
            | other -> failtest $"Expected exactly one session row, got: {other}"

        testCase "checkProjectionDrift reports no discrepancy for game_play_session_friend" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            seedGame conn
            let marco = seedFriend conn "Marco"
            let sarah = seedFriend conn "Sarah"

            for cmd in [
                Games.Record_play_session ("2024-06-01", 60)
                Games.Add_friend_to_play_session ("2024-06-01", marco)
                Games.Record_play_session ("2024-06-02", 30)
                Games.Add_friend_to_play_session ("2024-06-02", sarah)
                Games.Move_play_session ("2024-06-01", "2024-06-02")
            ] do
                match runCmd conn gameSlug cmd with
                | Error e -> failtest $"Expected Ok, got: {e}"
                | Ok () -> ()

            use shadow = new SqliteConnection("Data Source=:memory:")
            shadow.Open()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())
            let sessionDrift = results |> List.find (fun p -> p.Name = "PlaySessionProjection")
            Expect.equal sessionDrift.Discrepancies [] "PlaySessionProjection (both tables) should report zero discrepancies"
    ]

[<Tests>]
let getFriendMediaGameDatesTests =
    testList "IMediathecaApi.getFriendMedia real game session dates (games-zex36)" [

        testCase "shows real dates for a game shared through sessions, and Dates = [] for a manual-only Played with entry" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            let api = createApi db.Factory

            // Game A: played with marco through two dated sessions.
            let slugA = "session-game-2024"
            let dataA = { sampleGameData with Name = "Session Game" }
            EventStore.appendToStream conn (Games.streamId slugA) -1L [ Games.Serialization.toEventData (Games.Game_added_to_library dataA) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            Projection.runProjection conn PlaySessionProjection.handler

            // Game B: played with marco only via the manual Played-with toggle, no sessions.
            let slugB = "manual-only-game-2024"
            let dataB = { sampleGameData with Name = "Manual Only Game" }
            EventStore.appendToStream conn (Games.streamId slugB) -1L [ Games.Serialization.toEventData (Games.Game_added_to_library dataB) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            Projection.runProjection conn PlaySessionProjection.handler

            let marco = seedFriend conn "Marco"

            for cmd in [
                Games.Record_play_session ("2024-06-01", 60)
                Games.Add_friend_to_play_session ("2024-06-01", marco)
                Games.Record_play_session ("2024-06-03", 45)
                Games.Add_friend_to_play_session ("2024-06-03", marco)
            ] do
                match runCmd conn slugA cmd with
                | Error e -> failtest $"Expected Ok, got: {e}"
                | Ok () -> ()

            match runCmd conn slugB (Games.Add_played_with marco) with
            | Error e -> failtest $"Expected Ok, got: {e}"
            | Ok () -> ()

            let media = api.getFriendMedia marco |> Async.RunSynchronously
            let watchedA = media.Watched |> List.tryFind (fun w -> w.Slug = slugA)
            let watchedB = media.Watched |> List.tryFind (fun w -> w.Slug = slugB)

            match watchedA with
            | Some item -> Expect.equal (item.Dates |> List.sort) [ "2024-06-01"; "2024-06-03" ] "Session Game should show both real session dates"
            | None -> failtest "Expected the session-backed game to appear in Watched"

            match watchedB with
            | Some item -> Expect.equal item.Dates [] "Manual-only Played with must still appear, with Dates = []"
            | None -> failtest "Expected the manual-only Played-with game to appear in Watched"
    ]
