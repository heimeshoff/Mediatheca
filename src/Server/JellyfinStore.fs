namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald

module JellyfinStore =

    let initialize (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS jellyfin_movie (
                movie_slug  TEXT PRIMARY KEY,
                jellyfin_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS jellyfin_series (
                series_slug TEXT PRIMARY KEY,
                jellyfin_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS jellyfin_episode (
                series_slug    TEXT NOT NULL,
                season_number  INTEGER NOT NULL,
                episode_number INTEGER NOT NULL,
                jellyfin_id    TEXT NOT NULL,
                PRIMARY KEY (series_slug, season_number, episode_number)
            );
        """
        |> Db.exec

    let clearAll (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DELETE FROM jellyfin_movie;
            DELETE FROM jellyfin_series;
            DELETE FROM jellyfin_episode;
        """
        |> Db.exec

    let setMovieJellyfinId (conn: SqliteConnection) (movieSlug: string) (jellyfinId: string) : unit =
        conn
        |> Db.newCommand """
            INSERT OR REPLACE INTO jellyfin_movie (movie_slug, jellyfin_id)
            VALUES (@slug, @jellyfin_id)
        """
        |> Db.setParams [
            "slug", SqlType.String movieSlug
            "jellyfin_id", SqlType.String jellyfinId
        ]
        |> Db.exec

    let setSeriesJellyfinId (conn: SqliteConnection) (seriesSlug: string) (jellyfinId: string) : unit =
        conn
        |> Db.newCommand """
            INSERT OR REPLACE INTO jellyfin_series (series_slug, jellyfin_id)
            VALUES (@slug, @jellyfin_id)
        """
        |> Db.setParams [
            "slug", SqlType.String seriesSlug
            "jellyfin_id", SqlType.String jellyfinId
        ]
        |> Db.exec

    let setEpisodeJellyfinId (conn: SqliteConnection) (seriesSlug: string) (seasonNumber: int) (episodeNumber: int) (jellyfinId: string) : unit =
        conn
        |> Db.newCommand """
            INSERT OR REPLACE INTO jellyfin_episode (series_slug, season_number, episode_number, jellyfin_id)
            VALUES (@slug, @season, @episode, @jellyfin_id)
        """
        |> Db.setParams [
            "slug", SqlType.String seriesSlug
            "season", SqlType.Int32 seasonNumber
            "episode", SqlType.Int32 episodeNumber
            "jellyfin_id", SqlType.String jellyfinId
        ]
        |> Db.exec

    let getMovieJellyfinId (conn: SqliteConnection) (movieSlug: string) : string option =
        conn
        |> Db.newCommand "SELECT jellyfin_id FROM jellyfin_movie WHERE movie_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String movieSlug ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "jellyfin_id")

    let getEpisodeJellyfinId (conn: SqliteConnection) (seriesSlug: string) (seasonNumber: int) (episodeNumber: int) : string option =
        conn
        |> Db.newCommand "SELECT jellyfin_id FROM jellyfin_episode WHERE series_slug = @slug AND season_number = @season AND episode_number = @episode"
        |> Db.setParams [
            "slug", SqlType.String seriesSlug
            "season", SqlType.Int32 seasonNumber
            "episode", SqlType.Int32 episodeNumber
        ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "jellyfin_id")

    /// One-time migration: copy Jellyfin data from old projection tables into JellyfinStore tables.
    /// Defensive — silently ignores missing source tables or columns.
    let migrateFromProjections (conn: SqliteConnection) : unit =
        // Migrate movie jellyfin_id from movie_detail
        try
            conn
            |> Db.newCommand """
                INSERT OR IGNORE INTO jellyfin_movie (movie_slug, jellyfin_id)
                SELECT slug, jellyfin_id FROM movie_detail WHERE jellyfin_id IS NOT NULL
            """
            |> Db.exec
        with _ -> ()

        // Migrate series jellyfin_id from series_detail
        try
            conn
            |> Db.newCommand """
                INSERT OR IGNORE INTO jellyfin_series (series_slug, jellyfin_id)
                SELECT slug, jellyfin_id FROM series_detail WHERE jellyfin_id IS NOT NULL
            """
            |> Db.exec
        with _ -> ()

        // Migrate episode jellyfin_ids from series_episode_jellyfin
        try
            conn
            |> Db.newCommand """
                INSERT OR IGNORE INTO jellyfin_episode (series_slug, season_number, episode_number, jellyfin_id)
                SELECT series_slug, season_number, episode_number, jellyfin_id FROM series_episode_jellyfin
            """
            |> Db.exec
        with _ -> ()
