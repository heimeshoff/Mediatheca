namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

module NotesProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS notes_blocks (
                id         TEXT PRIMARY KEY,
                media_type TEXT    NOT NULL,
                slug       TEXT    NOT NULL,
                parent_id  TEXT,
                block_type TEXT    NOT NULL,
                content    TEXT    NOT NULL DEFAULT '',
                checked    INTEGER NOT NULL DEFAULT 0,
                collapsed  INTEGER NOT NULL DEFAULT 0,
                language   TEXT,
                url        TEXT,
                image_ref  TEXT,
                caption    TEXT,
                position   INTEGER NOT NULL DEFAULT 0,
                width      REAL    NOT NULL DEFAULT 1.0
            );
            CREATE INDEX IF NOT EXISTS idx_notes_blocks_owner ON notes_blocks(media_type, slug);
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS notes_blocks;
        """
        |> Db.exec

    let private deleteForOwner (conn: SqliteConnection) (mediaTypeToken: string) (slug: string) : unit =
        conn
        |> Db.newCommand "DELETE FROM notes_blocks WHERE media_type = @media_type AND slug = @slug"
        |> Db.setParams [
            "media_type", SqlType.String mediaTypeToken
            "slug", SqlType.String slug
        ]
        |> Db.exec

    let private insertBlock (conn: SqliteConnection) (mediaTypeToken: string) (slug: string) (block: JournalBlockDto) : unit =
        conn
        |> Db.newCommand """
            INSERT INTO notes_blocks (id, media_type, slug, parent_id, block_type, content, checked, collapsed, language, url, image_ref, caption, position, width)
            VALUES (@id, @media_type, @slug, @parent_id, @block_type, @content, @checked, @collapsed, @language, @url, @image_ref, @caption, @position, @width)
        """
        |> Db.setParams [
            "id", SqlType.String block.Id
            "media_type", SqlType.String mediaTypeToken
            "slug", SqlType.String slug
            "parent_id", (match block.ParentId with Some p -> SqlType.String p | None -> SqlType.Null)
            "block_type", SqlType.String block.BlockType
            "content", SqlType.String block.Content
            "checked", SqlType.Int32 (if block.Checked then 1 else 0)
            "collapsed", SqlType.Int32 (if block.Collapsed then 1 else 0)
            "language", (match block.Language with Some l -> SqlType.String l | None -> SqlType.Null)
            "url", (match block.Url with Some u -> SqlType.String u | None -> SqlType.Null)
            "image_ref", (match block.ImageRef with Some i -> SqlType.String i | None -> SqlType.Null)
            "caption", (match block.Caption with Some c -> SqlType.String c | None -> SqlType.Null)
            "position", SqlType.Int32 block.Position
            "width", SqlType.Double block.Width
        ]
        |> Db.exec

    /// One `Notes_saved` snapshot fully replaces its owner's rows —
    /// idempotent and order-independent across replay; last-snapshot-wins
    /// automatically since snapshots replay in ascending `stream_position`
    /// (ADR-0080 decision 6).
    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Notes-")) then ()
        else
            match Notes.parseStreamId event.StreamId with
            | None -> ()
            | Some (mediaType, slug) ->
                match Notes.Serialization.fromStoredEvent event with
                | None -> ()
                | Some (Notes.Notes_saved blocks) ->
                    let token = Notes.storageToken mediaType
                    deleteForOwner conn token slug
                    for block in blocks do
                        insertBlock conn token slug block

    let handler: Projection.ProjectionHandler = {
        Name = "NotesProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let private readBlock (rd: IDataReader) : JournalBlockDto =
        let readOpt col =
            if rd.IsDBNull(rd.GetOrdinal(col)) then None
            else Some (rd.ReadString col)
        { Id = rd.ReadString "id"
          ParentId = readOpt "parent_id"
          BlockType = rd.ReadString "block_type"
          Content = rd.ReadString "content"
          Checked = rd.ReadInt32 "checked" <> 0
          Collapsed = rd.ReadInt32 "collapsed" <> 0
          Language = readOpt "language"
          Url = readOpt "url"
          ImageRef = readOpt "image_ref"
          Caption = readOpt "caption"
          Position = rd.ReadInt32 "position"
          Width = rd.ReadDouble "width" }

    let getForOwner (conn: SqliteConnection) (mediaType: MediaType) (slug: string) : JournalBlockDto list =
        conn
        |> Db.newCommand """
            SELECT id, parent_id, block_type, content, checked, collapsed, language, url, image_ref, caption, position, width
            FROM notes_blocks
            WHERE media_type = @media_type AND slug = @slug
            ORDER BY position
        """
        |> Db.setParams [
            "media_type", SqlType.String (Notes.storageToken mediaType)
            "slug", SqlType.String slug
        ]
        |> Db.query readBlock
