module Mediatheca.Tests.GameReleaseDateProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

/// games-ev65k (ADR-0043/ADR-0045): `GameListItem.ReleaseDate`/
/// `GameDetail.ReleaseDate` wiring — cache-only, no override, computed
/// `IsUnreleased` — plus `GameProjection.getUpcomingGames`'s filter/sort and
/// `checkProjectionDrift` staying zero. Mirrors
/// `GameDeckCompatProjectionTests.fs`'s shape.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData (name: string) (year: int): Games.GameAddedData = {
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

[<Tests>]
let tests =
    testList "GameProjection getAll/getBySlug/getUpcomingGames wire ReleaseDate (games-ev65k)" [

        testCase "No cache row — getBySlug/getAll both degrade to the empty/not-unreleased default, never a fabricated value" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" (sampleGameData "Hades" 2020)

            let expected = { Raw = ""; Parsed = None; ComingSoon = false; IsUnreleased = false }
            let detail = GameProjection.getBySlug conn "hades-2020"
            Expect.equal (detail |> Option.map (fun d -> d.ReleaseDate)) (Some expected) "getBySlug honest degradation"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "hades-2020")
            Expect.equal (listItem |> Option.map (fun g -> g.ReleaseDate)) (Some expected) "getAll honest degradation"

        testCase "A coming-soon game reads IsUnreleased=true straight through, on both getBySlug and getAll" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "tenebris-somnia-2026" (sampleGameData "Tenebris Somnia" 2026)
            MetadataCache.upsertGameReleaseDate conn "tenebris-somnia-2026" "October 2026" (Some "2026-10-01") true

            let detail = GameProjection.getBySlug conn "tenebris-somnia-2026"
            match detail |> Option.map (fun d -> d.ReleaseDate) with
            | Some rd ->
                Expect.equal rd.Raw "October 2026" "raw string surfaces verbatim"
                Expect.equal rd.Parsed (Some "2026-10-01") "parsed sortable date surfaces"
                Expect.equal rd.ComingSoon true "coming_soon flag surfaces"
                Expect.equal rd.IsUnreleased true "coming_soon alone marks it unreleased"
            | None -> failtest "Expected the game to be found"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "tenebris-somnia-2026")
            Expect.equal (listItem |> Option.map (fun g -> g.ReleaseDate.IsUnreleased)) (Some true) "getAll wires the same IsUnreleased"

        testCase "A released game with a past parsed date and coming_soon=false reads IsUnreleased=false" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" (sampleGameData "Hades" 2020)
            MetadataCache.upsertGameReleaseDate conn "hades-2020" "17 Sep, 2020" (Some "2020-09-17") false

            let detail = GameProjection.getBySlug conn "hades-2020"
            Expect.equal (detail |> Option.map (fun d -> d.ReleaseDate.IsUnreleased)) (Some false) "past date, not coming_soon — released"

        testCase "A future-dated game (not flagged coming_soon) still reads IsUnreleased=true — parsed-date-in-future is its own signal" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "future-game-2099" (sampleGameData "Future Game" 2099)
            MetadataCache.upsertGameReleaseDate conn "future-game-2099" "1 Jan, 2099" (Some "2099-01-01") false

            let detail = GameProjection.getBySlug conn "future-game-2099"
            Expect.equal (detail |> Option.map (fun d -> d.ReleaseDate.IsUnreleased)) (Some true) "future parsed date alone marks it unreleased"

        testCase "getUpcomingGames returns only unreleased games, soonest parsed date first, TBA/unparseable last" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "released-game-2020" (sampleGameData "Released Game" 2020)
            MetadataCache.upsertGameReleaseDate conn "released-game-2020" "17 Sep, 2020" (Some "2020-09-17") false

            appendGameAdded conn "tenebris-somnia-2026" (sampleGameData "Tenebris Somnia" 2026)
            MetadataCache.upsertGameReleaseDate conn "tenebris-somnia-2026" "October 2026" (Some "2026-10-01") true

            appendGameAdded conn "sooner-game-2026" (sampleGameData "Sooner Game" 2026)
            MetadataCache.upsertGameReleaseDate conn "sooner-game-2026" "1 Feb, 2026" (Some "2026-02-01") true

            appendGameAdded conn "tba-game" (sampleGameData "TBA Game" 0)
            MetadataCache.upsertGameReleaseDate conn "tba-game" "Coming soon" None true

            let upcoming = GameProjection.getUpcomingGames conn |> List.map (fun g -> g.Slug)
            Expect.equal upcoming [ "sooner-game-2026"; "tenebris-somnia-2026"; "tba-game" ] "sorted soonest-first, TBA (no parsed date) last; released game excluded entirely"

        testCase "getUpcomingGames is absent-shaped (empty list) when no unreleased games exist" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" (sampleGameData "Hades" 2020)
            MetadataCache.upsertGameReleaseDate conn "hades-2020" "17 Sep, 2020" (Some "2020-09-17") false

            let upcoming = GameProjection.getUpcomingGames conn
            Expect.isEmpty upcoming "no unreleased games — an empty list, the client renders no section at all for this"

        testCase "getUpcomingGames excludes Dismissed games, mirroring getRecentlyAddedGames" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "tenebris-somnia-2026" (sampleGameData "Tenebris Somnia" 2026)
            MetadataCache.upsertGameReleaseDate conn "tenebris-somnia-2026" "October 2026" (Some "2026-10-01") true
            let sid = Games.streamId "tenebris-somnia-2026"
            EventStore.appendToStream conn sid 0L
                [ Games.Serialization.toEventData (Games.Game_status_changed Dismissed) ] |> ignore
            Projection.runProjection conn GameProjection.handler

            let upcoming = GameProjection.getUpcomingGames conn
            Expect.isEmpty upcoming "dismissed games never appear in Upcoming"

        testCase "getRecentlyAddedGames also wires ReleaseDate" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "tenebris-somnia-2026" (sampleGameData "Tenebris Somnia" 2026)
            MetadataCache.upsertGameReleaseDate conn "tenebris-somnia-2026" "October 2026" (Some "2026-10-01") true

            let recent = GameProjection.getRecentlyAddedGames conn 10 |> List.tryFind (fun g -> g.Slug = "tenebris-somnia-2026")
            Expect.equal (recent |> Option.map (fun g -> g.ReleaseDate.IsUnreleased)) (Some true) "getRecentlyAddedGames reads the cached release date too"

        testCase "checkProjectionDrift stays zero for GameProjection after a release-date write — the columns live in the cache tier only" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" (sampleGameData "Hades" 2020)
            MetadataCache.seedFromProjections conn
            MetadataCache.upsertGameReleaseDate conn "hades-2020" "17 Sep, 2020" (Some "2020-09-17") false

            let shadow = new SqliteConnection("Data Source=:memory:")
            shadow.Open()
            let results = Administration.checkProjectionDrift conn shadow [ GameProjection.handler ] (fun _ -> ())

            let totalDiscrepancies = results |> List.sumBy (fun p -> List.length p.Discrepancies)
            Expect.equal totalDiscrepancies 0 "No projection write path was altered — release-date columns live in game_metadata_cache, never a Projected table"
    ]
