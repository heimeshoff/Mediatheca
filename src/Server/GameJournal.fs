namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

/// Plain-SQLite storage for the Notion-style game journal (not event-sourced).
/// The whole block document of a game is replaced atomically on save.
module GameJournal =

    let initialize (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS game_journal_blocks (
                id         TEXT PRIMARY KEY,
                game_slug  TEXT NOT NULL,
                parent_id  TEXT,
                block_type TEXT NOT NULL,
                content    TEXT NOT NULL DEFAULT '',
                checked    INTEGER NOT NULL DEFAULT 0,
                collapsed  INTEGER NOT NULL DEFAULT 0,
                language   TEXT,
                url        TEXT,
                image_ref  TEXT,
                caption    TEXT,
                position   INTEGER NOT NULL DEFAULT 0,
                width      REAL NOT NULL DEFAULT 1.0
            );
            CREATE INDEX IF NOT EXISTS idx_game_journal_blocks_game ON game_journal_blocks(game_slug);
        """
        |> Db.exec

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

    let get (conn: SqliteConnection) (gameSlug: string) : JournalBlockDto list =
        conn
        |> Db.newCommand """
            SELECT id, parent_id, block_type, content, checked, collapsed, language, url, image_ref, caption, position, width
            FROM game_journal_blocks
            WHERE game_slug = @game_slug
            ORDER BY position
        """
        |> Db.setParams [ "game_slug", SqlType.String gameSlug ]
        |> Db.query readBlock

    // administration-cx92m (ADR-0030): `dbLock` is the same process-wide
    // SemaphoreSlim guarding `Api.executeCommand` and
    // `Administration.importEventsStreamHandler`'s import call — this
    // function's `conn.BeginTransaction()` is one of the exact 3
    // request-reachable transaction-opening choke points on the shared
    // `conn` that ADR-0030 serializes to prevent the empirically-observed
    // `SqliteConnection does not support nested transactions` crash under
    // concurrent requests.
    let save (conn: SqliteConnection) (dbLock: System.Threading.SemaphoreSlim) (gameSlug: string) (blocks: JournalBlockDto list) : Result<unit, string> =
        dbLock.Wait()
        try
            try
                use tran = conn.BeginTransaction()

                use deleteCmd = conn.CreateCommand()
                deleteCmd.Transaction <- tran
                deleteCmd.CommandText <- "DELETE FROM game_journal_blocks WHERE game_slug = @game_slug"
                deleteCmd.Parameters.AddWithValue("@game_slug", gameSlug) |> ignore
                deleteCmd.ExecuteNonQuery() |> ignore

                for block in blocks do
                    use cmd = conn.CreateCommand()
                    cmd.Transaction <- tran
                    cmd.CommandText <- """
                        INSERT INTO game_journal_blocks (id, game_slug, parent_id, block_type, content, checked, collapsed, language, url, image_ref, caption, position, width)
                        VALUES (@id, @game_slug, @parent_id, @block_type, @content, @checked, @collapsed, @language, @url, @image_ref, @caption, @position, @width)
                    """
                    let addParam (name: string) (value: obj) =
                        cmd.Parameters.AddWithValue(name, value) |> ignore
                    let addOpt (name: string) (value: string option) =
                        match value with
                        | Some v -> addParam name (box v)
                        | None -> addParam name (box System.DBNull.Value)
                    addParam "@id" (box block.Id)
                    addParam "@game_slug" (box gameSlug)
                    addOpt "@parent_id" block.ParentId
                    addParam "@block_type" (box block.BlockType)
                    addParam "@content" (box block.Content)
                    addParam "@checked" (box (if block.Checked then 1 else 0))
                    addParam "@collapsed" (box (if block.Collapsed then 1 else 0))
                    addOpt "@language" block.Language
                    addOpt "@url" block.Url
                    addOpt "@image_ref" block.ImageRef
                    addOpt "@caption" block.Caption
                    addParam "@position" (box block.Position)
                    addParam "@width" (box block.Width)
                    cmd.ExecuteNonQuery() |> ignore

                tran.Commit()
                Ok ()
            with ex ->
                Error $"Failed to save journal: {ex.Message}"
        finally
            dbLock.Release() |> ignore

    /// Delete a game's whole journal: its uploaded content images first, then
    /// the block rows. Called when the game itself is removed from the library.
    let deleteForGame (conn: SqliteConnection) (imageBasePath: string) (gameSlug: string) : unit =
        for block in get conn gameSlug do
            match block.ImageRef with
            // only journal uploads live under content/ — never touch posters/backdrops
            | Some imageRef when imageRef.StartsWith("content/") ->
                try ImageStore.deleteImage imageBasePath imageRef with _ -> ()
            | _ -> ()
        conn
        |> Db.newCommand "DELETE FROM game_journal_blocks WHERE game_slug = @game_slug"
        |> Db.setParams [ "game_slug", SqlType.String gameSlug ]
        |> Db.exec

    // ── Migration from the old event-sourced content blocks ──────────────────
    //
    // Old game journals lived in the shared content_blocks projection
    // (block types: text, quote, callout, code, screenshot, link; optional
    // RowGroup pairs for side-by-side layout). Every old type maps cleanly
    // onto the new model, so this is a full conversion. As insurance, each
    // migrated journal is also dumped to DATA_DIR/journal-export/<slug>.md
    // so the content can be recovered manually if anything looks off.

    let private newId () = System.Guid.NewGuid().ToString("N")

    let private mapOldType (oldType: string) =
        match oldType with
        | "screenshot" -> JournalBlockTypes.image
        | "quote" -> JournalBlockTypes.quote
        | "callout" -> JournalBlockTypes.callout
        | "code" -> JournalBlockTypes.code
        | "link" -> JournalBlockTypes.link
        | _ -> JournalBlockTypes.text

    let convertOldBlocks (oldBlocks: ContentBlockDto list) : JournalBlockDto list =
        let sorted = oldBlocks |> List.sortBy (fun b -> b.Position)
        let emptyBlock id parentId blockType position : JournalBlockDto =
            { Id = id
              ParentId = parentId
              BlockType = blockType
              Content = ""
              Checked = false
              Collapsed = false
              Language = None
              Url = None
              ImageRef = None
              Caption = None
              Position = position
              Width = 1.0 }
        let convertContent (parentId: string option) (position: int) (old: ContentBlockDto) : JournalBlockDto =
            { emptyBlock old.BlockId parentId (mapOldType old.BlockType) position with
                Content = old.Content
                Url = old.Url
                ImageRef = old.ImageRef
                Caption = old.Caption }
        // Walk in position order, emitting row-grouped pairs as columnList/column trees
        let mutable result = []
        let mutable seen = Set.empty
        let mutable rootPos = 0
        for block in sorted do
            if not (seen.Contains block.BlockId) then
                let partner =
                    match block.RowGroup with
                    | Some rg ->
                        sorted |> List.tryFind (fun b ->
                            b.BlockId <> block.BlockId && b.RowGroup = Some rg && not (seen.Contains b.BlockId))
                    | None -> None
                match partner with
                | Some p ->
                    seen <- seen.Add(block.BlockId).Add(p.BlockId)
                    let first, second =
                        if (block.RowPosition |> Option.defaultValue 0) <= (p.RowPosition |> Option.defaultValue 1)
                        then block, p
                        else p, block
                    let listId = newId ()
                    let col1Id = newId ()
                    let col2Id = newId ()
                    result <- result @ [
                        emptyBlock listId None JournalBlockTypes.columnList rootPos
                        { emptyBlock col1Id (Some listId) JournalBlockTypes.column 0 with Width = 0.5 }
                        { emptyBlock col2Id (Some listId) JournalBlockTypes.column 1 with Width = 0.5 }
                        convertContent (Some col1Id) 0 first
                        convertContent (Some col2Id) 0 second
                    ]
                    rootPos <- rootPos + 1
                | None ->
                    seen <- seen.Add block.BlockId
                    result <- result @ [ convertContent None rootPos block ]
                    rootPos <- rootPos + 1
        result

    let dumpToMarkdown (gameSlug: string) (oldBlocks: ContentBlockDto list) : string =
        let sorted = oldBlocks |> List.sortBy (fun b -> b.Position)
        let lines =
            sorted
            |> List.collect (fun b ->
                match b.BlockType with
                | "screenshot" ->
                    let alt = b.Caption |> Option.defaultValue "screenshot"
                    let ref = b.ImageRef |> Option.defaultValue ""
                    [ sprintf "![%s](/images/%s)" alt ref; "" ]
                | "quote" ->
                    [ sprintf "> %s" (b.Content.Replace("\n", "\n> ")); "" ]
                | "callout" ->
                    [ sprintf "> [!NOTE] %s" (b.Content.Replace("\n", "\n> ")); "" ]
                | "code" ->
                    [ "```"; b.Content; "```"; "" ]
                | "link" ->
                    let url = b.Url |> Option.defaultValue ""
                    let title = if b.Content = "" then url else b.Content
                    [ sprintf "[%s](%s)" title url; "" ]
                | _ ->
                    [ b.Content; "" ])
        sprintf "# %s\n\n%s" gameSlug (String.concat "\n" lines)

    let private getGameSlugs (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT slug FROM game_detail"
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "slug")

    /// One-time, idempotent: convert old game content blocks into the new
    /// journal table (skipping games that already have a new journal), and
    /// write a markdown dump per migrated game for manual recovery.
    let migrateFromContentBlocks (conn: SqliteConnection) (dbLock: System.Threading.SemaphoreSlim) (dataDir: string) : unit =
        match SettingsStore.getSetting conn "game_journal_migrated" with
        | Some "1" -> ()
        | _ ->
            try
                let exportDir = System.IO.Path.Combine(dataDir, "journal-export")
                for slug in getGameSlugs conn do
                    let oldBlocks = ContentBlockProjection.getForMovieDetail conn slug
                    if not (List.isEmpty oldBlocks) && List.isEmpty (get conn slug) then
                        if not (System.IO.Directory.Exists exportDir) then
                            System.IO.Directory.CreateDirectory exportDir |> ignore
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(exportDir, slug + ".md"),
                            dumpToMarkdown slug oldBlocks)
                        match save conn dbLock slug (convertOldBlocks oldBlocks) with
                        | Ok () -> printfn "[GameJournal] Migrated %d blocks for %s" oldBlocks.Length slug
                        | Error e -> eprintfn "[GameJournal] Migration failed for %s: %s" slug e
                SettingsStore.setSetting conn "game_journal_migrated" "1"
            with ex ->
                eprintfn "[GameJournal] Migration failed: %s" ex.Message
