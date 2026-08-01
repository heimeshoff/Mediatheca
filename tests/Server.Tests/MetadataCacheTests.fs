module Mediatheca.Tests.MetadataCacheTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server

/// The metadata cache tier (administration-c3nvp, ADR-0043/ADR-0044):
/// `MetadataCache.initialize`/`seedFromProjections` stand up
/// `game_metadata_cache`/`movie_metadata_cache` as durable, non-projection
/// tables that survive `handler.Drop; handler.Init; replay` — mirroring
/// `JellyfinStore`'s shape and `JellyfinStoreTests`-style fixtures.

/// Registration order mirrors `Composition.fs`/`ProjectionDriftTests.fs`:
/// FriendProjection's Friend_removed case scrubs movie_detail/watch_sessions
/// and needs those tables to already exist.
let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    ContentBlockProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
]

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Braid"
    Year = 2008
    Genres = [ "Puzzle"; "Platformer" ]
    Description = "A puzzle-platformer about time"
    ShortDescription = "Time-bending puzzler"
    WebsiteUrl = Some "https://braid-game.com"
    CoverRef = Some "games/braid-2008-cover.jpg"
    BackdropRef = Some "games/braid-2008-backdrop.jpg"
    RawgId = Some 4200
    RawgRating = Some 4.1
}

let private appendGameAdded (conn: SqliteConnection) (slug: string) =
    let eventData = Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData)
    EventStore.appendToStream conn (Games.streamId slug) -1L [ eventData ] |> ignore

let private pkColumns (conn: SqliteConnection) (table: string) : string list =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sprintf "PRAGMA table_info(%s)" table
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do
        let pk = reader.GetInt32(reader.GetOrdinal("pk"))
        if pk > 0 then yield reader.GetString(reader.GetOrdinal("name")) ]

let private allColumns (conn: SqliteConnection) (table: string) : string list =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sprintf "PRAGMA table_info(%s)" table
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do yield reader.GetString(reader.GetOrdinal("name")) ]

let private tableRowCount (conn: SqliteConnection) (table: string) : int =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sprintf "SELECT COUNT(*) FROM %s" table
    cmd.ExecuteScalar() :?> int64 |> int

[<Tests>]
let tests =
    testList "MetadataCache" [

        testCase "initialize creates both cache tables with their declared primary keys" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn

            Expect.equal (pkColumns conn "game_metadata_cache") [ "game_slug" ] "game_metadata_cache's primary key"
            Expect.equal (pkColumns conn "movie_metadata_cache") [ "movie_slug" ] "movie_metadata_cache's primary key"

        testCase "initialize is idempotent — running it twice changes no schema and throws nothing" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            let gameColsBefore = allColumns conn "game_metadata_cache"
            let movieColsBefore = allColumns conn "movie_metadata_cache"

            MetadataCache.initialize conn

            Expect.equal (allColumns conn "game_metadata_cache") gameColsBefore "game_metadata_cache schema unchanged by a second initialize"
            Expect.equal (allColumns conn "movie_metadata_cache") movieColsBefore "movie_metadata_cache schema unchanged by a second initialize"

        testCase "seedFromProjections seeds game_metadata_cache from game_detail exactly once and sets the marker" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            GameProjection.handler.Init conn
            appendGameAdded conn "braid-2008"
            Projection.runProjection conn GameProjection.handler

            MetadataCache.initialize conn
            Expect.isNone (SettingsStore.getSetting conn "metadata_cache_seeded") "marker absent before seeding"

            MetadataCache.seedFromProjections conn

            Expect.equal (tableRowCount conn "game_metadata_cache") 1 "one row seeded from the one existing game"
            Expect.equal (SettingsStore.getSetting conn "metadata_cache_seeded") (Some "true") "marker set after seeding"

            let seeded =
                conn
                |> Db.newCommand "SELECT description, cover_ref, rawg_rating, fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "braid-2008" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "description", rd.ReadString "cover_ref", rd.ReadDouble "rawg_rating", rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            match seeded with
            | None -> failtest "expected a seeded row for braid-2008"
            | Some (description, coverRef, rawgRating, fetchedAtIsNull) ->
                Expect.equal description sampleGameData.Description "seeded description matches game_detail"
                Expect.equal coverRef (sampleGameData.CoverRef |> Option.get) "seeded cover_ref matches game_detail"
                Expect.equal rawgRating (sampleGameData.RawgRating |> Option.get) "seeded rawg_rating matches game_detail"
                Expect.isTrue fetchedAtIsNull "fetched_at is NULL for a projection-seeded, never-refreshed row"

            // A second game arrives after the marker is already set — a
            // second seedFromProjections call must NOT pick it up (it is a
            // one-time seed, not an ongoing sync).
            appendGameAdded conn "another-game-2010"
            Projection.runProjection conn GameProjection.handler

            MetadataCache.seedFromProjections conn

            Expect.equal (tableRowCount conn "game_metadata_cache") 1 "seedFromProjections is a no-op after the marker is set, even with new game_detail rows"

        testCase "checkProjectionDrift reports zero discrepancies with the cache tables present, and never diffs them" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            for handler in allProjectionHandlers do
                handler.Init conn
            appendGameAdded conn "braid-2008"
            for handler in allProjectionHandlers do
                Projection.runProjection conn handler

            MetadataCache.initialize conn
            MetadataCache.seedFromProjections conn

            let shadow = new SqliteConnection("Data Source=:memory:")
            shadow.Open()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())

            let totalDiscrepancies = results |> List.sumBy (fun p -> List.length p.Discrepancies)
            Expect.equal totalDiscrepancies 0 "cache tables present must not introduce drift discrepancies"

            let discrepancyTables =
                results |> List.collect (fun p -> p.Discrepancies |> List.map (fun d -> d.Table)) |> Set.ofList
            Expect.isFalse (Set.contains "game_metadata_cache" discrepancyTables) "game_metadata_cache must never be diffed"
            Expect.isFalse (Set.contains "movie_metadata_cache" discrepancyTables) "movie_metadata_cache must never be diffed"

        testCase "Projection.rebuildProjection over every handler leaves every cache table's row count unchanged" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            for handler in allProjectionHandlers do
                handler.Init conn
            appendGameAdded conn "braid-2008"
            for handler in allProjectionHandlers do
                Projection.runProjection conn handler

            MetadataCache.initialize conn
            MetadataCache.seedFromProjections conn

            let gameCacheBefore = tableRowCount conn "game_metadata_cache"
            let movieCacheBefore = tableRowCount conn "movie_metadata_cache"

            for handler in allProjectionHandlers do
                Projection.rebuildProjection conn handler

            Expect.equal (tableRowCount conn "game_metadata_cache") gameCacheBefore "game_metadata_cache row count unchanged by rebuildProjection"
            Expect.equal (tableRowCount conn "movie_metadata_cache") movieCacheBefore "movie_metadata_cache row count unchanged by rebuildProjection"
    ]
