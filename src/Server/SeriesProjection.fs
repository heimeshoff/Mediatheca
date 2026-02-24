namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net

module SeriesProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS series_list (
                slug TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                year INTEGER NOT NULL,
                poster_ref TEXT,
                genres TEXT NOT NULL DEFAULT '[]',
                tmdb_rating REAL,
                status TEXT NOT NULL DEFAULT 'Unknown',
                season_count INTEGER NOT NULL DEFAULT 0,
                episode_count INTEGER NOT NULL DEFAULT 0,
                watched_episode_count INTEGER NOT NULL DEFAULT 0,
                next_up_season INTEGER,
                next_up_episode INTEGER,
                next_up_title TEXT,
                abandoned INTEGER NOT NULL DEFAULT 0,
                in_focus INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS series_detail (
                slug TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                year INTEGER NOT NULL,
                overview TEXT NOT NULL DEFAULT '',
                genres TEXT NOT NULL DEFAULT '[]',
                poster_ref TEXT,
                backdrop_ref TEXT,
                tmdb_id INTEGER NOT NULL,
                tmdb_rating REAL,
                episode_runtime INTEGER,
                status TEXT NOT NULL DEFAULT 'Unknown',
                personal_rating INTEGER,
                recommended_by TEXT NOT NULL DEFAULT '[]',
                want_to_watch_with TEXT NOT NULL DEFAULT '[]',
                abandoned INTEGER NOT NULL DEFAULT 0,
                in_focus INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS series_seasons (
                series_slug TEXT NOT NULL,
                season_number INTEGER NOT NULL,
                name TEXT NOT NULL DEFAULT '',
                overview TEXT NOT NULL DEFAULT '',
                poster_ref TEXT,
                air_date TEXT,
                episode_count INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (series_slug, season_number)
            );

            CREATE TABLE IF NOT EXISTS series_episodes (
                series_slug TEXT NOT NULL,
                season_number INTEGER NOT NULL,
                episode_number INTEGER NOT NULL,
                name TEXT NOT NULL DEFAULT '',
                overview TEXT NOT NULL DEFAULT '',
                runtime INTEGER,
                air_date TEXT,
                still_ref TEXT,
                tmdb_rating REAL,
                PRIMARY KEY (series_slug, season_number, episode_number)
            );

            CREATE TABLE IF NOT EXISTS series_rewatch_sessions (
                rewatch_id TEXT NOT NULL,
                series_slug TEXT NOT NULL,
                name TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                friends TEXT NOT NULL DEFAULT '[]',
                PRIMARY KEY (rewatch_id, series_slug)
            );

            CREATE TABLE IF NOT EXISTS series_episode_progress (
                series_slug TEXT NOT NULL,
                rewatch_id TEXT NOT NULL,
                season_number INTEGER NOT NULL,
                episode_number INTEGER NOT NULL,
                watched_date TEXT,
                PRIMARY KEY (series_slug, rewatch_id, season_number, episode_number)
            );
        """
        |> Db.exec

        // Migration: add in_focus column if not present (existing databases)
        try
            conn
            |> Db.newCommand "ALTER TABLE series_list ADD COLUMN in_focus INTEGER NOT NULL DEFAULT 0"
            |> Db.exec
        with _ -> () // Column already exists
        try
            conn
            |> Db.newCommand "ALTER TABLE series_detail ADD COLUMN in_focus INTEGER NOT NULL DEFAULT 0"
            |> Db.exec
        with _ -> () // Column already exists

        // Migration: add jellyfin_id column for Jellyfin play links
        try
            conn
            |> Db.newCommand "ALTER TABLE series_detail ADD COLUMN jellyfin_id TEXT"
            |> Db.exec
        with _ -> () // Column already exists

        // Create series_episode_jellyfin table for episode-level Jellyfin IDs
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS series_episode_jellyfin (
                series_slug TEXT NOT NULL,
                season_number INTEGER NOT NULL,
                episode_number INTEGER NOT NULL,
                jellyfin_id TEXT NOT NULL,
                PRIMARY KEY (series_slug, season_number, episode_number)
            )
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS series_list;
            DROP TABLE IF EXISTS series_detail;
            DROP TABLE IF EXISTS series_seasons;
            DROP TABLE IF EXISTS series_episodes;
            DROP TABLE IF EXISTS series_rewatch_sessions;
            DROP TABLE IF EXISTS series_episode_progress;
            DROP TABLE IF EXISTS series_episode_jellyfin;
        """
        |> Db.exec

    let private recalculateProgress (conn: SqliteConnection) (slug: string) : unit =
        // Count distinct (season_number, episode_number) across ALL rewatch sessions
        let watchedCount =
            conn
            |> Db.newCommand """
                SELECT COUNT(*) as cnt FROM (
                    SELECT DISTINCT season_number, episode_number
                    FROM series_episode_progress
                    WHERE series_slug = @slug
                )
            """
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0

        // Find first unwatched episode (next up) - ordered by season then episode
        let nextUp =
            conn
            |> Db.newCommand """
                SELECT e.season_number, e.episode_number, e.name
                FROM series_episodes e
                WHERE e.series_slug = @slug
                AND NOT EXISTS (
                    SELECT 1 FROM series_episode_progress p
                    WHERE p.series_slug = @slug
                    AND p.season_number = e.season_number
                    AND p.episode_number = e.episode_number
                )
                ORDER BY e.season_number, e.episode_number
                LIMIT 1
            """
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: IDataReader) ->
                rd.ReadInt32 "season_number",
                rd.ReadInt32 "episode_number",
                rd.ReadString "name"
            )

        match nextUp with
        | Some (seasonNum, epNum, epName) ->
            conn
            |> Db.newCommand """
                UPDATE series_list
                SET watched_episode_count = @watched_count,
                    next_up_season = @next_season,
                    next_up_episode = @next_episode,
                    next_up_title = @next_title
                WHERE slug = @slug
            """
            |> Db.setParams [
                "slug", SqlType.String slug
                "watched_count", SqlType.Int32 watchedCount
                "next_season", SqlType.Int32 seasonNum
                "next_episode", SqlType.Int32 epNum
                "next_title", SqlType.String epName
            ]
            |> Db.exec
        | None ->
            conn
            |> Db.newCommand """
                UPDATE series_list
                SET watched_episode_count = @watched_count,
                    next_up_season = NULL,
                    next_up_episode = NULL,
                    next_up_title = NULL
                WHERE slug = @slug
            """
            |> Db.setParams [
                "slug", SqlType.String slug
                "watched_count", SqlType.Int32 watchedCount
            ]
            |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Series-")) then ()
        else
            let slug = event.StreamId.Substring(7) // Remove "Series-" prefix
            match Series.Serialization.fromStoredEvent event with
            | None -> ()
            | Some seriesEvent ->
                match seriesEvent with
                | Series.Series_added_to_library data ->
                    let genresJson = data.Genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    let totalEpisodes =
                        data.Seasons |> List.sumBy (fun s -> s.Episodes.Length)
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO series_list (slug, name, year, poster_ref, genres, tmdb_rating, status, season_count, episode_count, watched_episode_count)
                        VALUES (@slug, @name, @year, @poster_ref, @genres, @tmdb_rating, @status, @season_count, @episode_count, 0)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "poster_ref", match data.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "genres", SqlType.String genresJson
                        "tmdb_rating", match data.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                        "status", SqlType.String data.Status
                        "season_count", SqlType.Int32 (List.length data.Seasons)
                        "episode_count", SqlType.Int32 totalEpisodes
                    ]
                    |> Db.exec

                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO series_detail (slug, name, year, overview, genres, poster_ref, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime, status, recommended_by, want_to_watch_with)
                        VALUES (@slug, @name, @year, @overview, @genres, @poster_ref, @backdrop_ref, @tmdb_id, @tmdb_rating, @episode_runtime, @status, '[]', '[]')
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "overview", SqlType.String data.Overview
                        "genres", SqlType.String genresJson
                        "poster_ref", match data.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "backdrop_ref", match data.BackdropRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "tmdb_id", SqlType.Int32 data.TmdbId
                        "tmdb_rating", match data.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                        "episode_runtime", match data.EpisodeRuntime with Some r -> SqlType.Int32 r | None -> SqlType.Null
                        "status", SqlType.String data.Status
                    ]
                    |> Db.exec

                    // Insert all seasons and episodes
                    for season in data.Seasons do
                        conn
                        |> Db.newCommand """
                            INSERT OR REPLACE INTO series_seasons (series_slug, season_number, name, overview, poster_ref, air_date, episode_count)
                            VALUES (@series_slug, @season_number, @name, @overview, @poster_ref, @air_date, @episode_count)
                        """
                        |> Db.setParams [
                            "series_slug", SqlType.String slug
                            "season_number", SqlType.Int32 season.SeasonNumber
                            "name", SqlType.String season.Name
                            "overview", SqlType.String season.Overview
                            "poster_ref", match season.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                            "air_date", match season.AirDate with Some d -> SqlType.String d | None -> SqlType.Null
                            "episode_count", SqlType.Int32 (List.length season.Episodes)
                        ]
                        |> Db.exec

                        for episode in season.Episodes do
                            conn
                            |> Db.newCommand """
                                INSERT OR REPLACE INTO series_episodes (series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating)
                                VALUES (@series_slug, @season_number, @episode_number, @name, @overview, @runtime, @air_date, @still_ref, @tmdb_rating)
                            """
                            |> Db.setParams [
                                "series_slug", SqlType.String slug
                                "season_number", SqlType.Int32 season.SeasonNumber
                                "episode_number", SqlType.Int32 episode.EpisodeNumber
                                "name", SqlType.String episode.Name
                                "overview", SqlType.String episode.Overview
                                "runtime", match episode.Runtime with Some r -> SqlType.Int32 r | None -> SqlType.Null
                                "air_date", match episode.AirDate with Some d -> SqlType.String d | None -> SqlType.Null
                                "still_ref", match episode.StillRef with Some r -> SqlType.String r | None -> SqlType.Null
                                "tmdb_rating", match episode.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                            ]
                            |> Db.exec

                    // Create default rewatch session
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO series_rewatch_sessions (rewatch_id, series_slug, name, is_default, friends)
                        VALUES ('default', @series_slug, NULL, 1, '[]')
                    """
                    |> Db.setParams [ "series_slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Series_removed_from_library ->
                    conn
                    |> Db.newCommand "DELETE FROM series_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_detail WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_seasons WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_episodes WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_rewatch_sessions WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_episode_progress WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Series_categorized genres ->
                    let genresJson = genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_list SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_detail SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec

                | Series.Series_poster_replaced posterRef ->
                    conn
                    |> Db.newCommand "UPDATE series_list SET poster_ref = @poster_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "poster_ref", SqlType.String posterRef ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_detail SET poster_ref = @poster_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "poster_ref", SqlType.String posterRef ]
                    |> Db.exec

                | Series.Series_backdrop_replaced backdropRef ->
                    conn
                    |> Db.newCommand "UPDATE series_detail SET backdrop_ref = @backdrop_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "backdrop_ref", SqlType.String backdropRef ]
                    |> Db.exec

                | Series.Series_recommended_by friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT recommended_by FROM series_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "recommended_by")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current @ [ friendSlug ] |> List.distinct
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_detail SET recommended_by = @recommended_by WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "recommended_by", SqlType.String updatedJson ]
                    |> Db.exec

                | Series.Series_recommendation_removed friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT recommended_by FROM series_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "recommended_by")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current |> List.filter (fun s -> s <> friendSlug)
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_detail SET recommended_by = @recommended_by WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "recommended_by", SqlType.String updatedJson ]
                    |> Db.exec

                | Series.Series_want_to_watch_with friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT want_to_watch_with FROM series_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "want_to_watch_with")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current @ [ friendSlug ] |> List.distinct
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_detail SET want_to_watch_with = @want_to_watch_with WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "want_to_watch_with", SqlType.String updatedJson ]
                    |> Db.exec

                | Series.Series_removed_want_to_watch_with friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT want_to_watch_with FROM series_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "want_to_watch_with")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current |> List.filter (fun s -> s <> friendSlug)
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_detail SET want_to_watch_with = @want_to_watch_with WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "want_to_watch_with", SqlType.String updatedJson ]
                    |> Db.exec

                | Series.Series_personal_rating_set rating ->
                    conn
                    |> Db.newCommand "UPDATE series_detail SET personal_rating = @personal_rating WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "personal_rating", match rating with Some r -> SqlType.Int32 r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Series.Rewatch_session_created data ->
                    let friendsJson = data.FriendSlugs |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO series_rewatch_sessions (rewatch_id, series_slug, name, is_default, friends)
                        VALUES (@rewatch_id, @series_slug, @name, 0, @friends)
                    """
                    |> Db.setParams [
                        "rewatch_id", SqlType.String data.RewatchId
                        "series_slug", SqlType.String slug
                        "name", match data.Name with Some n -> SqlType.String n | None -> SqlType.Null
                        "friends", SqlType.String friendsJson
                    ]
                    |> Db.exec

                | Series.Rewatch_session_removed rewatchId ->
                    conn
                    |> Db.newCommand "DELETE FROM series_rewatch_sessions WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                    |> Db.setParams [ "rewatch_id", SqlType.String rewatchId; "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_episode_progress WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                    |> Db.setParams [ "rewatch_id", SqlType.String rewatchId; "slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Default_rewatch_session_changed newDefaultId ->
                    conn
                    |> Db.newCommand "UPDATE series_rewatch_sessions SET is_default = 0 WHERE series_slug = @slug AND is_default = 1"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_rewatch_sessions SET is_default = 1 WHERE series_slug = @slug AND rewatch_id = @rewatch_id"
                    |> Db.setParams [ "slug", SqlType.String slug; "rewatch_id", SqlType.String newDefaultId ]
                    |> Db.exec

                | Series.Rewatch_session_friend_added data ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT friends FROM series_rewatch_sessions WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                        |> Db.setParams [ "rewatch_id", SqlType.String data.RewatchId; "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "friends")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current @ [ data.FriendSlug ] |> List.distinct
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_rewatch_sessions SET friends = @friends WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                    |> Db.setParams [
                        "rewatch_id", SqlType.String data.RewatchId
                        "slug", SqlType.String slug
                        "friends", SqlType.String updatedJson
                    ]
                    |> Db.exec

                | Series.Rewatch_session_friend_removed data ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT friends FROM series_rewatch_sessions WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                        |> Db.setParams [ "rewatch_id", SqlType.String data.RewatchId; "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "friends")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current |> List.filter (fun s -> s <> data.FriendSlug)
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE series_rewatch_sessions SET friends = @friends WHERE rewatch_id = @rewatch_id AND series_slug = @slug"
                    |> Db.setParams [
                        "rewatch_id", SqlType.String data.RewatchId
                        "slug", SqlType.String slug
                        "friends", SqlType.String updatedJson
                    ]
                    |> Db.exec

                | Series.Episode_watched data ->
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
                        VALUES (@slug, @rewatch_id, @season_number, @episode_number, @watched_date)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rewatch_id", SqlType.String data.RewatchId
                        "season_number", SqlType.Int32 data.SeasonNumber
                        "episode_number", SqlType.Int32 data.EpisodeNumber
                        "watched_date", SqlType.String data.Date
                    ]
                    |> Db.exec
                    recalculateProgress conn slug

                | Series.Episode_unwatched data ->
                    conn
                    |> Db.newCommand """
                        DELETE FROM series_episode_progress
                        WHERE series_slug = @slug AND rewatch_id = @rewatch_id AND season_number = @season_number AND episode_number = @episode_number
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rewatch_id", SqlType.String data.RewatchId
                        "season_number", SqlType.Int32 data.SeasonNumber
                        "episode_number", SqlType.Int32 data.EpisodeNumber
                    ]
                    |> Db.exec
                    recalculateProgress conn slug

                | Series.Season_marked_watched data ->
                    // Get all episodes in this season from series_episodes
                    let episodes =
                        conn
                        |> Db.newCommand "SELECT episode_number FROM series_episodes WHERE series_slug = @slug AND season_number = @season_number"
                        |> Db.setParams [ "slug", SqlType.String slug; "season_number", SqlType.Int32 data.SeasonNumber ]
                        |> Db.query (fun (rd: IDataReader) -> rd.ReadInt32 "episode_number")
                    for epNum in episodes do
                        conn
                        |> Db.newCommand """
                            INSERT OR REPLACE INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
                            VALUES (@slug, @rewatch_id, @season_number, @episode_number, @watched_date)
                        """
                        |> Db.setParams [
                            "slug", SqlType.String slug
                            "rewatch_id", SqlType.String data.RewatchId
                            "season_number", SqlType.Int32 data.SeasonNumber
                            "episode_number", SqlType.Int32 epNum
                            "watched_date", SqlType.String data.Date
                        ]
                        |> Db.exec
                    recalculateProgress conn slug

                | Series.Episodes_watched_up_to data ->
                    for epNum in 1 .. data.EpisodeNumber do
                        conn
                        |> Db.newCommand """
                            INSERT OR REPLACE INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
                            VALUES (@slug, @rewatch_id, @season_number, @episode_number, @watched_date)
                        """
                        |> Db.setParams [
                            "slug", SqlType.String slug
                            "rewatch_id", SqlType.String data.RewatchId
                            "season_number", SqlType.Int32 data.SeasonNumber
                            "episode_number", SqlType.Int32 epNum
                            "watched_date", SqlType.String data.Date
                        ]
                        |> Db.exec
                    recalculateProgress conn slug

                | Series.Season_marked_unwatched data ->
                    conn
                    |> Db.newCommand """
                        DELETE FROM series_episode_progress
                        WHERE series_slug = @slug AND rewatch_id = @rewatch_id AND season_number = @season_number
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rewatch_id", SqlType.String data.RewatchId
                        "season_number", SqlType.Int32 data.SeasonNumber
                    ]
                    |> Db.exec
                    recalculateProgress conn slug

                | Series.Episode_watched_date_changed data ->
                    conn
                    |> Db.newCommand """
                        UPDATE series_episode_progress SET watched_date = @watched_date
                        WHERE series_slug = @slug AND rewatch_id = @rewatch_id
                          AND season_number = @season_number AND episode_number = @episode_number
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rewatch_id", SqlType.String data.RewatchId
                        "season_number", SqlType.Int32 data.SeasonNumber
                        "episode_number", SqlType.Int32 data.EpisodeNumber
                        "watched_date", SqlType.String data.Date
                    ]
                    |> Db.exec

                | Series.Series_abandoned ->
                    conn
                    |> Db.newCommand "UPDATE series_detail SET abandoned = 1 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_list SET abandoned = 1 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Series_unabandoned ->
                    conn
                    |> Db.newCommand "UPDATE series_detail SET abandoned = 0 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_list SET abandoned = 0 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Series_in_focus_set ->
                    conn
                    |> Db.newCommand "UPDATE series_list SET in_focus = 1 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_detail SET in_focus = 1 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Series.Series_in_focus_cleared ->
                    conn
                    |> Db.newCommand "UPDATE series_list SET in_focus = 0 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE series_detail SET in_focus = 0 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

    let handler: Projection.ProjectionHandler = {
        Name = "SeriesProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let private parseStatus (s: string) : Mediatheca.Shared.SeriesStatus =
        match s with
        | "Returning" -> Mediatheca.Shared.Returning
        | "Ended" -> Mediatheca.Shared.Ended
        | "Canceled" -> Mediatheca.Shared.Canceled
        | "InProduction" -> Mediatheca.Shared.InProduction
        | "Planned" -> Mediatheca.Shared.Planned
        | _ -> Mediatheca.Shared.UnknownStatus

    let private resolveFriendRefs (conn: SqliteConnection) (slugs: string list) : Mediatheca.Shared.FriendRef list =
        if List.isEmpty slugs then []
        else
            let friendMap =
                conn
                |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list"
                |> Db.query (fun (rd: IDataReader) ->
                    rd.ReadString "slug",
                    (rd.ReadString "name",
                     if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                     else Some (rd.ReadString "image_ref")))
                |> Map.ofList
            slugs |> List.map (fun s ->
                let name, imageRef =
                    friendMap |> Map.tryFind s |> Option.defaultValue (s, None)
                { Mediatheca.Shared.FriendRef.Slug = s
                  Name = name
                  ImageRef = imageRef })

    let getAll (conn: SqliteConnection) : Mediatheca.Shared.SeriesListItem list =
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref, genres, tmdb_rating, status, season_count, episode_count, watched_episode_count, next_up_season, next_up_episode, next_up_title, abandoned, in_focus FROM series_list ORDER BY name"
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            let nextUp =
                if rd.IsDBNull(rd.GetOrdinal("next_up_season")) then None
                else
                    Some {
                        Mediatheca.Shared.NextUpDto.SeasonNumber = rd.ReadInt32 "next_up_season"
                        Mediatheca.Shared.NextUpDto.EpisodeNumber = rd.ReadInt32 "next_up_episode"
                        Mediatheca.Shared.NextUpDto.EpisodeName = rd.ReadString "next_up_title"
                    }
            { Mediatheca.Shared.SeriesListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              Genres = genres
              TmdbRating =
                if rd.IsDBNull(rd.GetOrdinal("tmdb_rating")) then None
                else Some (rd.ReadDouble "tmdb_rating")
              Status = parseStatus (rd.ReadString "status")
              SeasonCount = rd.ReadInt32 "season_count"
              EpisodeCount = rd.ReadInt32 "episode_count"
              WatchedEpisodeCount = rd.ReadInt32 "watched_episode_count"
              NextUp = nextUp
              IsAbandoned = rd.ReadInt32 "abandoned" = 1
              InFocus = rd.ReadInt32 "in_focus" <> 0 }
        )

    let search (conn: SqliteConnection) (query: string) : Mediatheca.Shared.LibrarySearchResult list =
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref FROM series_list WHERE name LIKE @q ORDER BY name LIMIT 10"
        |> Db.setParams [ "q", SqlType.String ("%" + query + "%") ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.LibrarySearchResult.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              MediaType = Mediatheca.Shared.Series }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) (rewatchId: string option) : Mediatheca.Shared.SeriesDetail option =
        conn
        |> Db.newCommand "SELECT slug, name, year, overview, genres, poster_ref, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime, status, personal_rating, recommended_by, want_to_watch_with, abandoned, in_focus FROM series_detail WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            let recommendedByJson = rd.ReadString "recommended_by"
            let recommendedBySlugs =
                Decode.fromString (Decode.list Decode.string) recommendedByJson
                |> Result.defaultValue []
            let wantToWatchWithJson = rd.ReadString "want_to_watch_with"
            let wantToWatchWithSlugs =
                Decode.fromString (Decode.list Decode.string) wantToWatchWithJson
                |> Result.defaultValue []

            // Determine which rewatch session to show progress for
            let activeRewatchId =
                match rewatchId with
                | Some id -> id
                | None ->
                    conn
                    |> Db.newCommand "SELECT rewatch_id FROM series_rewatch_sessions WHERE series_slug = @slug AND is_default = 1"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.querySingle (fun (rd2: IDataReader) -> rd2.ReadString "rewatch_id")
                    |> Option.defaultValue "default"

            // Get watched episodes for active session
            let activeWatched =
                conn
                |> Db.newCommand "SELECT season_number, episode_number, watched_date FROM series_episode_progress WHERE series_slug = @slug AND rewatch_id = @rewatch_id"
                |> Db.setParams [ "slug", SqlType.String slug; "rewatch_id", SqlType.String activeRewatchId ]
                |> Db.query (fun (rd2: IDataReader) ->
                    (rd2.ReadInt32 "season_number", rd2.ReadInt32 "episode_number"),
                    if rd2.IsDBNull(rd2.GetOrdinal("watched_date")) then None
                    else Some (rd2.ReadString "watched_date")
                )
                |> Map.ofList

            // Get all watched episodes across ALL sessions for overall counts
            let overallWatched =
                conn
                |> Db.newCommand """
                    SELECT DISTINCT season_number, episode_number
                    FROM series_episode_progress
                    WHERE series_slug = @slug
                """
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.query (fun (rd2: IDataReader) ->
                    rd2.ReadInt32 "season_number", rd2.ReadInt32 "episode_number"
                )
                |> Set.ofList

            // Get seasons
            let seasons =
                conn
                |> Db.newCommand "SELECT series_slug, season_number, name, overview, poster_ref, air_date, episode_count FROM series_seasons WHERE series_slug = @slug ORDER BY season_number"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.query (fun (rd2: IDataReader) ->
                    let seasonNumber = rd2.ReadInt32 "season_number"
                    // Get episodes for this season
                    let episodes =
                        conn
                        |> Db.newCommand "SELECT episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating FROM series_episodes WHERE series_slug = @slug AND season_number = @season_number ORDER BY episode_number"
                        |> Db.setParams [ "slug", SqlType.String slug; "season_number", SqlType.Int32 seasonNumber ]
                        |> Db.query (fun (rd3: IDataReader) ->
                            let epNum = rd3.ReadInt32 "episode_number"
                            let watchInfo = activeWatched |> Map.tryFind (seasonNumber, epNum)
                            { Mediatheca.Shared.EpisodeDto.EpisodeNumber = epNum
                              Mediatheca.Shared.EpisodeDto.Name = rd3.ReadString "name"
                              Mediatheca.Shared.EpisodeDto.Overview = rd3.ReadString "overview"
                              Mediatheca.Shared.EpisodeDto.Runtime =
                                if rd3.IsDBNull(rd3.GetOrdinal("runtime")) then None
                                else Some (rd3.ReadInt32 "runtime")
                              Mediatheca.Shared.EpisodeDto.AirDate =
                                if rd3.IsDBNull(rd3.GetOrdinal("air_date")) then None
                                else Some (rd3.ReadString "air_date")
                              Mediatheca.Shared.EpisodeDto.StillRef =
                                if rd3.IsDBNull(rd3.GetOrdinal("still_ref")) then None
                                else Some (rd3.ReadString "still_ref")
                              Mediatheca.Shared.EpisodeDto.TmdbRating =
                                if rd3.IsDBNull(rd3.GetOrdinal("tmdb_rating")) then None
                                else Some (rd3.ReadDouble "tmdb_rating")
                              Mediatheca.Shared.EpisodeDto.IsWatched = watchInfo.IsSome
                              Mediatheca.Shared.EpisodeDto.WatchedDate = watchInfo |> Option.bind id }
                        )
                    let watchedCount =
                        episodes |> List.filter (fun e -> e.IsWatched) |> List.length
                    let overallWatchedCount =
                        episodes |> List.filter (fun e -> overallWatched |> Set.contains (seasonNumber, e.EpisodeNumber)) |> List.length
                    { Mediatheca.Shared.SeasonDto.SeasonNumber = seasonNumber
                      Mediatheca.Shared.SeasonDto.Name = rd2.ReadString "name"
                      Mediatheca.Shared.SeasonDto.Overview = rd2.ReadString "overview"
                      Mediatheca.Shared.SeasonDto.PosterRef =
                        if rd2.IsDBNull(rd2.GetOrdinal("poster_ref")) then None
                        else Some (rd2.ReadString "poster_ref")
                      Mediatheca.Shared.SeasonDto.AirDate =
                        if rd2.IsDBNull(rd2.GetOrdinal("air_date")) then None
                        else Some (rd2.ReadString "air_date")
                      Mediatheca.Shared.SeasonDto.Episodes = episodes
                      Mediatheca.Shared.SeasonDto.WatchedCount = watchedCount
                      Mediatheca.Shared.SeasonDto.OverallWatchedCount = overallWatchedCount }
                )

            let totalEpisodes = seasons |> List.sumBy (fun s -> s.Episodes.Length)

            // Ensure default rewatch session exists (repairs legacy data)
            let hasDefault =
                conn
                |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_rewatch_sessions WHERE series_slug = @slug AND is_default = 1"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun (rd2: IDataReader) -> rd2.ReadInt32 "cnt")
                |> Option.defaultValue 0
            if hasDefault = 0 then
                conn
                |> Db.newCommand """
                    INSERT OR REPLACE INTO series_rewatch_sessions (rewatch_id, series_slug, name, is_default, friends)
                    VALUES ('default', @series_slug, NULL, 1, '[]')
                """
                |> Db.setParams [ "series_slug", SqlType.String slug ]
                |> Db.exec

            // Get rewatch sessions
            let rewatchSessions =
                conn
                |> Db.newCommand "SELECT rewatch_id, series_slug, name, is_default, friends FROM series_rewatch_sessions WHERE series_slug = @slug ORDER BY is_default DESC, rewatch_id"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.query (fun (rd2: IDataReader) ->
                    let rewatchId = rd2.ReadString "rewatch_id"
                    let friendsJson = rd2.ReadString "friends"
                    let friendSlugs =
                        Decode.fromString (Decode.list Decode.string) friendsJson
                        |> Result.defaultValue []
                    let watchedCount =
                        conn
                        |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE series_slug = @slug AND rewatch_id = @rewatch_id"
                        |> Db.setParams [ "slug", SqlType.String slug; "rewatch_id", SqlType.String rewatchId ]
                        |> Db.querySingle (fun (rd3: IDataReader) -> rd3.ReadInt32 "cnt")
                        |> Option.defaultValue 0
                    let completionPct =
                        if totalEpisodes = 0 then 0.0
                        else float watchedCount / float totalEpisodes * 100.0
                    { Mediatheca.Shared.RewatchSessionDto.RewatchId = rewatchId
                      Mediatheca.Shared.RewatchSessionDto.Name =
                        if rd2.IsDBNull(rd2.GetOrdinal("name")) then None
                        else Some (rd2.ReadString "name")
                      Mediatheca.Shared.RewatchSessionDto.IsDefault = rd2.ReadInt32 "is_default" = 1
                      Mediatheca.Shared.RewatchSessionDto.Friends = resolveFriendRefs conn friendSlugs
                      Mediatheca.Shared.RewatchSessionDto.WatchedCount = watchedCount
                      Mediatheca.Shared.RewatchSessionDto.TotalEpisodes = totalEpisodes
                      Mediatheca.Shared.RewatchSessionDto.CompletionPercentage = completionPct }
                )

            let streamId = Series.streamId slug
            let cast = CastStore.getSeriesCast conn streamId
            { Mediatheca.Shared.SeriesDetail.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              Overview = rd.ReadString "overview"
              Genres = genres
              Status = parseStatus (rd.ReadString "status")
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              BackdropRef =
                if rd.IsDBNull(rd.GetOrdinal("backdrop_ref")) then None
                else Some (rd.ReadString "backdrop_ref")
              TmdbId = rd.ReadInt32 "tmdb_id"
              TmdbRating =
                if rd.IsDBNull(rd.GetOrdinal("tmdb_rating")) then None
                else Some (rd.ReadDouble "tmdb_rating")
              EpisodeRuntime =
                if rd.IsDBNull(rd.GetOrdinal("episode_runtime")) then None
                else Some (rd.ReadInt32 "episode_runtime")
              PersonalRating =
                if rd.IsDBNull(rd.GetOrdinal("personal_rating")) then None
                else Some (rd.ReadInt32 "personal_rating")
              IsAbandoned = rd.ReadInt32 "abandoned" = 1
              InFocus = rd.ReadInt32 "in_focus" <> 0
              Cast = cast
              RecommendedBy = resolveFriendRefs conn recommendedBySlugs
              WantToWatchWith = resolveFriendRefs conn wantToWatchWithSlugs
              Seasons = seasons
              RewatchSessions = rewatchSessions
              ContentBlocks = ContentBlockProjection.getByMovie conn slug }
        )

    let getRecentSeries (conn: SqliteConnection) (count: int) : Mediatheca.Shared.RecentSeriesItem list =
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref, episode_count, watched_episode_count, next_up_season, next_up_episode, next_up_title FROM series_list ORDER BY rowid DESC LIMIT @count"
        |> Db.setParams [ "count", SqlType.Int32 count ]
        |> Db.query (fun (rd: IDataReader) ->
            let nextUp =
                if rd.IsDBNull(rd.GetOrdinal("next_up_season")) then None
                else
                    Some {
                        Mediatheca.Shared.NextUpDto.SeasonNumber = rd.ReadInt32 "next_up_season"
                        Mediatheca.Shared.NextUpDto.EpisodeNumber = rd.ReadInt32 "next_up_episode"
                        Mediatheca.Shared.NextUpDto.EpisodeName = rd.ReadString "next_up_title"
                    }
            { Mediatheca.Shared.RecentSeriesItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              NextUp = nextUp
              WatchedEpisodeCount = rd.ReadInt32 "watched_episode_count"
              EpisodeCount = rd.ReadInt32 "episode_count" }
        )

    let getSeriesRecommendedByFriend (conn: SqliteConnection) (friendSlug: string) : Mediatheca.Shared.FriendMediaItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref FROM series_detail WHERE recommended_by LIKE @pattern ORDER BY name"
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.FriendMediaItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              MediaType = Mediatheca.Shared.Series }
        )

    let getSeriesWantToWatchWithFriend (conn: SqliteConnection) (friendSlug: string) : Mediatheca.Shared.FriendMediaItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref FROM series_detail WHERE want_to_watch_with LIKE @pattern ORDER BY name"
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.FriendMediaItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              MediaType = Mediatheca.Shared.Series }
        )

    let getSeriesWatchedWithFriend (conn: SqliteConnection) (friendSlug: string) : Mediatheca.Shared.FriendWatchedItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand """
            SELECT d.slug, d.name, d.year, d.poster_ref, MAX(p.watched_date) as last_watched
            FROM series_rewatch_sessions rs
            JOIN series_episode_progress p ON p.series_slug = rs.series_slug AND p.rewatch_id = rs.rewatch_id
            JOIN series_detail d ON d.slug = rs.series_slug
            WHERE rs.friends LIKE @pattern
            GROUP BY d.slug
            ORDER BY d.name
        """
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            let lastWatched =
                if rd.IsDBNull(rd.GetOrdinal("last_watched")) then []
                else [ rd.ReadString "last_watched" ]
            { Mediatheca.Shared.FriendWatchedItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              Dates = lastWatched
              MediaType = Mediatheca.Shared.Series }
        )

    let getWatchedEpisodesForSession (conn: SqliteConnection) (slug: string) (rewatchId: string) : Set<int * int> =
        conn
        |> Db.newCommand "SELECT season_number, episode_number FROM series_episode_progress WHERE series_slug = @slug AND rewatch_id = @rewatch_id"
        |> Db.setParams [ "slug", SqlType.String slug; "rewatch_id", SqlType.String rewatchId ]
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number"
        )
        |> Set.ofList

    let getDefaultRewatchId (conn: SqliteConnection) (slug: string) : string =
        conn
        |> Db.newCommand "SELECT rewatch_id FROM series_rewatch_sessions WHERE series_slug = @slug AND is_default = 1"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "rewatch_id")
        |> Option.defaultValue "default"

    // Dashboard queries

    let getDashboardSeriesNextUp (conn: SqliteConnection) (limit: int option) : Mediatheca.Shared.DashboardSeriesNextUp list =
        let limitClause =
            match limit with
            | Some n -> sprintf "LIMIT %d" n
            | None -> ""
        conn
        |> Db.newCommand (sprintf """
            SELECT sl.slug, sl.name, sl.poster_ref, sl.next_up_season, sl.next_up_episode, sl.next_up_title,
                   sl.in_focus, sl.abandoned, sl.episode_count, sl.watched_episode_count,
                   rs.friends,
                   (SELECT MAX(watched_date) FROM series_episode_progress WHERE series_slug = sl.slug) as last_watched_date,
                   sd.backdrop_ref,
                   ep.still_ref as episode_still_ref,
                   ep.overview as episode_overview,
                   jej.jellyfin_id as jellyfin_episode_id
            FROM series_list sl
            LEFT JOIN series_rewatch_sessions rs ON rs.series_slug = sl.slug AND rs.is_default = 1
            LEFT JOIN series_detail sd ON sd.slug = sl.slug
            LEFT JOIN series_episodes ep ON ep.series_slug = sl.slug AND ep.season_number = sl.next_up_season AND ep.episode_number = sl.next_up_episode
            LEFT JOIN series_episode_jellyfin jej ON jej.series_slug = sl.slug AND jej.season_number = sl.next_up_season AND jej.episode_number = sl.next_up_episode
            WHERE sl.next_up_season IS NOT NULL
               OR sl.in_focus = 1
               OR sl.abandoned = 1
               OR (sl.episode_count > 0
                   AND sl.watched_episode_count >= sl.episode_count
                   AND sl.abandoned = 0
                   AND (SELECT MAX(watched_date) FROM series_episode_progress
                        WHERE series_slug = sl.slug) >= date('now', '-7 days'))
            ORDER BY sl.in_focus DESC, last_watched_date DESC NULLS LAST
            %s
        """ limitClause)
        |> Db.query (fun (rd: IDataReader) ->
            let slug = rd.ReadString "slug"
            let friendsJson =
                if rd.IsDBNull(rd.GetOrdinal("friends")) then "[]"
                else rd.ReadString "friends"
            let friendSlugs =
                Decode.fromString (Decode.list Decode.string) friendsJson
                |> Result.defaultValue []
            let episodeCount = rd.ReadInt32 "episode_count"
            let watchedCount = rd.ReadInt32 "watched_episode_count"
            let isFinished = episodeCount > 0 && watchedCount >= episodeCount
            { Mediatheca.Shared.DashboardSeriesNextUp.Slug = slug
              Name = rd.ReadString "name"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              BackdropRef =
                if rd.IsDBNull(rd.GetOrdinal("backdrop_ref")) then None
                else Some (rd.ReadString "backdrop_ref")
              EpisodeStillRef =
                if rd.IsDBNull(rd.GetOrdinal("episode_still_ref")) then None
                else Some (rd.ReadString "episode_still_ref")
              EpisodeOverview =
                if rd.IsDBNull(rd.GetOrdinal("episode_overview")) then None
                else
                    let ov = rd.ReadString "episode_overview"
                    if System.String.IsNullOrWhiteSpace(ov) then None else Some ov
              NextUpSeason =
                if rd.IsDBNull(rd.GetOrdinal("next_up_season")) then 0
                else rd.ReadInt32 "next_up_season"
              NextUpEpisode =
                if rd.IsDBNull(rd.GetOrdinal("next_up_episode")) then 0
                else rd.ReadInt32 "next_up_episode"
              NextUpTitle =
                if rd.IsDBNull(rd.GetOrdinal("next_up_title")) then ""
                else rd.ReadString "next_up_title"
              WatchWithFriends = resolveFriendRefs conn friendSlugs
              InFocus = rd.ReadInt32 "in_focus" <> 0
              IsFinished = isFinished
              IsAbandoned = rd.ReadInt32 "abandoned" = 1
              LastWatchedDate =
                if rd.IsDBNull(rd.GetOrdinal("last_watched_date")) then None
                else Some (rd.ReadString "last_watched_date")
              JellyfinEpisodeId =
                if rd.IsDBNull(rd.GetOrdinal("jellyfin_episode_id")) then None
                else Some (rd.ReadString "jellyfin_episode_id")
              EpisodeCount = episodeCount
              WatchedEpisodeCount = watchedCount
              AverageRuntimeMinutes =
                let avgRt =
                    conn
                    |> Db.newCommand "SELECT AVG(runtime) as avg_rt FROM series_episodes WHERE series_slug = @slug AND runtime IS NOT NULL"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.querySingle (fun rd2 ->
                        if rd2.IsDBNull(rd2.GetOrdinal("avg_rt")) then None
                        else Some (rd2.ReadInt32 "avg_rt"))
                avgRt |> Option.flatten }
        )

    let getRecentlyFinished (conn: SqliteConnection) : Mediatheca.Shared.SeriesListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, name, year, poster_ref, genres, tmdb_rating, status, season_count, episode_count,
                   watched_episode_count, next_up_season, next_up_episode, next_up_title, abandoned, in_focus
            FROM series_list
            WHERE episode_count > 0 AND watched_episode_count >= episode_count AND abandoned = 0
            ORDER BY rowid DESC
            LIMIT 10
        """
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            let nextUp =
                if rd.IsDBNull(rd.GetOrdinal("next_up_season")) then None
                else
                    Some {
                        Mediatheca.Shared.NextUpDto.SeasonNumber = rd.ReadInt32 "next_up_season"
                        Mediatheca.Shared.NextUpDto.EpisodeNumber = rd.ReadInt32 "next_up_episode"
                        Mediatheca.Shared.NextUpDto.EpisodeName = rd.ReadString "next_up_title"
                    }
            { Mediatheca.Shared.SeriesListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              Genres = genres
              TmdbRating =
                if rd.IsDBNull(rd.GetOrdinal("tmdb_rating")) then None
                else Some (rd.ReadDouble "tmdb_rating")
              Status = parseStatus (rd.ReadString "status")
              SeasonCount = rd.ReadInt32 "season_count"
              EpisodeCount = rd.ReadInt32 "episode_count"
              WatchedEpisodeCount = rd.ReadInt32 "watched_episode_count"
              NextUp = nextUp
              IsAbandoned = rd.ReadInt32 "abandoned" = 1
              InFocus = rd.ReadInt32 "in_focus" <> 0 }
        )

    let getRecentlyAbandoned (conn: SqliteConnection) : Mediatheca.Shared.SeriesListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, name, year, poster_ref, genres, tmdb_rating, status, season_count, episode_count,
                   watched_episode_count, next_up_season, next_up_episode, next_up_title, abandoned, in_focus
            FROM series_list
            WHERE abandoned = 1
            ORDER BY rowid DESC
            LIMIT 10
        """
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            let nextUp =
                if rd.IsDBNull(rd.GetOrdinal("next_up_season")) then None
                else
                    Some {
                        Mediatheca.Shared.NextUpDto.SeasonNumber = rd.ReadInt32 "next_up_season"
                        Mediatheca.Shared.NextUpDto.EpisodeNumber = rd.ReadInt32 "next_up_episode"
                        Mediatheca.Shared.NextUpDto.EpisodeName = rd.ReadString "next_up_title"
                    }
            { Mediatheca.Shared.SeriesListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              Genres = genres
              TmdbRating =
                if rd.IsDBNull(rd.GetOrdinal("tmdb_rating")) then None
                else Some (rd.ReadDouble "tmdb_rating")
              Status = parseStatus (rd.ReadString "status")
              SeasonCount = rd.ReadInt32 "season_count"
              EpisodeCount = rd.ReadInt32 "episode_count"
              WatchedEpisodeCount = rd.ReadInt32 "watched_episode_count"
              NextUp = nextUp
              IsAbandoned = rd.ReadInt32 "abandoned" = 1
              InFocus = rd.ReadInt32 "in_focus" <> 0 }
        )

    // Dashboard: Currently watching count (series with unwatched episodes, not abandoned)
    let getCurrentlyWatchingCount (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand """
            SELECT COUNT(*) as cnt FROM series_list
            WHERE watched_episode_count > 0
              AND watched_episode_count < episode_count
              AND abandoned = 0
              AND episode_count > 0
        """
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Dashboard: Average series rating
    let getAverageSeriesRating (conn: SqliteConnection) : float option =
        conn
        |> Db.newCommand """
            SELECT AVG(CAST(personal_rating AS REAL)) as avg_rating
            FROM series_detail
            WHERE personal_rating IS NOT NULL
        """
        |> Db.querySingle (fun rd ->
            if rd.IsDBNull(rd.GetOrdinal("avg_rating")) then None
            else Some (rd.ReadDouble "avg_rating"))
        |> Option.flatten

    // Dashboard: Completion rate (finished / (finished + in-progress), excluding abandoned)
    let getCompletionRate (conn: SqliteConnection) : float option =
        let finished =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list WHERE episode_count > 0 AND watched_episode_count >= episode_count AND abandoned = 0"
            |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
        let started =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list WHERE watched_episode_count > 0 AND abandoned = 0 AND episode_count > 0"
            |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
        if started = 0 then None
        else Some (float finished / float started * 100.0)

    // Dashboard: Series ratings distribution
    let getSeriesRatingDistribution (conn: SqliteConnection) : (int * int) list =
        conn
        |> Db.newCommand """
            SELECT personal_rating, COUNT(*) as count
            FROM series_detail
            WHERE personal_rating IS NOT NULL
            GROUP BY personal_rating
            ORDER BY personal_rating
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "personal_rating", rd.ReadInt32 "count")

    // Dashboard: Series genre distribution (top 10)
    let getSeriesGenreDistribution (conn: SqliteConnection) : (string * int) list =
        let allGenres =
            conn
            |> Db.newCommand "SELECT genres FROM series_detail"
            |> Db.query (fun (rd: IDataReader) ->
                let genresJson = rd.ReadString "genres"
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue [])
        allGenres
        |> List.concat
        |> List.countBy id
        |> List.sortByDescending snd
        |> List.truncate 10

    // Dashboard: Monthly episode activity (last 12 months)
    let getMonthlyEpisodeActivity (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT strftime('%Y-%m', watched_date) as month, COUNT(*) as episodes
            FROM series_episode_progress
            WHERE watched_date >= date('now', '-12 months')
              AND watched_date IS NOT NULL
            GROUP BY month
            ORDER BY month
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "month", rd.ReadInt32 "episodes")

    // Dashboard: Episode activity per day (last 14 days), grouped by series
    let getEpisodeActivity (conn: SqliteConnection) : Mediatheca.Shared.DashboardEpisodeActivity list =
        conn
        |> Db.newCommand """
            SELECT sep.watched_date, s.name, s.slug, COUNT(*) as episode_count
            FROM series_episode_progress sep
            JOIN series_list s ON sep.series_slug = s.slug
            WHERE sep.watched_date >= date('now', '-14 days')
              AND sep.watched_date IS NOT NULL
            GROUP BY sep.watched_date, s.slug
            ORDER BY sep.watched_date
        """
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.DashboardEpisodeActivity.Date = rd.ReadString "watched_date"
              SeriesName = rd.ReadString "name"
              SeriesSlug = rd.ReadString "slug"
              EpisodeCount = rd.ReadInt32 "episode_count" })

    // Dashboard: Most watched with (friends from rewatch sessions, by episode count)
    let getSeriesTopWatchedWith (conn: SqliteConnection) (limit: int) : Mediatheca.Shared.DashboardSeriesWatchedWith list =
        // Get all rewatch sessions with friends
        let sessionsWithFriends =
            conn
            |> Db.newCommand """
                SELECT rs.rewatch_id, rs.series_slug, rs.friends,
                       (SELECT COUNT(*) FROM series_episode_progress WHERE series_slug = rs.series_slug AND rewatch_id = rs.rewatch_id) as ep_count
                FROM series_rewatch_sessions rs
                WHERE rs.friends != '[]' AND rs.friends IS NOT NULL
            """
            |> Db.query (fun (rd: IDataReader) ->
                let friendsJson = rd.ReadString "friends"
                let friendSlugs =
                    Decode.fromString (Decode.list Decode.string) friendsJson
                    |> Result.defaultValue []
                let epCount = rd.ReadInt32 "ep_count"
                friendSlugs |> List.map (fun slug -> slug, epCount))
        // Aggregate by friend slug
        let friendEpisodeCounts =
            sessionsWithFriends
            |> List.concat
            |> List.groupBy fst
            |> List.map (fun (slug, entries) -> slug, entries |> List.sumBy snd)
            |> List.sortByDescending snd
            |> List.truncate limit
        // Resolve friend details
        friendEpisodeCounts
        |> List.choose (fun (slug, epCount) ->
            let friendRef =
                conn
                |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun rd ->
                    { Slug = rd.ReadString "slug"
                      Name = rd.ReadString "name"
                      ImageRef =
                        if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                        else Some (rd.ReadString "image_ref")
                      EpisodeCount = epCount } : Mediatheca.Shared.DashboardSeriesWatchedWith)
            friendRef)

    // Cross-media: Total series watch time in minutes
    let getTotalSeriesWatchTimeMinutes (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand """
            SELECT COALESCE(SUM(e.runtime), 0) as total
            FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress) p
            JOIN series_episodes e ON e.series_slug = p.series_slug AND e.season_number = p.season_number AND e.episode_number = p.episode_number
        """
        |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
        |> Option.defaultValue 0

    // Cross-media: Episodes watched this year
    let getEpisodesWatchedThisYear (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE watched_date >= strftime('%Y-01-01', 'now') AND watched_date IS NOT NULL"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Episodes watched this month
    let getEpisodesWatchedThisMonth (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE watched_date >= strftime('%Y-%m-01', 'now') AND watched_date IS NOT NULL"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Episodes watched this week (last 7 days)
    let getEpisodesWatchedThisWeek (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE watched_date >= date('now', '-7 days') AND watched_date IS NOT NULL"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Daily episode activity for last 365 days
    let getDailyEpisodeActivity (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT watched_date as date, COUNT(*) as count
            FROM series_episode_progress
            WHERE watched_date >= date('now', '-365 days') AND watched_date IS NOT NULL
            GROUP BY watched_date
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "date", rd.ReadInt32 "count")

    // Cross-media: Monthly series minutes for last 12 months
    let getMonthlySeriesMinutes (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT strftime('%Y-%m', sep.watched_date) as month,
                   COALESCE(SUM(e.runtime), 0) as minutes
            FROM series_episode_progress sep
            JOIN series_episodes e ON e.series_slug = sep.series_slug
                AND e.season_number = sep.season_number
                AND e.episode_number = sep.episode_number
            WHERE sep.watched_date >= date('now', '-12 months')
              AND sep.watched_date IS NOT NULL
            GROUP BY month
            ORDER BY month
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "month", rd.ReadInt32 "minutes")
