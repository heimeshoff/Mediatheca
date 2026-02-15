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
                next_up_title TEXT
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
                want_to_watch_with TEXT NOT NULL DEFAULT '[]'
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
                rewatch_id TEXT PRIMARY KEY,
                series_slug TEXT NOT NULL,
                name TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                friends TEXT NOT NULL DEFAULT '[]'
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

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS series_list;
            DROP TABLE IF EXISTS series_detail;
            DROP TABLE IF EXISTS series_seasons;
            DROP TABLE IF EXISTS series_episodes;
            DROP TABLE IF EXISTS series_rewatch_sessions;
            DROP TABLE IF EXISTS series_episode_progress;
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
        |> Db.newCommand "SELECT slug, name, year, poster_ref, genres, tmdb_rating, status, season_count, episode_count, watched_episode_count, next_up_season, next_up_episode, next_up_title FROM series_list ORDER BY name"
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
              NextUp = nextUp }
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
        |> Db.newCommand "SELECT slug, name, year, overview, genres, poster_ref, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime, status, personal_rating, recommended_by, want_to_watch_with FROM series_detail WHERE slug = @slug"
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
              Cast = cast
              RecommendedBy = resolveFriendRefs conn recommendedBySlugs
              WantToWatchWith = resolveFriendRefs conn wantToWatchWithSlugs
              Seasons = seasons
              RewatchSessions = rewatchSessions
              ContentBlocks = ContentBlockProjection.getByMovie conn slug }
        )

    let getWatchedEpisodesForSession (conn: SqliteConnection) (slug: string) (rewatchId: string) : Set<int * int> =
        conn
        |> Db.newCommand "SELECT season_number, episode_number FROM series_episode_progress WHERE series_slug = @slug AND rewatch_id = @rewatch_id"
        |> Db.setParams [ "slug", SqlType.String slug; "rewatch_id", SqlType.String rewatchId ]
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number"
        )
        |> Set.ofList
