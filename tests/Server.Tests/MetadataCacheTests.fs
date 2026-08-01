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

let private existingTableNames (conn: SqliteConnection) : Set<string> =
    conn
    |> Db.newCommand "SELECT name FROM sqlite_master WHERE type = 'table'"
    |> Db.query (fun rd -> rd.ReadString "name")
    |> Set.ofList

/// series-m7fdk: stands up the *pre-migration* `series_seasons`/
/// `series_episodes` pair under their old names and old schema — simulating
/// an existing database from before this task, the fixture `initialize`'s
/// `ALTER TABLE ... RENAME` is meant to migrate. Mirrors the shape
/// `SeriesProjection.createTables` declared for these tables before the
/// rename (same columns, same PK).
let private createLegacySeriesSeasonsAndEpisodes (conn: SqliteConnection) : unit =
    conn
    |> Db.newCommand
        """
        CREATE TABLE series_seasons (
            series_slug TEXT NOT NULL,
            season_number INTEGER NOT NULL,
            name TEXT NOT NULL DEFAULT '',
            overview TEXT NOT NULL DEFAULT '',
            poster_ref TEXT,
            air_date TEXT,
            episode_count INTEGER NOT NULL DEFAULT 0,
            source TEXT NOT NULL DEFAULT 'tmdb',
            PRIMARY KEY (series_slug, season_number)
        );

        CREATE TABLE series_episodes (
            series_slug TEXT NOT NULL,
            season_number INTEGER NOT NULL,
            episode_number INTEGER NOT NULL,
            name TEXT NOT NULL DEFAULT '',
            overview TEXT NOT NULL DEFAULT '',
            runtime INTEGER,
            air_date TEXT,
            still_ref TEXT,
            tmdb_rating REAL,
            source TEXT NOT NULL DEFAULT 'tmdb',
            PRIMARY KEY (series_slug, season_number, episode_number)
        );
        """
    |> Db.exec

/// series-m7fdk: minimal row insert against the already-renamed
/// `series_episode_cache`, for the view tests below — post-migration
/// fixtures don't need the full legacy schema, just enough columns for
/// `series_next_up`/`series_episode_counts` to have something to compute over.
let private insertCacheEpisode (conn: SqliteConnection) (slug: string) (season: int) (episode: int) : unit =
    conn
    |> Db.newCommand
        "INSERT INTO series_episode_cache (series_slug, season_number, episode_number, name) VALUES (@slug, @season, @episode, 'Ep')"
    |> Db.setParams [ "slug", SqlType.String slug; "season", SqlType.Int32 season; "episode", SqlType.Int32 episode ]
    |> Db.exec

