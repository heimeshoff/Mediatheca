namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald

module CastStore =

    let initialize (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS cast_members (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT    NOT NULL,
                tmdb_id    INTEGER NOT NULL UNIQUE,
                image_ref  TEXT
            );

            CREATE TABLE IF NOT EXISTS movie_cast (
                movie_stream_id  TEXT    NOT NULL,
                cast_member_id   INTEGER NOT NULL REFERENCES cast_members(id),
                role             TEXT    NOT NULL,
                billing_order    INTEGER NOT NULL,
                is_top_billed    INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (movie_stream_id, cast_member_id)
            );

            CREATE INDEX IF NOT EXISTS idx_movie_cast_movie ON movie_cast(movie_stream_id);
        """
        |> Db.exec

    let upsertCastMember (conn: SqliteConnection) (name: string) (tmdbId: int) (imageRef: string option) : int64 =
        conn
        |> Db.newCommand """
            INSERT INTO cast_members (name, tmdb_id, image_ref)
            VALUES (@name, @tmdb_id, @image_ref)
            ON CONFLICT(tmdb_id) DO UPDATE SET
                name = @name,
                image_ref = COALESCE(@image_ref, cast_members.image_ref)
            RETURNING id
        """
        |> Db.setParams [
            "name", SqlType.String name
            "tmdb_id", SqlType.Int32 tmdbId
            "image_ref", match imageRef with Some r -> SqlType.String r | None -> SqlType.Null
        ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "id")
        |> Option.defaultWith (fun () -> failwith "Failed to upsert cast member")

    let addMovieCast (conn: SqliteConnection) (movieStreamId: string) (castMemberId: int64) (role: string) (billingOrder: int) (isTopBilled: bool) : unit =
        conn
        |> Db.newCommand """
            INSERT OR IGNORE INTO movie_cast (movie_stream_id, cast_member_id, role, billing_order, is_top_billed)
            VALUES (@movie_stream_id, @cast_member_id, @role, @billing_order, @is_top_billed)
        """
        |> Db.setParams [
            "movie_stream_id", SqlType.String movieStreamId
            "cast_member_id", SqlType.Int64 castMemberId
            "role", SqlType.String role
            "billing_order", SqlType.Int32 billingOrder
            "is_top_billed", SqlType.Int32 (if isTopBilled then 1 else 0)
        ]
        |> Db.exec

    let getMovieCast (conn: SqliteConnection) (movieStreamId: string) : Mediatheca.Shared.CastMemberDto list =
        conn
        |> Db.newCommand """
            SELECT cm.name, mc.role, cm.image_ref, cm.tmdb_id
            FROM movie_cast mc
            JOIN cast_members cm ON cm.id = mc.cast_member_id
            WHERE mc.movie_stream_id = @movie_stream_id
            ORDER BY mc.billing_order
        """
        |> Db.setParams [ "movie_stream_id", SqlType.String movieStreamId ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.CastMemberDto.Name = rd.ReadString "name"
              Role = rd.ReadString "role"
              ImageRef =
                if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                else Some (rd.ReadString "image_ref")
              TmdbId = rd.ReadInt32 "tmdb_id" }
        )

    let removeMovieCastAndCleanup (conn: SqliteConnection) (imageBasePath: string) (movieStreamId: string) : unit =
        // Get orphan-candidate cast member ids before deleting
        let castMemberIds =
            conn
            |> Db.newCommand "SELECT cast_member_id FROM movie_cast WHERE movie_stream_id = @movie_stream_id"
            |> Db.setParams [ "movie_stream_id", SqlType.String movieStreamId ]
            |> Db.query (fun (rd: IDataReader) -> rd.ReadInt64 "cast_member_id")

        // Delete movie_cast rows
        conn
        |> Db.newCommand "DELETE FROM movie_cast WHERE movie_stream_id = @movie_stream_id"
        |> Db.setParams [ "movie_stream_id", SqlType.String movieStreamId ]
        |> Db.exec

        // Find and clean up orphaned cast members
        for cmId in castMemberIds do
            let usageCount =
                conn
                |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_cast WHERE cast_member_id = @id"
                |> Db.setParams [ "id", SqlType.Int64 cmId ]
                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "cnt")
                |> Option.defaultValue 0L

            if usageCount = 0L then
                // Get image ref before deleting
                let imageRef =
                    conn
                    |> Db.newCommand "SELECT image_ref FROM cast_members WHERE id = @id"
                    |> Db.setParams [ "id", SqlType.Int64 cmId ]
                    |> Db.querySingle (fun (rd: IDataReader) ->
                        if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                        else Some (rd.ReadString "image_ref")
                    )
                    |> Option.flatten

                // Delete cast member
                conn
                |> Db.newCommand "DELETE FROM cast_members WHERE id = @id"
                |> Db.setParams [ "id", SqlType.Int64 cmId ]
                |> Db.exec

                // Delete cast member image
                match imageRef with
                | Some ref -> ImageStore.deleteImage imageBasePath ref
                | None -> ()
