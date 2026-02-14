namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald

module ContentBlockProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS content_blocks (
                block_id     TEXT PRIMARY KEY,
                movie_slug   TEXT NOT NULL,
                session_id   TEXT,
                block_type   TEXT NOT NULL,
                content      TEXT NOT NULL DEFAULT '',
                image_ref    TEXT,
                url          TEXT,
                caption      TEXT,
                position     INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_content_blocks_movie ON content_blocks(movie_slug);
            CREATE INDEX IF NOT EXISTS idx_content_blocks_session ON content_blocks(movie_slug, session_id);
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS content_blocks;
        """
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("ContentBlocks-")) then ()
        else
            let movieSlug = event.StreamId.Substring(14) // Remove "ContentBlocks-" prefix
            match ContentBlocks.Serialization.fromStoredEvent event with
            | None -> ()
            | Some blockEvent ->
                match blockEvent with
                | ContentBlocks.Content_block_added (data, position, sessionId) ->
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO content_blocks (block_id, movie_slug, session_id, block_type, content, image_ref, url, caption, position)
                        VALUES (@block_id, @movie_slug, @session_id, @block_type, @content, @image_ref, @url, @caption, @position)
                    """
                    |> Db.setParams [
                        "block_id", SqlType.String data.BlockId
                        "movie_slug", SqlType.String movieSlug
                        "session_id", match sessionId with Some s -> SqlType.String s | None -> SqlType.Null
                        "block_type", SqlType.String data.BlockType
                        "content", SqlType.String data.Content
                        "image_ref", match data.ImageRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "url", match data.Url with Some u -> SqlType.String u | None -> SqlType.Null
                        "caption", match data.Caption with Some c -> SqlType.String c | None -> SqlType.Null
                        "position", SqlType.Int32 position
                    ]
                    |> Db.exec

                | ContentBlocks.Content_block_updated (blockId, content, imageRef, url, caption) ->
                    conn
                    |> Db.newCommand """
                        UPDATE content_blocks
                        SET content = @content, image_ref = @image_ref, url = @url, caption = @caption
                        WHERE block_id = @block_id
                    """
                    |> Db.setParams [
                        "block_id", SqlType.String blockId
                        "content", SqlType.String content
                        "image_ref", match imageRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "url", match url with Some u -> SqlType.String u | None -> SqlType.Null
                        "caption", match caption with Some c -> SqlType.String c | None -> SqlType.Null
                    ]
                    |> Db.exec

                | ContentBlocks.Content_block_removed blockId ->
                    conn
                    |> Db.newCommand "DELETE FROM content_blocks WHERE block_id = @block_id"
                    |> Db.setParams [ "block_id", SqlType.String blockId ]
                    |> Db.exec

                | ContentBlocks.Content_block_type_changed (blockId, blockType) ->
                    conn
                    |> Db.newCommand "UPDATE content_blocks SET block_type = @block_type WHERE block_id = @block_id"
                    |> Db.setParams [
                        "block_id", SqlType.String blockId
                        "block_type", SqlType.String blockType
                    ]
                    |> Db.exec

                | ContentBlocks.Content_blocks_reordered (blockIds, _sessionId) ->
                    blockIds
                    |> List.iteri (fun i bid ->
                        conn
                        |> Db.newCommand "UPDATE content_blocks SET position = @position WHERE block_id = @block_id"
                        |> Db.setParams [
                            "block_id", SqlType.String bid
                            "position", SqlType.Int32 i
                        ]
                        |> Db.exec)

    let handler: Projection.ProjectionHandler = {
        Name = "ContentBlockProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let private readContentBlockDto (rd: IDataReader) : Mediatheca.Shared.ContentBlockDto =
        { Mediatheca.Shared.ContentBlockDto.BlockId = rd.ReadString "block_id"
          BlockType = rd.ReadString "block_type"
          Content = rd.ReadString "content"
          ImageRef =
            if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
            else Some (rd.ReadString "image_ref")
          Url =
            if rd.IsDBNull(rd.GetOrdinal("url")) then None
            else Some (rd.ReadString "url")
          Caption =
            if rd.IsDBNull(rd.GetOrdinal("caption")) then None
            else Some (rd.ReadString "caption")
          Position = rd.ReadInt32 "position" }

    let getByMovie (conn: SqliteConnection) (movieSlug: string) : Mediatheca.Shared.ContentBlockDto list =
        conn
        |> Db.newCommand """
            SELECT block_id, block_type, content, image_ref, url, caption, position
            FROM content_blocks
            WHERE movie_slug = @movie_slug
            ORDER BY position
        """
        |> Db.setParams [ "movie_slug", SqlType.String movieSlug ]
        |> Db.query readContentBlockDto

    let getBySession (conn: SqliteConnection) (movieSlug: string) (sessionId: string) : Mediatheca.Shared.ContentBlockDto list =
        conn
        |> Db.newCommand """
            SELECT block_id, block_type, content, image_ref, url, caption, position
            FROM content_blocks
            WHERE movie_slug = @movie_slug AND session_id = @session_id
            ORDER BY position
        """
        |> Db.setParams [
            "movie_slug", SqlType.String movieSlug
            "session_id", SqlType.String sessionId
        ]
        |> Db.query readContentBlockDto

    let getForMovieDetail (conn: SqliteConnection) (movieSlug: string) : Mediatheca.Shared.ContentBlockDto list =
        conn
        |> Db.newCommand """
            SELECT block_id, block_type, content, image_ref, url, caption, position
            FROM content_blocks
            WHERE movie_slug = @movie_slug AND session_id IS NULL
            ORDER BY position
        """
        |> Db.setParams [ "movie_slug", SqlType.String movieSlug ]
        |> Db.query readContentBlockDto