let private insertProgress (conn: SqliteConnection) (slug: string) (rewatchId: string) (season: int) (episode: int) : unit =
    conn
    |> Db.newCommand
        "INSERT INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date) VALUES (@slug, @rewatch, @season, @episode, '2024-01-01')"
    |> Db.setParams [
        "slug", SqlType.String slug
        "rewatch", SqlType.String rewatchId
        "season", SqlType.Int32 season
        "episode", SqlType.Int32 episode
    ]
    |> Db.exec

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

        // series-m7fdk ---------------------------------------------------

        testCase "initialize renames a pre-existing series_seasons/series_episodes pair, preserving row counts and byte-identical source/still_ref values" <| fun _ ->
            let conn = createConnection ()
            createLegacySeriesSeasonsAndEpisodes conn
            conn
            |> Db.newCommand "INSERT INTO series_seasons (series_slug, season_number, name, source) VALUES ('the-wire', 1, 'Season 1', 'jellyfin')"
            |> Db.exec
            conn
            |> Db.newCommand
                "INSERT INTO series_episodes (series_slug, season_number, episode_number, name, still_ref, source) VALUES ('the-wire', 1, 1, 'Ep1', 'stills/tw-s01e01.jpg', 'jellyfin')"
            |> Db.exec
            conn
            |> Db.newCommand
                "INSERT INTO series_episodes (series_slug, season_number, episode_number, name, still_ref, source) VALUES ('the-wire', 1, 2, 'Ep2', NULL, 'tmdb')"
            |> Db.exec

            MetadataCache.initialize conn

            Expect.equal (tableRowCount conn "series_season_cache") 1 "season row count preserved by the rename"
            Expect.equal (tableRowCount conn "series_episode_cache") 2 "episode row count preserved by the rename"

            let ep1 =
                conn
                |> Db.newCommand "SELECT source, still_ref FROM series_episode_cache WHERE series_slug = @slug AND season_number = 1 AND episode_number = 1"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadString "source", rd.ReadString "still_ref")
            Expect.equal ep1 (Some ("jellyfin", "stills/tw-s01e01.jpg")) "episode 1's source and still_ref are byte-identical after the rename"

            let ep2Source =
                conn
                |> Db.newCommand "SELECT source FROM series_episode_cache WHERE series_slug = @slug AND season_number = 1 AND episode_number = 2"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadString "source")
            Expect.equal ep2Source (Some "tmdb") "episode 2's tmdb provenance is preserved after the rename"

            let tableNames = existingTableNames conn
            Expect.isFalse (Set.contains "series_seasons" tableNames) "series_seasons must not exist under its old name after the rename"
            Expect.isFalse (Set.contains "series_episodes" tableNames) "series_episodes must not exist under its old name after the rename"

        testCase "initialize run twice is a no-op the second time — rename already applied, data untouched" <| fun _ ->
            let conn = createConnection ()
            createLegacySeriesSeasonsAndEpisodes conn
            conn
            |> Db.newCommand
                "INSERT INTO series_episodes (series_slug, season_number, episode_number, still_ref, source) VALUES ('the-wire', 1, 1, 'stills/tw-s01e01.jpg', 'jellyfin')"
            |> Db.exec

            MetadataCache.initialize conn
            Expect.equal (tableRowCount conn "series_episode_cache") 1 "row present after the first initialize"

            MetadataCache.initialize conn // second call must not throw and must not touch the data

            Expect.equal (tableRowCount conn "series_episode_cache") 1 "row count unchanged after a second initialize"
            let stillRef =
                conn
                |> Db.newCommand "SELECT still_ref FROM series_episode_cache WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadString "still_ref")
            Expect.equal stillRef (Some "stills/tw-s01e01.jpg") "still_ref unchanged after a second initialize"

        testCase "series_next_up returns exactly one row per series with an unwatched episode, and zero rows for a fully-watched series" <| fun _ ->
            let conn = createConnection ()
            // Mirrors Composition.buildApp's real startup order: MetadataCache.initialize
            // (views bind lazily to tables that don't exist yet — confirmed safe) before
            // Projection.startAllProjections reaches SeriesProjection.createTables.
            MetadataCache.initialize conn
            SeriesProjection.handler.Init conn

            insertCacheEpisode conn "series-a" 1 1
            insertCacheEpisode conn "series-a" 1 2

            insertCacheEpisode conn "series-b" 1 1
            insertProgress conn "series-b" "default" 1 1
            // Watched again under a second, named rewatch session — the view's
            // LEFT JOIN must not fan out across rewatch sessions into extra rows.
            insertProgress conn "series-b" "rewatch-with-alice" 1 1

            let nextUpFor slug =
                conn
                |> Db.newCommand "SELECT season_number, episode_number FROM series_next_up WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.query (fun rd -> rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number")

            Expect.equal (nextUpFor "series-a") [ (1, 1) ] "series-a's next up is exactly one row: its first unwatched episode"
            Expect.isEmpty (nextUpFor "series-b") "series-b is fully watched (across two rewatch sessions) — zero rows, not a fan-out duplicate"

        testCase "series_episode_counts matches a direct COUNT(*) over series_episode_cache for a multi-season fixture" <| fun _ ->
            let conn = createConnection ()
            // Mirrors Composition.buildApp's real startup order: MetadataCache.initialize
            // (views bind lazily to tables that don't exist yet — confirmed safe) before
            // Projection.startAllProjections reaches SeriesProjection.createTables.
            MetadataCache.initialize conn
            SeriesProjection.handler.Init conn

            insertCacheEpisode conn "series-c" 1 1
            insertCacheEpisode conn "series-c" 1 2
            insertCacheEpisode conn "series-c" 2 1

            let directCount =
                conn
                |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_cache WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "series-c" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                |> Option.defaultValue 0

            let viewCounts =
                conn
                |> Db.newCommand "SELECT season_count, episode_count FROM series_episode_counts WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "series-c" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "season_count", rd.ReadInt32 "episode_count")

            Expect.equal directCount 3 "sanity: three episodes were inserted across two seasons"
            Expect.equal viewCounts (Some (2, directCount)) "series_episode_counts.episode_count must match a direct COUNT(*) over series_episode_cache"

        testCase "seedFromProjections seeds game_metadata_cache from game_detail exactly once and sets the marker" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            GameProjection.handler.Init conn
            // series-m7fdk: seedFromProjections now also seeds series_metadata_cache
            // from series_detail in the same batch — series_detail must exist,
            // matching Composition.buildApp's real order (every projection handler
            // is initialized before this function runs), even though this test's
            // own assertions are scoped to the game side only.
            SeriesProjection.handler.Init conn
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

        testCase "seedFromProjections seeds series_metadata_cache from series_detail exactly once" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            GameProjection.handler.Init conn
            SeriesProjection.handler.Init conn
            conn
            |> Db.newCommand
                "INSERT INTO series_detail (slug, name, year, overview, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime) VALUES ('the-wire', 'The Wire', 2002, 'A cop show', 'backdrops/the-wire.jpg', 12345, 9.1, 55)"
            |> Db.exec

            MetadataCache.initialize conn
            MetadataCache.seedFromProjections conn

            Expect.equal (tableRowCount conn "series_metadata_cache") 1 "one row seeded from the one existing series"
            let seeded =
                conn
                |> Db.newCommand "SELECT overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at FROM series_metadata_cache WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "overview", rd.ReadString "backdrop_ref", rd.ReadDouble "tmdb_rating", rd.ReadInt32 "episode_runtime", rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            match seeded with
            | None -> failtest "expected a seeded row for the-wire"
            | Some (overview, backdropRef, tmdbRating, episodeRuntime, fetchedAtIsNull) ->
                Expect.equal overview "A cop show" "seeded overview matches series_detail"
                Expect.equal backdropRef "backdrops/the-wire.jpg" "seeded backdrop_ref matches series_detail"
                Expect.equal tmdbRating 9.1 "seeded tmdb_rating matches series_detail"
                Expect.equal episodeRuntime 55 "seeded episode_runtime matches series_detail"
                Expect.isTrue fetchedAtIsNull "fetched_at is NULL for a projection-seeded, never-refreshed row"

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
