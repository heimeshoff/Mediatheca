module Mediatheca.Tests.MetadataCacheTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

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

/// A deliberately minimal stand-in for the `CREATE VIEW` DDL
/// `MetadataCache.initialize` declares — NOT a byte-for-byte copy. It keeps
/// the pre-frontier (series-k4zpn) `series_next_up` body, since this
/// fixture's only job is to reproduce the `recoverStranded` view-revalidation
/// hazard (SQLite revalidates every view in the schema during
/// `ALTER TABLE ... RENAME`), which depends on `series_next_up`/
/// `series_episode_counts` existing and referencing `series_episode_cache` —
/// not on the SELECT body matching the production frontier rule.
/// series-d5tpn iteration 2: these views are present on any live database
/// that has booted once — creating them here before simulating the
/// stranded-rename shape is what makes the fixture genuinely reproduce the
/// live incident.
let private createSeriesViews (conn: SqliteConnection) : unit =
    conn
    |> Db.newCommand
        """
        CREATE TABLE IF NOT EXISTS series_episode_progress (
            series_slug TEXT NOT NULL,
            rewatch_id TEXT NOT NULL,
            season_number INTEGER NOT NULL,
            episode_number INTEGER NOT NULL,
            watched_date TEXT,
            PRIMARY KEY (series_slug, rewatch_id, season_number, episode_number)
        );

        CREATE VIEW IF NOT EXISTS series_next_up AS
        SELECT series_slug, season_number, episode_number, name, overview, still_ref, tmdb_rating
        FROM (
            SELECT
                e.series_slug, e.season_number, e.episode_number, e.name, e.overview, e.still_ref, e.tmdb_rating,
                ROW_NUMBER() OVER (PARTITION BY e.series_slug ORDER BY e.season_number, e.episode_number) AS rn
            FROM series_episode_cache e
            LEFT JOIN series_episode_progress p
                ON p.series_slug = e.series_slug
               AND p.season_number = e.season_number
               AND p.episode_number = e.episode_number
            WHERE p.series_slug IS NULL
        )
        WHERE rn = 1;

        CREATE VIEW IF NOT EXISTS series_episode_counts AS
        SELECT
            series_slug,
            COUNT(DISTINCT season_number) AS season_count,
            COUNT(*) AS episode_count
        FROM series_episode_cache
        GROUP BY series_slug;
        """
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

        testCase "initialize recovers a stranded rename — old-named table has rows, new-named table already exists and is empty" <| fun _ ->
            let conn = createConnection ()
            createLegacySeriesSeasonsAndEpisodes conn
            conn
            |> Db.newCommand "INSERT INTO series_seasons (series_slug, season_number, name, source) VALUES ('the-wire', 1, 'Season 1', 'jellyfin')"
            |> Db.exec
            conn
            |> Db.newCommand
                "INSERT INTO series_episodes (series_slug, season_number, episode_number, name, still_ref, source) VALUES ('the-wire', 1, 1, 'Ep1', 'stills/tw-s01e01.jpg', 'jellyfin')"
            |> Db.exec

            // Simulate the exact ordering hazard the iteration-1 verifier
            // note describes against the real database: some out-of-band
            // caller runs `CREATE TABLE IF NOT EXISTS
            // series_episode_cache`/`series_season_cache` before
            // `MetadataCache.initialize` gets a chance to rename the real,
            // populated tables above into place — stranding the real rows
            // under the old names forever, with the rename now failing
            // silently ("target already exists") on every future boot.
            conn
            |> Db.newCommand
                """
                CREATE TABLE series_season_cache (
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

                CREATE TABLE series_episode_cache (
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

            // series-d5tpn iteration 2: the views exist on any live database
            // that has booted once — create them now, before recovery runs,
            // so this fixture genuinely reproduces the incident shape (the
            // iteration-2 verifier note: neither prior fixture did this, so
            // neither exercised the view-revalidation failure the real
            // database hit).
            createSeriesViews conn

            // Sanity: this is the stranded shape before recovery — the
            // rename target already exists and is empty, the real data
            // still sits under the old names.
            Expect.equal (tableRowCount conn "series_season_cache") 0 "sanity: new-named season table starts empty (the stranded shape)"
            Expect.equal (tableRowCount conn "series_episode_cache") 0 "sanity: new-named episode table starts empty (the stranded shape)"
            Expect.equal (tableRowCount conn "series_seasons") 1 "sanity: real season data still sits under the old name"
            Expect.equal (tableRowCount conn "series_episodes") 1 "sanity: real episode data still sits under the old name"

            MetadataCache.initialize conn

            Expect.equal (tableRowCount conn "series_season_cache") 1 "recovery moves the stranded season row under the new name"
            Expect.equal (tableRowCount conn "series_episode_cache") 1 "recovery moves the stranded episode row under the new name"

            let tableNames = existingTableNames conn
            Expect.isFalse (Set.contains "series_seasons" tableNames) "the old-named season table is gone after recovery"
            Expect.isFalse (Set.contains "series_episodes" tableNames) "the old-named episode table is gone after recovery"

            let recoveredEpisode =
                conn
                |> Db.newCommand "SELECT source, still_ref FROM series_episode_cache WHERE series_slug = @slug AND season_number = 1 AND episode_number = 1"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadString "source", rd.ReadString "still_ref")
            Expect.equal recoveredEpisode (Some ("jellyfin", "stills/tw-s01e01.jpg")) "the recovered episode's data is byte-identical to what was stranded"

            // The views must have survived recovery (dropped and recreated
            // around the DROP TABLE/RENAME pair — series-d5tpn iteration 2)
            // and must return rows computed over the recovered data, not
            // empty results left over from a broken schema.
            let counts =
                conn
                |> Db.newCommand "SELECT season_count, episode_count FROM series_episode_counts WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "season_count", rd.ReadInt32 "episode_count")
            Expect.equal counts (Some (1, 1)) "series_episode_counts reflects the recovered episode after view-safe recovery"
            let nextUp =
                conn
                |> Db.newCommand "SELECT season_number, episode_number FROM series_next_up WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number")
            Expect.equal nextUp (Some (1, 1)) "series_next_up returns the recovered episode after view-safe recovery"

            // Recovery must be idempotent — a second initialize is a no-op
            // once the rows are safely under the new names.
            MetadataCache.initialize conn
            Expect.equal (tableRowCount conn "series_episode_cache") 1 "row count unchanged by a second initialize after recovery"

        testCase "initialize recovers a stranded rename even when SeriesProjection.createTables' own fallback wins the race and creates the new-named tables first" <| fun _ ->
            // `SeriesProjection.createTables` still declares its own `CREATE
            // TABLE IF NOT EXISTS series_episode_cache`/`series_season_cache`
            // fallback (kept — see MetadataCache.initialize's doc comment for
            // why removing it was reverted). This test proves the guard holds
            // even in exactly the scenario that produced the live incident:
            // `SeriesProjection.handler.Init` runs BEFORE `MetadataCache.initialize`
            // ever gets a chance to rename the real, populated legacy tables —
            // the reverse of Composition.buildApp's real (correct) order.
            let conn = createConnection ()
            createLegacySeriesSeasonsAndEpisodes conn
            conn
            |> Db.newCommand "INSERT INTO series_seasons (series_slug, season_number, name, source) VALUES ('the-wire', 1, 'Season 1', 'jellyfin')"
            |> Db.exec
            conn
            |> Db.newCommand
                "INSERT INTO series_episodes (series_slug, season_number, episode_number, name, still_ref, source) VALUES ('the-wire', 1, 1, 'Ep1', 'stills/tw-s01e01.jpg', 'jellyfin')"
            |> Db.exec

            // Out-of-order call: SeriesProjection's own fallback claims the
            // new names first, empty.
            SeriesProjection.handler.Init conn
            Expect.equal (tableRowCount conn "series_episode_cache") 0 "sanity: SeriesProjection.handler.Init alone creates the new-named table empty"

            // series-d5tpn iteration 2: on a live database this incident
            // shape always carries the views too (they're created the first
            // time `initialize` ever runs, and a boot that gets this far
            // implies it has). Create them now so this fixture genuinely
            // reproduces the incident shape, not a version of it that never
            // hit the view-revalidation failure.
            createSeriesViews conn

            // MetadataCache.initialize now runs "too late" — the exact
            // ordering violation the iteration-1 incident produced.
            MetadataCache.initialize conn

            Expect.equal (tableRowCount conn "series_season_cache") 1 "recovery moves the stranded season row under the new name even after the out-of-order Init"
            Expect.equal (tableRowCount conn "series_episode_cache") 1 "recovery moves the stranded episode row under the new name even after the out-of-order Init"

            let counts =
                conn
                |> Db.newCommand "SELECT season_count, episode_count FROM series_episode_counts WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "season_count", rd.ReadInt32 "episode_count")
            Expect.equal counts (Some (1, 1)) "series_episode_counts reflects the recovered episode after view-safe recovery"
            let nextUp =
                conn
                |> Db.newCommand "SELECT season_number, episode_number FROM series_next_up WHERE series_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "the-wire" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number")
            Expect.equal nextUp (Some (1, 1)) "series_next_up returns the recovered episode after view-safe recovery"

            let tableNames = existingTableNames conn
            Expect.isFalse (Set.contains "series_seasons" tableNames) "the old-named season table is gone after recovery"
            Expect.isFalse (Set.contains "series_episodes" tableNames) "the old-named episode table is gone after recovery"

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
            GameJournal.initialize conn
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

            // games-v4nqe: game_detail no longer carries description/short_
            // description/website_url/hltb_* at all (dropped from
            // GameProjection.createTables' DDL) — on this fresh test
            // database the seed's second (try/with-wrapped) step has no
            // source column to read, so description stays unseeded here.
            // cover_ref/rawg_rating survive (never dropped from game_detail)
            // and are always seeded by the first, unconditional step.
            let seeded =
                conn
                |> Db.newCommand "SELECT description, cover_ref, rawg_rating, fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "braid-2008" ]
                |> Db.querySingle (fun rd ->
                    (if rd.IsDBNull(rd.GetOrdinal("description")) then None else Some (rd.ReadString "description")),
                    rd.ReadString "cover_ref", rd.ReadDouble "rawg_rating", rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            match seeded with
            | None -> failtest "expected a seeded row for braid-2008"
            | Some (description, coverRef, rawgRating, fetchedAtIsNull) ->
                Expect.equal description None "no game_detail.description column exists on this fresh schema to seed from"
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
            GameJournal.initialize conn
            // series-d5tpn dropped overview/tmdb_rating/episode_runtime from
            // `SeriesProjection.createTables`'s series_detail — a fresh
            // `SeriesProjection.handler.Init` no longer has these columns at
            // all. This test still exercises `seedFromProjections`'s legacy
            // read path (the one the drop's `dropDeprecatedColumns` migration
            // must run AFTER, on an existing database), so it stands up a
            // series_detail shaped like a pre-drop database directly — same
            // "simulate the state a migration is meant to handle" idiom
            // `createLegacySeriesSeasonsAndEpisodes` above already uses for
            // the series_seasons/series_episodes rename.
            conn
            |> Db.newCommand
                """
                CREATE TABLE series_detail (
                    slug TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    year INTEGER NOT NULL,
                    overview TEXT NOT NULL DEFAULT '',
                    backdrop_ref TEXT,
                    tmdb_id INTEGER NOT NULL,
                    tmdb_rating REAL,
                    episode_runtime INTEGER
                );
                INSERT INTO series_detail (slug, name, year, overview, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime)
                VALUES ('the-wire', 'The Wire', 2002, 'A cop show', 'backdrops/the-wire.jpg', 12345, 9.1, 55);
                """
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

        // games-a7dqx (ADR-0053) -------------------------------------------

        testCase "initialize adds the 8 facet/genre/category-id columns to game_metadata_cache, idempotently" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            let colsBefore = allColumns conn "game_metadata_cache" |> Set.ofList
            for col in [ "genres"; "facet_solo"; "facet_coop_couch"; "facet_coop_online"
                         "facet_versus_couch"; "facet_versus_online"; "facet_remote_play_together"
                         "facet_vr"; "steam_category_ids" ] do
                Expect.isTrue (Set.contains col colsBefore) (sprintf "game_metadata_cache should have column %s" col)

            MetadataCache.initialize conn // second call must not throw
            Expect.equal (allColumns conn "game_metadata_cache" |> Set.ofList) colsBefore "Schema unchanged by a second initialize"

        testCase "upsertGameFacets inserts a fresh row when none exists yet" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            let facets : PlayFacets = {
                Solo = true; CoopCouch = false; CoopOnline = true; VersusCouch = false
                VersusOnline = true; RemotePlayTogether = false; Vr = VrSupported
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" facets [ 2; 9; 38; 49; 36; 53 ]

            let row =
                conn
                |> Db.newCommand """
                    SELECT facet_solo, facet_coop_couch, facet_coop_online, facet_versus_couch,
                           facet_versus_online, facet_remote_play_together, facet_vr, steam_category_ids, fetched_at
                    FROM game_metadata_cache WHERE game_slug = @slug
                """
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadInt32 "facet_solo", rd.ReadInt32 "facet_coop_couch", rd.ReadInt32 "facet_coop_online",
                    rd.ReadInt32 "facet_versus_couch", rd.ReadInt32 "facet_versus_online", rd.ReadInt32 "facet_remote_play_together",
                    rd.ReadString "facet_vr", rd.ReadString "steam_category_ids", rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            match row with
            | Some (solo, coopCouch, coopOnline, versusCouch, versusOnline, remotePlay, vr, categoryIdsJson, fetchedAtIsNull) ->
                Expect.equal (solo, coopCouch, coopOnline, versusCouch, versusOnline, remotePlay) (1, 0, 1, 0, 1, 0) "Booleans written as 0/1"
                Expect.equal vr "VrSupported" "Vr written as text"
                Expect.stringContains categoryIdsJson "53" "steam_category_ids carries the raw fetched ids as JSON"
                Expect.isFalse fetchedAtIsNull "A genuine fetch stamps fetched_at, unlike the seed step's deliberate NULL"
            | None -> failtest "expected a row"

        testCase "upsertGameFacets updates only the facet/category-id/fetched_at columns — description/hltb/rawg on an existing row survive untouched" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            GameProjection.handler.Init conn
            GameJournal.initialize conn
            SeriesProjection.handler.Init conn
            appendGameAdded conn "braid-2008"
            Projection.runProjection conn GameProjection.handler
            MetadataCache.initialize conn
            MetadataCache.seedFromProjections conn // seeds cover_ref/rawg_rating for braid-2008 (games-v4nqe: description no longer seedable from game_detail)
            // games-v4nqe: the identity-card writer (its own slice) and the
            // HLTB writer (its own slice) each write independently of the
            // facet writer — exercise all three to prove upsertGameFacets's
            // ON CONFLICT DO UPDATE genuinely scopes to its own column set.
            MetadataCache.upsertGameIdentityCard conn "braid-2008" {
                Description = sampleGameData.Description
                ShortDescription = sampleGameData.ShortDescription
                WebsiteUrl = sampleGameData.WebsiteUrl
            }
            MetadataCache.upsertGameHltbHours conn "braid-2008" (Some 12.5) None None

            let facets : PlayFacets = {
                Solo = true; CoopCouch = false; CoopOnline = false; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "braid-2008" facets [ 2 ]

            let survived =
                conn
                |> Db.newCommand "SELECT description, cover_ref, rawg_rating, hltb_hours FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "braid-2008" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "description", rd.ReadString "cover_ref", rd.ReadDouble "rawg_rating", rd.ReadDouble "hltb_hours")
            match survived with
            | Some (description, coverRef, rawgRating, hltbHours) ->
                Expect.equal description sampleGameData.Description "description survives an upsertGameFacets call that never mentions it — NOT INSERT OR REPLACE"
                Expect.equal coverRef (sampleGameData.CoverRef |> Option.get) "cover_ref survives too"
                Expect.equal rawgRating (sampleGameData.RawgRating |> Option.get) "rawg_rating survives too"
                Expect.equal hltbHours 12.5 "hltb_hours (written by upsertGameHltbHours) survives too"
            | None -> failtest "expected the seeded row to still exist"

        testCase "upsertGameFacets overwrites a previous facet write on a second call (re-derivation after a table fix)" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            let firstPass : PlayFacets = {
                Solo = false; CoopCouch = false; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" firstPass [ 9; 38 ]
            let secondPass = { firstPass with Solo = true; Vr = VrOnly }
            MetadataCache.upsertGameFacets conn "portal-2-2011" secondPass [ 9; 38; 2; 54 ]

            let row =
                conn
                |> Db.newCommand "SELECT facet_solo, facet_vr FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "facet_solo", rd.ReadString "facet_vr")
            Expect.equal row (Some (1, "VrOnly")) "Second upsert replaces the first pass's facet values"

        testCase "findGamesNeedingFacetBackfill returns only steam-linked, never-fetched games — the resumable cursor" <| fun _ ->
            let conn = createConnection ()
            SettingsStore.initialize conn
            GameProjection.handler.Init conn
            GameJournal.initialize conn
            SeriesProjection.handler.Init conn
            appendGameAdded conn "braid-2008"
            appendGameAdded conn "another-game-2010"
            Projection.runProjection conn GameProjection.handler
            MetadataCache.initialize conn
            MetadataCache.seedFromProjections conn

            conn
            |> Db.newCommand "UPDATE game_detail SET steam_app_id = 12345 WHERE slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "braid-2008" ]
            |> Db.exec
            // another-game-2010 has no steam_app_id at all.

            let candidatesBefore = MetadataCache.findGamesNeedingFacetBackfill conn
            Expect.equal candidatesBefore [ ("braid-2008", 12345) ] "Only the steam-linked, seed-only (fetched_at NULL) game is a candidate"

            // Once facets are written, fetched_at is stamped and the game
            // drops out of the cursor on its own.
            let facets : PlayFacets = {
                Solo = true; CoopCouch = false; CoopOnline = false; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "braid-2008" facets [ 2 ]

            Expect.isEmpty (MetadataCache.findGamesNeedingFacetBackfill conn) "The processed game no longer appears in the cursor"
    ]
