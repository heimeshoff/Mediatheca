namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald

module FriendProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS friend_list (
                slug       TEXT PRIMARY KEY,
                name       TEXT NOT NULL,
                image_ref  TEXT
            );
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand "DROP TABLE IF EXISTS friend_list;"
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Friend-")) then ()
        else
            let slug = event.StreamId.Substring(7) // Remove "Friend-" prefix
            match Friends.Serialization.fromStoredEvent event with
            | None -> ()
            | Some friendEvent ->
                match friendEvent with
                | Friends.Friend_added data ->
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO friend_list (slug, name, image_ref)
                        VALUES (@slug, @name, @image_ref)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "image_ref", match data.ImageRef with Some r -> SqlType.String r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Friends.Friend_updated data ->
                    conn
                    |> Db.newCommand """
                        UPDATE friend_list SET name = @name, image_ref = @image_ref WHERE slug = @slug
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "image_ref", match data.ImageRef with Some r -> SqlType.String r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Friends.Friend_removed ->
                    conn
                    |> Db.newCommand "DELETE FROM friend_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                    // Scrub removed friend from movie_detail if the table exists
                    let movieDetailExists =
                        conn
                        |> Db.newCommand "SELECT COUNT(*) as cnt FROM sqlite_master WHERE type='table' AND name='movie_detail'"
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                        |> Option.defaultValue 0

                    if movieDetailExists > 0 then
                        conn
                        |> Db.newCommand """
                            UPDATE movie_detail
                            SET recommended_by = (
                                SELECT json_group_array(j.value)
                                FROM json_each(movie_detail.recommended_by) AS j
                                WHERE j.value <> @slug
                            )
                            WHERE EXISTS (
                                SELECT 1 FROM json_each(movie_detail.recommended_by) AS j
                                WHERE j.value = @slug
                            )
                        """
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.exec

                        conn
                        |> Db.newCommand """
                            UPDATE movie_detail
                            SET want_to_watch_with = (
                                SELECT json_group_array(j.value)
                                FROM json_each(movie_detail.want_to_watch_with) AS j
                                WHERE j.value <> @slug
                            )
                            WHERE EXISTS (
                                SELECT 1 FROM json_each(movie_detail.want_to_watch_with) AS j
                                WHERE j.value = @slug
                            )
                        """
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.exec

    let handler: Projection.ProjectionHandler = {
        Name = "FriendProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let getAll (conn: SqliteConnection) : Mediatheca.Shared.FriendListItem list =
        conn
        |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list ORDER BY name"
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.FriendListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              ImageRef =
                if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                else Some (rd.ReadString "image_ref") }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) : Mediatheca.Shared.FriendDetail option =
        conn
        |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            { Mediatheca.Shared.FriendDetail.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              ImageRef =
                if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                else Some (rd.ReadString "image_ref") }
        )
