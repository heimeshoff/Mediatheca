namespace Mediatheca.Server

open Microsoft.Data.Sqlite
open Donald

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
    /// this seed's series half kept fresh. On any database that already ran
    /// this seed (the marker below is set), this whole function is a no-op
    /// and the column drop never matters. On a database upgrading through
    /// both changes in the same release, `Composition.buildApp` calls this
    /// BEFORE `SeriesProjection.dropDeprecatedColumns`, so the columns still
    /// exist the one time this genuinely runs. The series `INSERT` is
    /// therefore wrapped in its own `try/with` (same "defensive, tolerates a
    /// missing source column" idiom `JellyfinStore.migrateFromProjections`
    /// already uses) rather than folded into one `Db.newCommand` batch with
    /// the game seed: a fresh install's `series_detail` never has these
    /// columns at all (they're gone from `SeriesProjection.createTables`'s
    /// DDL too), and a single failing statement must not also fail the
    /// unrelated game seed.
    let seedFromProjections (conn: SqliteConnection) : unit =
        match SettingsStore.getSetting conn seededMarkerKey with
        | Some _ -> ()
        | None ->
            conn
            |> Db.newCommand
                """
                INSERT OR IGNORE INTO game_metadata_cache
                    (game_slug, description, short_description, website_url, cover_ref, backdrop_ref, rawg_id, rawg_rating, hltb_hours, hltb_main_plus_hours, hltb_completionist_hours, fetched_at)
                SELECT slug, description, short_description, website_url, cover_ref, backdrop_ref, rawg_id, rawg_rating, hltb_hours, hltb_main_plus_hours, hltb_completionist_hours, NULL
                FROM game_detail
                """
            |> Db.exec

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
