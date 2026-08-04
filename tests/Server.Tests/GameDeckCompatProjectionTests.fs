module Mediatheca.Tests.GameDeckCompatProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// games-b8xnw (ADR-0043/ADR-0045): `GameListItem.DeckCompat`/
/// `GameDetail.DeckCompat` wiring — cache-only, no override, no aggregate
/// involvement at all (unlike `PlayFacets`, there is no merge). Mirrors
/// `GameFacetProjectionTests.fs`'s `dtoFacetWiringTests` shape.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Hades"
    Year = 2020
    Genres = [ "Roguelike" ]
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
    testList "GameProjection getAll/getBySlug wire DeckCompat (games-b8xnw)" [

        testCase "No cache row — getBySlug/getAll both degrade to Unknown, never a fabricated value" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" sampleGameData

            let detail = GameProjection.getBySlug conn "hades-2020"
            Expect.equal (detail |> Option.map (fun d -> d.DeckCompat)) (Some Unknown) "getBySlug honest degradation"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "hades-2020")
            Expect.equal (listItem |> Option.map (fun g -> g.DeckCompat)) (Some Unknown) "getAll honest degradation"

        testCase "Once the backfill writes a verdict, getBySlug/getAll both read it straight through — no merge involved" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" sampleGameData
            MetadataCache.upsertGameDeckCompat conn "hades-2020" Verified

            let detail = GameProjection.getBySlug conn "hades-2020"
            Expect.equal (detail |> Option.map (fun d -> d.DeckCompat)) (Some Verified) "getBySlug reads the cached verdict"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "hades-2020")
            Expect.equal (listItem |> Option.map (fun g -> g.DeckCompat)) (Some Verified) "getAll reads the cached verdict"

        testCase "getRecentlyAddedGames also wires DeckCompat" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" sampleGameData
            MetadataCache.upsertGameDeckCompat conn "hades-2020" Playable

            let recent = GameProjection.getRecentlyAddedGames conn 10 |> List.tryFind (fun g -> g.Slug = "hades-2020")
            Expect.equal (recent |> Option.map (fun g -> g.DeckCompat)) (Some Playable) "getRecentlyAddedGames reads the cached verdict too"

        testCase "checkProjectionDrift stays zero for GameProjection after a Deck-compat write — the column lives in the cache tier only" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "hades-2020" sampleGameData
            MetadataCache.seedFromProjections conn
            MetadataCache.upsertGameDeckCompat conn "hades-2020" Unsupported

            let shadow = new SqliteConnection("Data Source=:memory:")
            shadow.Open()
            let results = Administration.checkProjectionDrift conn shadow [ GameProjection.handler ] (fun _ -> ())

            let totalDiscrepancies = results |> List.sumBy (fun p -> List.length p.Discrepancies)
            Expect.equal totalDiscrepancies 0 "No projection write path was altered — deck_compat lives in game_metadata_cache, never a Projected table"
    ]
