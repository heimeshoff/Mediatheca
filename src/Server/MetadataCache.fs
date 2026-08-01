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
    /// new name, same as any other fresh install. Reversing this order would
    /// let `createTables` claim the new name as an empty table *first*,
    /// making the rename attempted here fail (target already exists) and
    /// stranding the real rows under the old name forever.
    let initialize (conn: SqliteConnection) : unit =
        try
            conn |> Db.newCommand "ALTER TABLE series_episodes RENAME TO series_episode_cache" |> Db.exec
        with _ -> () // Already renamed (or never existed — fresh install)
        try
            conn |> Db.newCommand "ALTER TABLE series_seasons RENAME TO series_season_cache" |> Db.exec
        with _ -> () // Already renamed (or never existed — fresh install)

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
    /// `series_metadata_cache` (series-m7fdk) seeds the same way, from
    /// `series_detail` — its four third-party-sourced flat fields
    /// (`overview`, `backdrop_ref`, `tmdb_rating`, `episode_runtime`), ready
    /// for whichever future task cuts a reader over to it.
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
                FROM game_detail;

                INSERT OR IGNORE INTO series_metadata_cache
                    (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at)
                SELECT slug, overview, backdrop_ref, tmdb_rating, episode_runtime, NULL
                FROM series_detail
                """
            |> Db.exec

            SettingsStore.setSetting conn seededMarkerKey "true"
