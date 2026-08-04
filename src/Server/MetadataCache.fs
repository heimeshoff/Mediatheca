namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net
open Mediatheca.Shared

/// The metadata cache tier (administration-c3nvp; doctrine: ADR-0043
/// event-worthiness, registry: ADR-0044). The durable home for a third
/// party's description of a work — RAWG/HowLongToBeat/TMDB-sourced fields
/// that fail ADR-0043's re-derivability test and so must never live only in
/// a `ProjectionHandler`'s own tables, where a rebuild would silently lose
/// them (the exact 2437-discrepancy defect ADR-0043 documents). Same shape
/// as `ImageStore`/`JellyfinStore`: durable, non-projection, slug-addressed,
/// seeded once from the current projection snapshot, joined back at read
/// time by whichever BC eventually cuts over to it (`series-m7fdk`,
/// `movies-v2gkh`, `games-a7dqx` — none of which this task performs).
///
/// HARD CONSTRAINT (load-bearing): a `ProjectionHandler` must never read this
/// module. Injecting a cache-reader seam into a handler would degrade
/// ADR-0031's "read-only against live holds by construction" to a
/// code-review property, and would let a nightly refresh race the drift
/// check into false positives (this tier has no checkpoint, so
/// `Administration.isAnyProjectionDirty` cannot detect it). `initialize`/
/// `seedFromProjections` are called once from `Composition.buildApp`'s
/// startup path, never from any `ProjectionHandler.Init`/`Drop` — and both
/// tables are registered `Cache` in `Administration.tableRegistry`, never
/// `Projected`, so `checkProjectionDrift`/`Projection.rebuildProjection`
/// never touch them.
module MetadataCache =

    /// One-time schema creation, called from `Composition.buildApp` beside
    /// `JellyfinStore.initialize` (never from any `ProjectionHandler.Init` —
    /// see the module doc comment). `fetched_at` is nullable on every table:
    /// it cannot be `NOT NULL` given the `ALTER TABLE ... ADD COLUMN`
    /// migration idiom used to grow these tables over time (the same idiom
    /// `GameProjection.fs`/`SeriesProjection.fs` already use), and NULL
    /// carries real meaning here — "seeded from the projection, never
    /// actually fetched" — exactly the cohort a first genuine refresh should
    /// prioritize.
    ///
    /// `movie_metadata_cache` ships with only its primary key and
    /// `fetched_at` — four lines of DDL that make the taxonomy honest ahead
    /// of schedule. Movies have no out-of-band metadata writer today
    /// (`movies-v2gkh` is the backlog cutover, gated on a movie-refresh
    /// feature actually existing), so there is nothing yet to give this
    /// table a real column for; it ships empty and unread.
    ///
    /// `game_metadata_cache` gets real typed columns now because
    /// `game_detail` already carries genuinely third-party-sourced fields
    /// today (RAWG/HowLongToBeat descriptions, ratings, hours, artwork) —
    /// the seeding half below (`seedFromProjections`) copies their current
    /// values in, ready for `games-a7dqx`'s cutover.
    ///
    /// `series_episode_cache`/`series_season_cache` (series-m7fdk) are a
    /// different shape of cutover than the other two: they are not a fresh
    /// empty table waiting for a future writer, but the **renamed** former
    /// `series_episodes`/`series_seasons` projection tables — ~4600 rows
    /// (structural episode/season data, `source` provenance, `still_ref`/
    /// `poster_ref`) moved here with zero data movement via `ALTER TABLE
    /// RENAME`, below, before this function's `CREATE TABLE IF NOT EXISTS`
    /// statements run. **Statement order is load-bearing**: the rename must
    /// run first. `SeriesProjection.createTables` still declares `CREATE
    /// TABLE IF NOT EXISTS series_episode_cache`/`series_season_cache` under
    /// their new names (unchanged shape, just renamed) as a fresh-install
    /// fallback, and `Composition.buildApp` calls this `initialize` before
    /// `Projection.startAllProjections` (which reaches that `createTables`) —
    /// so on an existing database the rename claims the new name first and
    /// that `CREATE TABLE IF NOT EXISTS` becomes a no-op; on a fresh database
    /// neither table exists yet, the rename attempt below throws "no such
    /// table" (swallowed), and `createTables` creates them empty under the
    /// new name, same as any other fresh install.
    ///
    /// Reversing this order — letting `createTables` claim the new name as
    /// an empty table *first* — is exactly the hazard this comment used to
    /// only warn about, and (`series-d5tpn`, iteration-1 verifier note) was
    /// realized once for real against the live database: an out-of-band run
    /// did exactly that, the rename below then failed ("target already
    /// exists") and was silently swallowed, and ~4600 real rows were
    /// stranded under the old names. Removing `createTables`'s independent
    /// declaration entirely was considered and reverted (iteration 2): a
    /// large share of the test suite calls `SeriesProjection.handler.Init`
    /// directly, without going through `MetadataCache.initialize` first, and
    /// depends on that fallback existing on its own. Instead, `recoverStranded`
    /// below makes the hazard survivable regardless of which of the two
    /// independent `CREATE TABLE` statements wins the race: it detects the
    /// exact stranded shape (old name non-empty, new name empty) on every
    /// call to this function and repairs it, the same repair the conductor
    /// applied by hand to the live database after iteration 1 — view-safely
    /// (the `series_next_up`/`series_episode_counts` views are dropped and
    /// later recreated around the repair, iteration 2), atomically (one
    /// transaction), and non-fatally (an unexpected failure is logged and
    /// leaves the pre-repair state intact rather than aborting startup).
    let private tableExists (conn: SqliteConnection) (tableName: string) : bool =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name"
        cmd.Parameters.AddWithValue("@name", tableName) |> ignore
        use reader = cmd.ExecuteReader()
        reader.Read()

    let private tableRowCount (conn: SqliteConnection) (tableName: string) : int64 =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT COUNT(*) FROM {tableName}"
        cmd.ExecuteScalar() :?> int64

    /// Stranded-row recovery — the ordering hazard the module doc comment
    /// above warns about, realized once against the real database
    /// (series-d5tpn, iteration-1 verifier note): an out-of-band run let
    /// `SeriesProjection.createTables`'s `CREATE TABLE IF NOT EXISTS` claim
    /// `newTable` as an empty table *before* the rename attempt above got a
    /// chance to run, so that rename failed ("target already exists") and
    /// was silently swallowed, leaving the real rows sitting under
    /// `oldTable` forever with nothing left in the codebase that reads that
    /// name. Detect exactly that shape — `oldTable` exists and is
    /// non-empty, `newTable` exists and is empty — and repair it the same
    /// way the conductor repaired the live database by hand: discard the
    /// empty impostor and rename the real data into its place. A plain
    /// rename (not a row-by-row `INSERT ... SELECT`) sidesteps any risk of
    /// the two tables' column sets having drifted apart, and is idempotent
    /// — once `newTable` has rows, the row-count check below is false and
    /// this is a no-op on every subsequent boot.
    ///
    /// **View-safety (series-d5tpn, iteration-2 verifier note).** The views
    /// `series_next_up`/`series_episode_counts` (created by `initialize`,
    /// below, and present on any live database that has booted once) select
    /// FROM `series_episode_cache`. SQLite revalidates every view in the
    /// schema during `ALTER TABLE ... RENAME` — with the view still in
    /// place, `DROP TABLE newTable` followed by the rename throws `error in
    /// view series_next_up: no such table: main.series_episode_cache`
    /// *after* the drop has already committed, stranding the row-count
    /// check's own repair worse than the original hazard. Both views are
    /// therefore dropped (`IF EXISTS`, so a fresh install or a
    /// `series_season_cache` repair with no episode view yet is unaffected)
    /// before the drop/rename pair; `initialize`'s own `CREATE VIEW IF NOT
    /// EXISTS` block later in this same function recreates them
    /// unconditionally, so they always exist again by the time `initialize`
    /// returns.
    ///
    /// **Atomicity and non-fatality.** All four statements run inside one
    /// transaction, so a mid-repair failure can never leave `newTable`
    /// dropped without `oldTable` successfully renamed into its place — the
    /// transaction rolls back and the pre-repair state (data still sitting
    /// safely under `oldTable`) is exactly what remains. An unexpected
    /// failure is logged to stderr and swallowed rather than propagated:
    /// this repair pass must never be the reason `Composition.buildApp`
    /// fails to boot, since a boot crash after `DROP TABLE` has already run
    /// (the pre-view-safety-fix behavior) is strictly worse than leaving the
    /// stranded rows exactly where they were found.
    let private recoverStranded (conn: SqliteConnection) (oldTable: string) (newTable: string) : unit =
        if tableExists conn oldTable && tableExists conn newTable
           && tableRowCount conn oldTable > 0L && tableRowCount conn newTable = 0L then
            use tx = conn.BeginTransaction()
            let exec (sql: string) =
                use cmd = conn.CreateCommand()
                cmd.Transaction <- tx
                cmd.CommandText <- sql
                cmd.ExecuteNonQuery() |> ignore
            try
                exec "DROP VIEW IF EXISTS series_next_up"
                exec "DROP VIEW IF EXISTS series_episode_counts"
                exec (sprintf "DROP TABLE %s" newTable)
                exec (sprintf "ALTER TABLE %s RENAME TO %s" oldTable newTable)
                tx.Commit()
            with ex ->
                (try tx.Rollback() with _ -> ())
                eprintfn
                    "MetadataCache.recoverStranded: repair of %s -> %s failed (%s) — pre-repair state left intact"
                    oldTable newTable ex.Message

    let initialize (conn: SqliteConnection) : unit =
        try
            conn |> Db.newCommand "ALTER TABLE series_episodes RENAME TO series_episode_cache" |> Db.exec
        with _ -> () // Already renamed (or never existed — fresh install)
        try
            conn |> Db.newCommand "ALTER TABLE series_seasons RENAME TO series_season_cache" |> Db.exec
        with _ -> () // Already renamed (or never existed — fresh install)

        // Recovery pass — see `recoverStranded`'s doc comment. Cheap no-op
        // on every boot where the rename above already succeeded, where
        // neither table has ever existed (fresh install — `recoverStranded`
        // requires both tables to exist, so it is a no-op there too), or
        // where the rename above succeeded and `SeriesProjection.createTables`'s
        // `CREATE TABLE IF NOT EXISTS` fallback for the same two tables
        // (unchanged, still the fresh-install path) correctly became a
        // no-op because the rename already claimed the name.
        recoverStranded conn "series_episodes" "series_episode_cache"
        recoverStranded conn "series_seasons" "series_season_cache"

        conn
        |> Db.newCommand
            """
            CREATE TABLE IF NOT EXISTS game_metadata_cache (
                game_slug                TEXT PRIMARY KEY,
                description              TEXT,
                short_description        TEXT,
                website_url              TEXT,
                cover_ref                TEXT,
                backdrop_ref             TEXT,
                rawg_id                  INTEGER,
                rawg_rating              REAL,
                hltb_hours               REAL,
                hltb_main_plus_hours     REAL,
                hltb_completionist_hours REAL,
                fetched_at               TEXT
            );

            CREATE TABLE IF NOT EXISTS movie_metadata_cache (
                movie_slug TEXT PRIMARY KEY,
                fetched_at TEXT
            );

            CREATE TABLE IF NOT EXISTS series_metadata_cache (
                series_slug     TEXT PRIMARY KEY,
                overview        TEXT,
                backdrop_ref    TEXT,
                tmdb_rating     REAL,
                episode_runtime INTEGER,
                fetched_at      TEXT
            );
            """
        |> Db.exec

        // games-a7dqx (ADR-0053): additive facet/genre/category-id columns on
        // `game_metadata_cache` — idempotent `ALTER TABLE ... ADD COLUMN`,
        // same try/with idiom as every migration in this function and in
        // `GameProjection.createTables`. The six facet booleans store
        // as INTEGER (0/1/NULL); `facet_vr` stores the `VrSupport` DU as
        // text ("NoVr"/"VrSupported"/"VrOnly"); `steam_category_ids` stores
        // the raw fetched ids as a JSON int array, kept alongside the
        // derived facets so a future re-derivation (e.g. after a
        // `deriveFacets` table fix) never needs a second Steam fetch.
        //
        // `genres` was added here (unpopulated) anticipating a games-v4nqe
        // cache cutover for Game genres — ADR-0055 (amending ADR-0043)
        // reverted that plan: no refresh path in this codebase ever
        // re-derives Game genres (RAWG genre search only ever runs at
        // creation time), so genres fails ADR-0043's re-derivability test
        // and stays the `game_list`/`game_detail` identity-card projection
        // column it always was. This column is kept (dropping it needs its
        // own migration and buys nothing) but is permanently unused —
        // nothing reads or writes it.
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN genres TEXT" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_solo INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_coop_couch INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_coop_online INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_versus_couch INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_versus_online INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_remote_play_together INTEGER" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN facet_vr TEXT" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE game_metadata_cache ADD COLUMN steam_category_ids TEXT" |> Db.exec
        with _ -> () // Column already exists

        // `fetched_at` on the renamed tables themselves — they predate this
        // cache tier and never had this column. Same `ALTER TABLE ... ADD
        // COLUMN` idiom as `SeriesProjection.createTables`'s migrations.
        try
            conn |> Db.newCommand "ALTER TABLE series_episode_cache ADD COLUMN fetched_at TEXT" |> Db.exec
        with _ -> () // Column already exists
        try
            conn |> Db.newCommand "ALTER TABLE series_season_cache ADD COLUMN fetched_at TEXT" |> Db.exec
        with _ -> () // Column already exists

        // Views replacing the materialized next-up/count columns that a
        // `ProjectionHandler` could maintain only while it owned these
        // tables directly — it must never read this cache tier now (see the
        // module doc comment). Computed on read: structurally incapable of
        // drifting, and invisible to `PRAGMA table_info` (ADR-0031's shadow
        // diff never sees a view, only base tables).
        conn
        |> Db.newCommand
            """
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

    /// `SettingsStore` marker key gating `seedFromProjections` — a plain,
    /// greppable "has this already run" fact rather than a permanently-
    /// swallowed `try/with` (contrast `JellyfinStore.migrateFromProjections`,
    /// whose defensive `try/with` tolerates being called before its source
    /// tables exist at all). This marker is what makes retirement of the
    /// seed step explicit: once every reader has cut over to the cache and
    /// nothing needs a fresh-projection seed anymore, deleting the call site
    /// and this key is a two-line diff, not an archaeology exercise.
    let private seededMarkerKey = "metadata_cache_seeded"

    /// One-time seed of `game_metadata_cache` from the current `game_detail`
    /// projection snapshot — the `JellyfinStore.migrateFromProjections`
    /// template, gated on the `metadata_cache_seeded` marker instead of a
    /// swallowed exception. Must be called after `game_detail` exists (i.e.
    /// after `Projection.startAllProjections` has run `GameProjection.handler.Init`),
    /// not immediately after `initialize`.
    ///
    /// `movie_metadata_cache` is deliberately never seeded here — it ships
    /// empty and unread until `movies-v2gkh` cuts over.
    ///
    /// `series_metadata_cache` (series-m7fdk) seeded the same way, from
    /// `series_detail`'s four third-party-sourced flat fields (`overview`,
    /// `backdrop_ref`, `tmdb_rating`, `episode_runtime`) — until `series-d5tpn`
    /// dropped three of those four columns (`overview`/`tmdb_rating`/
    /// `episode_runtime`; `backdrop_ref` stayed, ADR-0051) from
    /// `series_detail` entirely, once `series-q8jwc` proved no reader needed
    /// this seed's series half kept fresh. `game_metadata_cache`'s seed lost
    /// its own three source columns the same way (games-v4nqe drops
    /// `description`/`short_description`/`website_url` from `game_detail`;
    /// `genres` stays — ADR-0055 amends ADR-0043 to keep it event-carried).
    /// On any database that already ran this seed (the
    /// marker below is set), this whole function is a no-op and neither
    /// column drop ever matters. On a database upgrading through a seeding
    /// task and its column-drop task in the same release (the common case —
    /// series-m7fdk/series-d5tpn, games-a7dqx/games-v4nqe), `Composition.buildApp`
    /// calls this BEFORE the drop, so the columns still exist the one time
    /// this genuinely runs and needs them. Both the game and series `INSERT`s
    /// are therefore wrapped in their own `try/with` (same "defensive,
    /// tolerates a missing source column" idiom `JellyfinStore.migrateFromProjections`
    /// already uses), each in its own `Db.newCommand` call rather than one
    /// shared batch: a fresh install's `game_detail`/`series_detail` never
    /// have these columns at all (gone from `GameProjection.createTables`'s/
    /// `SeriesProjection.createTables`'s DDL too), and a failing statement
    /// for one media type must not also fail the other's unrelated seed.
    let seedFromProjections (conn: SqliteConnection) : unit =
        match SettingsStore.getSetting conn seededMarkerKey with
        | Some _ -> ()
        | None ->
            // Step 1: columns games-v4nqe does NOT drop from game_detail
            // (cover_ref/backdrop_ref/rawg_id/rawg_rating) — always safe,
            // fresh install or legacy upgrade alike, so this creates the row
            // unconditionally (never wrapped in try/with).
            conn
            |> Db.newCommand
                """
                INSERT OR IGNORE INTO game_metadata_cache
                    (game_slug, cover_ref, backdrop_ref, rawg_id, rawg_rating, fetched_at)
                SELECT slug, cover_ref, backdrop_ref, rawg_id, rawg_rating, NULL
                FROM game_detail
                """
            |> Db.exec

            // Step 2: description/short_description/website_url/hltb_* —
            // games-v4nqe drops these from game_detail. On a database
            // upgrading through both games-a7dqx (this cache tier) and
            // games-v4nqe (the drop) in the same release, Composition.buildApp
            // calls this BEFORE the drop, so the columns still exist here and
            // this UPDATE genuinely seeds them. On a fresh install (this
            // task's own new schema, or any test fixture), game_detail never
            // has these columns at all — the UPDATE fails to prepare and is
            // swallowed, same "defensive, tolerates a missing source column"
            // idiom the series half below already uses. Row-scoped UPDATE
            // (not a second INSERT — the row already exists from step 1).
            try
                conn
                |> Db.newCommand
                    """
                    UPDATE game_metadata_cache
                    SET
                        description = (SELECT gd.description FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug),
                        short_description = (SELECT gd.short_description FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug),
                        website_url = (SELECT gd.website_url FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug),
                        hltb_hours = (SELECT gd.hltb_hours FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug),
                        hltb_main_plus_hours = (SELECT gd.hltb_main_plus_hours FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug),
                        hltb_completionist_hours = (SELECT gd.hltb_completionist_hours FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug)
                    WHERE EXISTS (SELECT 1 FROM game_detail gd WHERE gd.slug = game_metadata_cache.game_slug)
                    """
                |> Db.exec
            with _ -> () // game_detail no longer has description/short_description/website_url/hltb_* (games-v4nqe) — nothing to seed

            try
                conn
                |> Db.newCommand
                    """
                    INSERT OR IGNORE INTO series_metadata_cache
                        (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at)
                    SELECT slug, overview, backdrop_ref, tmdb_rating, episode_runtime, NULL
                    FROM series_detail
                    """
                |> Db.exec
            with _ -> () // series_detail no longer has overview/tmdb_rating/episode_runtime (series-d5tpn) — nothing to seed

            SettingsStore.setSetting conn seededMarkerKey "true"

    /// The ongoing write path for `series_metadata_cache` (series-t3jkv) —
    /// without it, the one-time seed above is the only writer, and every
    /// series added or refreshed after the seed ran would show
    /// `TmdbRating = None`/`Overview = ""`/`EpisodeRuntime = None` forever.
    /// Called imperatively at command time (`Api.addSeriesToLibraryImpl`) and
    /// on every TMDB refresh (`SeriesRefresh.applyToProjection`) — never from
    /// any `ProjectionHandler` (the module doc comment's hard constraint).
    /// `fetched_at` is stamped with the current UTC instant on every genuine
    /// write, distinguishing a real fetch from the seed step's deliberate
    /// NULL ("seeded from the projection, never actually fetched").
    let upsertSeriesMetadata
        (conn: SqliteConnection)
        (slug: string)
        (overview: string)
        (backdropRef: string option)
        (tmdbRating: float option)
        (episodeRuntime: int option)
        : unit =
        conn
        |> Db.newCommand
            """
            INSERT OR REPLACE INTO series_metadata_cache
                (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at)
            VALUES (@series_slug, @overview, @backdrop_ref, @tmdb_rating, @episode_runtime, @fetched_at)
            """
        |> Db.setParams [
            "series_slug", SqlType.String slug
            "overview", SqlType.String overview
            "backdrop_ref", (match backdropRef with Some r -> SqlType.String r | None -> SqlType.Null)
            "tmdb_rating", (match tmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null)
            "episode_runtime", (match episodeRuntime with Some r -> SqlType.Int32 r | None -> SqlType.Null)
            "fetched_at", SqlType.String (System.DateTime.UtcNow.ToString("o"))
        ]
        |> Db.exec

    let private encodeVrSupport (vr: VrSupport) =
        match vr with
        | NoVr -> "NoVr"
        | VrSupported -> "VrSupported"
        | VrOnly -> "VrOnly"

    /// games-a7dqx (ADR-0053): the ongoing write path for
    /// `game_metadata_cache`'s facet columns — the resumable backfill job's
    /// only writer, stamping `fetched_at` on every genuine fetch (the same
    /// "seed vs. real fetch" distinction `upsertSeriesMetadata` uses).
    ///
    /// Deliberately `INSERT ... ON CONFLICT DO UPDATE`, never `INSERT OR
    /// REPLACE`: `game_metadata_cache` also carries description/hltb/rawg
    /// columns this task's read-composition switches now depend on, and a
    /// `REPLACE` would silently null out every column not named here.
    /// `upsertSeriesMetadata` gets away with `INSERT OR REPLACE` because
    /// every one of `series_metadata_cache`'s columns is named on every
    /// call; this function only ever owns the facet + category-id + fetched_at
    /// slice of a row that may already carry unrelated, already-seeded
    /// values in its other columns.
    ///
    /// `genres` is deliberately not a parameter here — ADR-0055 (amending
    /// ADR-0043) decided that write will never exist: genres stays
    /// event-carried on `game_list`/`game_detail`, never cache-sourced, and
    /// the cache's `genres` column is kept but permanently unused.
    let upsertGameFacets
        (conn: SqliteConnection)
        (slug: string)
        (facets: PlayFacets)
        (categoryIds: int list)
        : unit =
        let categoryIdsJson = categoryIds |> List.map Encode.int |> Encode.list |> Encode.toString 0
        conn
        |> Db.newCommand
            """
            INSERT INTO game_metadata_cache
                (game_slug, facet_solo, facet_coop_couch, facet_coop_online, facet_versus_couch, facet_versus_online, facet_remote_play_together, facet_vr, steam_category_ids, fetched_at)
            VALUES (@game_slug, @facet_solo, @facet_coop_couch, @facet_coop_online, @facet_versus_couch, @facet_versus_online, @facet_remote_play_together, @facet_vr, @steam_category_ids, @fetched_at)
            ON CONFLICT(game_slug) DO UPDATE SET
                facet_solo = excluded.facet_solo,
                facet_coop_couch = excluded.facet_coop_couch,
                facet_coop_online = excluded.facet_coop_online,
                facet_versus_couch = excluded.facet_versus_couch,
                facet_versus_online = excluded.facet_versus_online,
                facet_remote_play_together = excluded.facet_remote_play_together,
                facet_vr = excluded.facet_vr,
                steam_category_ids = excluded.steam_category_ids,
                fetched_at = excluded.fetched_at
            """
        |> Db.setParams [
            "game_slug", SqlType.String slug
            "facet_solo", SqlType.Int32 (if facets.Solo then 1 else 0)
            "facet_coop_couch", SqlType.Int32 (if facets.CoopCouch then 1 else 0)
            "facet_coop_online", SqlType.Int32 (if facets.CoopOnline then 1 else 0)
            "facet_versus_couch", SqlType.Int32 (if facets.VersusCouch then 1 else 0)
            "facet_versus_online", SqlType.Int32 (if facets.VersusOnline then 1 else 0)
            "facet_remote_play_together", SqlType.Int32 (if facets.RemotePlayTogether then 1 else 0)
            "facet_vr", SqlType.String (encodeVrSupport facets.Vr)
            "steam_category_ids", SqlType.String categoryIdsJson
            "fetched_at", SqlType.String (System.DateTime.UtcNow.ToString("o"))
        ]
        |> Db.exec

    /// games-v4nqe: the identity-card slice of `game_metadata_cache` —
    /// description/short_description/website_url, the three columns
    /// `game_detail`'s dropped `description`/`short_description`/
    /// `website_url` columns are replaced by. `genres` is deliberately NOT a
    /// field here (ADR-0055, amending ADR-0043): it stays event-carried on
    /// `game_list`/`game_detail`, never cache-sourced — see
    /// `GameProjection.dropDeprecatedColumns`'s doc comment. Kept as its own
    /// record (not reusing `PlayFacets`/DTO types) so `tryGetGameIdentityCard`/
    /// `upsertGameIdentityCard` stay a self-contained read-modify-write pair
    /// callers use to echo untouched fields back unchanged (see
    /// `upsertGameIdentityCard`'s doc comment for why that matters).
    type GameIdentityCard = {
        Description: string
        ShortDescription: string
        WebsiteUrl: string option
    }

    let private emptyIdentityCard : GameIdentityCard =
        { Description = ""; ShortDescription = ""; WebsiteUrl = None }

    /// games-v4nqe: reads the current identity-card slice, defaulting to
    /// "empty" (never a fabricated value) when no cache row exists yet for
    /// this slug — the same honest-degradation stance every other cache read
    /// in this codebase takes (ADR-0048). Callers that only want to change
    /// ONE of the three fields read this first, `{ current with Field = ... }`
    /// the field(s) they actually want to change, and write the whole slice
    /// back via `upsertGameIdentityCard` — never constructing a partial
    /// record from scratch, which would silently blank the fields they
    /// didn't mean to touch.
    let tryGetGameIdentityCard (conn: SqliteConnection) (slug: string) : GameIdentityCard =
        conn
        |> Db.newCommand
            "SELECT description, short_description, website_url FROM game_metadata_cache WHERE game_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            { Description = if rd.IsDBNull(rd.GetOrdinal("description")) then "" else rd.ReadString "description"
              ShortDescription = if rd.IsDBNull(rd.GetOrdinal("short_description")) then "" else rd.ReadString "short_description"
              WebsiteUrl = if rd.IsDBNull(rd.GetOrdinal("website_url")) then None else Some (rd.ReadString "website_url") })
        |> Option.defaultValue emptyIdentityCard

    /// games-v4nqe: the identity-card writer the task's "What" section calls
    /// for — the counterpart to `upsertGameFacets` for the
    /// description/short_description/website_url slice a7dqx deliberately
    /// left unwritten. Same `INSERT ... ON CONFLICT DO UPDATE` slice
    /// discipline as `upsertGameFacets`: never `INSERT OR REPLACE`, which
    /// would silently null the facet/category-id/`fetched_at` columns of an
    /// existing row. Deliberately does NOT touch `fetched_at` at all — that
    /// column is the facet-backfill job's own resume cursor
    /// (`findGamesNeedingFacetBackfill`'s `WHERE fetched_at IS NULL`); an
    /// identity-card write must never be mistaken for "facets were fetched
    /// for this game."
    let upsertGameIdentityCard (conn: SqliteConnection) (slug: string) (card: GameIdentityCard) : unit =
        conn
        |> Db.newCommand
            """
            INSERT INTO game_metadata_cache (game_slug, description, short_description, website_url)
            VALUES (@game_slug, @description, @short_description, @website_url)
            ON CONFLICT(game_slug) DO UPDATE SET
                description = excluded.description,
                short_description = excluded.short_description,
                website_url = excluded.website_url
            """
        |> Db.setParams [
            "game_slug", SqlType.String slug
            "description", SqlType.String card.Description
            "short_description", SqlType.String card.ShortDescription
            "website_url", (match card.WebsiteUrl with Some u -> SqlType.String u | None -> SqlType.Null)
        ]
        |> Db.exec

    /// games-v4nqe: the HLTB-hours writer for `fetchHltbData` (Api.fs) — the
    /// counterpart to `upsertGameFacets`/`upsertGameIdentityCard` for the
    /// three `hltb_*` columns. Same slice discipline: `ON CONFLICT DO
    /// UPDATE` names only these three columns, never touching `fetched_at`
    /// (the facet-backfill cursor) or any other column on the row.
    let upsertGameHltbHours
        (conn: SqliteConnection)
        (slug: string)
        (hours: float option)
        (mainPlusHours: float option)
        (completionistHours: float option)
        : unit =
        conn
        |> Db.newCommand
            """
            INSERT INTO game_metadata_cache (game_slug, hltb_hours, hltb_main_plus_hours, hltb_completionist_hours)
            VALUES (@game_slug, @hltb_hours, @hltb_main_plus_hours, @hltb_completionist_hours)
            ON CONFLICT(game_slug) DO UPDATE SET
                hltb_hours = excluded.hltb_hours,
                hltb_main_plus_hours = excluded.hltb_main_plus_hours,
                hltb_completionist_hours = excluded.hltb_completionist_hours
            """
        |> Db.setParams [
            "game_slug", SqlType.String slug
            "hltb_hours", (match hours with Some h -> SqlType.Double h | None -> SqlType.Null)
            "hltb_main_plus_hours", (match mainPlusHours with Some h -> SqlType.Double h | None -> SqlType.Null)
            "hltb_completionist_hours", (match completionistHours with Some h -> SqlType.Double h | None -> SqlType.Null)
        ]
        |> Db.exec

    /// games-a7dqx: the resumable backfill job's cursor — every game whose
    /// cache row is still seed-only (`fetched_at IS NULL`, ADR-0045's
    /// "seeded from the projection, never actually fetched" cohort) and has
    /// a Steam app id to fetch facets for. The `WHERE` clause IS the resume
    /// cursor: a row the job successfully processes gets `fetched_at`
    /// stamped by `upsertGameFacets`, so it drops out of this query on the
    /// next run — no separate cursor table needed. Games created after this
    /// task's deploy with no `game_metadata_cache` row at all (the
    /// creation-path cache-write is games-v4nqe's job) are out of scope for
    /// this cursor by construction — an `INNER JOIN`, not a `LEFT JOIN`.
    let findGamesNeedingFacetBackfill (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand
            """
            SELECT mc.game_slug, gd.steam_app_id
            FROM game_metadata_cache mc
            JOIN game_detail gd ON gd.slug = mc.game_slug
            WHERE mc.fetched_at IS NULL AND gd.steam_app_id IS NOT NULL
            """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "game_slug", rd.ReadInt32 "steam_app_id")
