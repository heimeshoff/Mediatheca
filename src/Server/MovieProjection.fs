namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net

module MovieProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS movie_list (
                slug        TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                year        INTEGER NOT NULL,
                poster_ref  TEXT,
                genres      TEXT NOT NULL DEFAULT '[]',
                tmdb_rating REAL
            );

            CREATE TABLE IF NOT EXISTS movie_detail (
                slug          TEXT PRIMARY KEY,
                name          TEXT NOT NULL,
                year          INTEGER NOT NULL,
                runtime       INTEGER,
                overview      TEXT NOT NULL DEFAULT '',
                genres        TEXT NOT NULL DEFAULT '[]',
                poster_ref    TEXT,
                backdrop_ref  TEXT,
                tmdb_id       INTEGER NOT NULL,
                tmdb_rating   REAL,
                recommended_by     TEXT NOT NULL DEFAULT '[]',
                want_to_watch_with TEXT NOT NULL DEFAULT '[]'
            );
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS movie_list;
            DROP TABLE IF EXISTS movie_detail;
        """
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Movie-")) then ()
        else
            let slug = event.StreamId.Substring(6) // Remove "Movie-" prefix
            match Catalog.Serialization.fromStoredEvent event with
            | None -> ()
            | Some movieEvent ->
                match movieEvent with
                | Catalog.MovieAddedToLibrary data ->
                    let genresJson = data.Genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO movie_list (slug, name, year, poster_ref, genres, tmdb_rating)
                        VALUES (@slug, @name, @year, @poster_ref, @genres, @tmdb_rating)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "poster_ref", match data.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "genres", SqlType.String genresJson
                        "tmdb_rating", match data.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec

                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO movie_detail (slug, name, year, runtime, overview, genres, poster_ref, backdrop_ref, tmdb_id, tmdb_rating, recommended_by, want_to_watch_with)
                        VALUES (@slug, @name, @year, @runtime, @overview, @genres, @poster_ref, @backdrop_ref, @tmdb_id, @tmdb_rating, '[]', '[]')
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "runtime", match data.Runtime with Some r -> SqlType.Int32 r | None -> SqlType.Null
                        "overview", SqlType.String data.Overview
                        "genres", SqlType.String genresJson
                        "poster_ref", match data.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "backdrop_ref", match data.BackdropRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "tmdb_id", SqlType.Int32 data.TmdbId
                        "tmdb_rating", match data.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Catalog.MovieRemovedFromLibrary ->
                    conn
                    |> Db.newCommand "DELETE FROM movie_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM movie_detail WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Catalog.MovieCategorized genres ->
                    let genresJson = genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE movie_list SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec

                | Catalog.MoviePosterReplaced posterRef ->
                    conn
                    |> Db.newCommand "UPDATE movie_list SET poster_ref = @poster_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "poster_ref", SqlType.String posterRef ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET poster_ref = @poster_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "poster_ref", SqlType.String posterRef ]
                    |> Db.exec

                | Catalog.MovieBackdropReplaced backdropRef ->
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET backdrop_ref = @backdrop_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "backdrop_ref", SqlType.String backdropRef ]
                    |> Db.exec

                | Catalog.MovieRecommendedBy friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT recommended_by FROM movie_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "recommended_by")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current @ [ friendSlug ] |> List.distinct
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET recommended_by = @recommended_by WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "recommended_by", SqlType.String updatedJson ]
                    |> Db.exec

                | Catalog.RecommendationRemoved friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT recommended_by FROM movie_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "recommended_by")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current |> List.filter (fun s -> s <> friendSlug)
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET recommended_by = @recommended_by WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "recommended_by", SqlType.String updatedJson ]
                    |> Db.exec

                | Catalog.WantToWatchWith friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT want_to_watch_with FROM movie_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "want_to_watch_with")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current @ [ friendSlug ] |> List.distinct
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET want_to_watch_with = @want_to_watch_with WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "want_to_watch_with", SqlType.String updatedJson ]
                    |> Db.exec

                | Catalog.RemovedWantToWatchWith friendSlug ->
                    let currentJson =
                        conn
                        |> Db.newCommand "SELECT want_to_watch_with FROM movie_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "want_to_watch_with")
                        |> Option.defaultValue "[]"
                    let current =
                        Decode.fromString (Decode.list Decode.string) currentJson
                        |> Result.defaultValue []
                    let updated = current |> List.filter (fun s -> s <> friendSlug)
                    let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE movie_detail SET want_to_watch_with = @want_to_watch_with WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "want_to_watch_with", SqlType.String updatedJson ]
                    |> Db.exec

    let handler: Projection.ProjectionHandler = {
        Name = "MovieProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let getAll (conn: SqliteConnection) : Mediatheca.Shared.MovieListItem list =
        conn
        |> Db.newCommand "SELECT slug, name, year, poster_ref, genres, tmdb_rating FROM movie_list ORDER BY name"
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            { Mediatheca.Shared.MovieListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("poster_ref")) then None
                else Some (rd.ReadString "poster_ref")
              Genres = genres
              TmdbRating =
                if rd.IsDBNull(rd.GetOrdinal("tmdb_rating")) then None
                else Some (rd.ReadDouble "tmdb_rating") }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) : Mediatheca.Shared.MovieDetail option =
        conn
        |> Db.newCommand "SELECT slug, name, year, runtime, overview, genres, poster_ref, backdrop_ref, tmdb_id, tmdb_rating, recommended_by, want_to_watch_with FROM movie_detail WHERE slug = @slug"
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
            let streamId = Catalog.streamId slug
            let cast = CastStore.getMovieCast conn streamId
            { Mediatheca.Shared.MovieDetail.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              Runtime =
                if rd.IsDBNull(rd.GetOrdinal("runtime")) then None
                else Some (rd.ReadInt32 "runtime")
              Overview = rd.ReadString "overview"
              Genres = genres
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
              Cast = cast
              RecommendedBy = recommendedBySlugs |> List.map (fun s -> { Mediatheca.Shared.FriendRef.Slug = s; Name = s })
              WantToWatchWith = wantToWatchWithSlugs |> List.map (fun s -> { Mediatheca.Shared.FriendRef.Slug = s; Name = s }) }
        )
