module Mediatheca.Tests.GameJournalTests

open System.IO
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

// administration-cx92m (ADR-0030): `GameJournal.save` now requires the same
// process-wide dbLock parameter production threads in from Composition.fs.
// Each test here uses its own connection with no real concurrency, so a
// fresh, never-contended lock per test file satisfies the signature exactly
// like `manualSyncTriggerLock`/`jobDbLock` do at their own uncontended
// call sites.
let private testDbLock = new SemaphoreSlim(1, 1)

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    conn

let private mkBlock (id: string) (blockType: string) (position: int) : JournalBlockDto = {
    Id = id
    ParentId = None
    BlockType = blockType
    Content = ""
    Checked = false
    Collapsed = false
    Language = None
    Url = None
    ImageRef = None
    Caption = None
    Position = position
    Width = 1.0
}

let private mkOldBlock (id: string) (blockType: string) (position: int) : ContentBlockDto = {
    BlockId = id
    BlockType = blockType
    Content = ""
    ImageRef = None
    Url = None
    Caption = None
    Position = position
    RowGroup = None
    RowPosition = None
}

[<Tests>]
let gameJournalTests =
    testList "GameJournal" [

        testList "save / get" [
            testCase "round-trips all block fields and order" <| fun _ ->
                use conn = createInMemoryConnection ()
                let blocks = [
                    { mkBlock "b1" JournalBlockTypes.heading1 0 with Content = "My Playthrough" }
                    { mkBlock "b2" JournalBlockTypes.todo 1 with Content = "Beat the boss"; Checked = true }
                    { mkBlock "b3" JournalBlockTypes.toggle 2 with Content = "Spoilers"; Collapsed = true }
                    { mkBlock "b4" JournalBlockTypes.text 0 with ParentId = Some "b3"; Content = "hidden note" }
                    { mkBlock "b5" JournalBlockTypes.code 3 with Content = "let x = 1"; Language = Some "fsharp" }
                    { mkBlock "b6" JournalBlockTypes.link 4 with Content = "RAWG"; Url = Some "https://rawg.io" }
                    { mkBlock "b7" JournalBlockTypes.image 5 with ImageRef = Some "content/abc.png"; Caption = Some "screenshot" }
                ]
                Expect.isOk (GameJournal.save conn testDbLock "elden-ring-2022" blocks) "save should succeed"
                let loaded = GameJournal.get conn "elden-ring-2022"
                Expect.equal (List.length loaded) 7 "all blocks come back"
                let b2 = loaded |> List.find (fun b -> b.Id = "b2")
                Expect.isTrue b2.Checked "todo checked state survives"
                let b3 = loaded |> List.find (fun b -> b.Id = "b3")
                Expect.isTrue b3.Collapsed "toggle collapsed state survives"
                let b4 = loaded |> List.find (fun b -> b.Id = "b4")
                Expect.equal b4.ParentId (Some "b3") "parent link survives"
                let b5 = loaded |> List.find (fun b -> b.Id = "b5")
                Expect.equal b5.Language (Some "fsharp") "code language survives"
                let b6 = loaded |> List.find (fun b -> b.Id = "b6")
                Expect.equal b6.Url (Some "https://rawg.io") "link url survives"
                let b7 = loaded |> List.find (fun b -> b.Id = "b7")
                Expect.equal b7.ImageRef (Some "content/abc.png") "image ref survives"
                Expect.equal b7.Caption (Some "screenshot") "caption survives"

            testCase "save replaces the whole document" <| fun _ ->
                use conn = createInMemoryConnection ()
                GameJournal.save conn testDbLock "game-1" [ mkBlock "old1" JournalBlockTypes.text 0; mkBlock "old2" JournalBlockTypes.text 1 ]
                |> ignore
                GameJournal.save conn testDbLock "game-1" [ mkBlock "new1" JournalBlockTypes.text 0 ]
                |> ignore
                let loaded = GameJournal.get conn "game-1"
                Expect.equal (loaded |> List.map (fun b -> b.Id)) [ "new1" ] "old blocks are gone"

            testCase "documents are per game" <| fun _ ->
                use conn = createInMemoryConnection ()
                GameJournal.save conn testDbLock "game-a" [ mkBlock "a1" JournalBlockTypes.text 0 ] |> ignore
                GameJournal.save conn testDbLock "game-b" [ mkBlock "b1" JournalBlockTypes.text 0 ] |> ignore
                Expect.equal ((GameJournal.get conn "game-a") |> List.map (fun b -> b.Id)) [ "a1" ] "game-a keeps its own blocks"
                Expect.equal ((GameJournal.get conn "game-b") |> List.map (fun b -> b.Id)) [ "b1" ] "game-b keeps its own blocks"

            testCase "column widths survive as floats" <| fun _ ->
                use conn = createInMemoryConnection ()
                let blocks = [
                    mkBlock "cl" JournalBlockTypes.columnList 0
                    { mkBlock "c1" JournalBlockTypes.column 0 with ParentId = Some "cl"; Width = 0.3 }
                    { mkBlock "c2" JournalBlockTypes.column 1 with ParentId = Some "cl"; Width = 0.7 }
                    { mkBlock "t1" JournalBlockTypes.text 0 with ParentId = Some "c1" }
                    { mkBlock "t2" JournalBlockTypes.text 0 with ParentId = Some "c2" }
                ]
                GameJournal.save conn testDbLock "game-1" blocks |> ignore
                let loaded = GameJournal.get conn "game-1"
                let c1 = loaded |> List.find (fun b -> b.Id = "c1")
                Expect.floatClose Accuracy.high c1.Width 0.3 "width survives"
        ]

        testList "deleteForGame" [
            testCase "removes the game's blocks and its uploaded content images, leaving other games alone" <| fun _ ->
                use conn = createInMemoryConnection ()
                let imageBasePath = Path.Combine(Path.GetTempPath(), "mediatheca-journal-img-test-" + System.Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory(Path.Combine(imageBasePath, "content")) |> ignore
                Directory.CreateDirectory(Path.Combine(imageBasePath, "posters")) |> ignore
                try
                    let doomedImage = "content/doomed.png"
                    let keptImage = "content/kept.png"
                    let posterImage = "posters/game-doomed-2020.jpg"
                    File.WriteAllBytes(Path.Combine(imageBasePath, doomedImage), [| 1uy |])
                    File.WriteAllBytes(Path.Combine(imageBasePath, keptImage), [| 2uy |])
                    File.WriteAllBytes(Path.Combine(imageBasePath, posterImage), [| 3uy |])

                    GameJournal.save conn testDbLock "doomed-2020" [
                        { mkBlock "d1" JournalBlockTypes.text 0 with Content = "notes" }
                        { mkBlock "d2" JournalBlockTypes.image 1 with ImageRef = Some doomedImage }
                    ] |> ignore
                    GameJournal.save conn testDbLock "kept-2021" [
                        { mkBlock "k1" JournalBlockTypes.image 0 with ImageRef = Some keptImage }
                    ] |> ignore

                    GameJournal.deleteForGame conn imageBasePath "doomed-2020"

                    Expect.isEmpty (GameJournal.get conn "doomed-2020") "doomed game's blocks removed"
                    Expect.isFalse (File.Exists(Path.Combine(imageBasePath, doomedImage))) "doomed game's content image deleted"
                    Expect.equal (List.length (GameJournal.get conn "kept-2021")) 1 "other game's blocks untouched"
                    Expect.isTrue (File.Exists(Path.Combine(imageBasePath, keptImage))) "other game's content image untouched"
                    Expect.isTrue (File.Exists(Path.Combine(imageBasePath, posterImage))) "non-content images are not this function's business"
                finally
                    try Directory.Delete(imageBasePath, true) with _ -> ()
        ]

        testList "convertOldBlocks" [
            testCase "maps old block types onto the new model" <| fun _ ->
                let old = [
                    { mkOldBlock "o1" "text" 0 with Content = "hello" }
                    { mkOldBlock "o2" "quote" 1 with Content = "quoted" }
                    { mkOldBlock "o3" "callout" 2 with Content = "note" }
                    { mkOldBlock "o4" "code" 3 with Content = "let x = 1" }
                    { mkOldBlock "o5" "screenshot" 4 with ImageRef = Some "content/x.png"; Caption = Some "cap" }
                    { mkOldBlock "o6" "link" 5 with Content = "site"; Url = Some "https://x.io" }
                ]
                let converted = GameJournal.convertOldBlocks old
                let typeOf id = (converted |> List.find (fun b -> b.Id = id)).BlockType
                Expect.equal (typeOf "o1") JournalBlockTypes.text "text maps to text"
                Expect.equal (typeOf "o2") JournalBlockTypes.quote "quote maps to quote"
                Expect.equal (typeOf "o3") JournalBlockTypes.callout "callout maps to callout"
                Expect.equal (typeOf "o4") JournalBlockTypes.code "code maps to code"
                Expect.equal (typeOf "o5") JournalBlockTypes.image "screenshot maps to image"
                Expect.equal (typeOf "o6") JournalBlockTypes.link "link maps to link"
                let o5 = converted |> List.find (fun b -> b.Id = "o5")
                Expect.equal o5.ImageRef (Some "content/x.png") "image ref carried over"
                Expect.equal o5.Caption (Some "cap") "caption carried over"
                // order preserved at root
                let rootIds =
                    converted
                    |> List.filter (fun b -> b.ParentId = None)
                    |> List.sortBy (fun b -> b.Position)
                    |> List.map (fun b -> b.Id)
                Expect.equal rootIds [ "o1"; "o2"; "o3"; "o4"; "o5"; "o6" ] "root order preserved"

            testCase "row-grouped pairs become a columnList with two 50% columns" <| fun _ ->
                let old = [
                    { mkOldBlock "a" "text" 0 with Content = "left"; RowGroup = Some "rg1"; RowPosition = Some 0 }
                    { mkOldBlock "b" "screenshot" 1 with ImageRef = Some "content/y.png"; RowGroup = Some "rg1"; RowPosition = Some 1 }
                    { mkOldBlock "c" "text" 2 with Content = "below" }
                ]
                let converted = GameJournal.convertOldBlocks old
                let columnLists = converted |> List.filter (fun blk -> blk.BlockType = JournalBlockTypes.columnList)
                Expect.equal (List.length columnLists) 1 "one columnList"
                let cl = columnLists.Head
                let cols = converted |> List.filter (fun blk -> blk.ParentId = Some cl.Id)
                Expect.equal (List.length cols) 2 "two columns"
                for col in cols do
                    Expect.floatClose Accuracy.high col.Width 0.5 "columns split 50/50"
                let colIds = cols |> List.sortBy (fun c -> c.Position) |> List.map (fun c -> c.Id)
                let inCol0 = converted |> List.find (fun blk -> blk.ParentId = Some colIds.[0])
                let inCol1 = converted |> List.find (fun blk -> blk.ParentId = Some colIds.[1])
                Expect.equal inCol0.Id "a" "left block in first column"
                Expect.equal inCol1.Id "b" "right block in second column"
                let cBlock = converted |> List.find (fun blk -> blk.Id = "c")
                Expect.equal cBlock.ParentId None "ungrouped block stays at root"
                Expect.isTrue (cBlock.Position > cl.Position) "ungrouped block stays below the row"
        ]

        testList "dumpToMarkdown" [
            testCase "renders every old block's content" <| fun _ ->
                let old = [
                    { mkOldBlock "o1" "text" 0 with Content = "plain text" }
                    { mkOldBlock "o2" "quote" 1 with Content = "the quote" }
                    { mkOldBlock "o3" "code" 2 with Content = "code here" }
                    { mkOldBlock "o4" "screenshot" 3 with ImageRef = Some "content/z.png"; Caption = Some "the cap" }
                    { mkOldBlock "o5" "link" 4 with Content = "a link"; Url = Some "https://z.io" }
                ]
                let md = GameJournal.dumpToMarkdown "my-game-2024" old
                Expect.stringContains md "# my-game-2024" "has title"
                Expect.stringContains md "plain text" "has text content"
                Expect.stringContains md "> the quote" "has quote"
                Expect.stringContains md "code here" "has code"
                Expect.stringContains md "content/z.png" "has image ref"
                Expect.stringContains md "https://z.io" "has link url"
        ]

        testList "migrateFromContentBlocks" [
            testCase "migrates old game journals once, writes markdown dump, sets flag" <| fun _ ->
                use conn = createInMemoryConnection ()
                let dataDir = Path.Combine(Path.GetTempPath(), "mediatheca-journal-test-" + System.Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory(dataDir) |> ignore
                try
                    // a game with old content blocks
                    conn
                    |> Db.newCommand "INSERT INTO game_detail (slug, name, year) VALUES ('hades-2020', 'Hades', 2020)"
                    |> Db.exec
                    conn
                    |> Db.newCommand """
                        INSERT INTO content_blocks (block_id, movie_slug, block_type, content, position)
                        VALUES ('ob1', 'hades-2020', 'text', 'old journal entry', 0)
                    """
                    |> Db.exec

                    GameJournal.migrateFromContentBlocks conn testDbLock dataDir

                    let migrated = GameJournal.get conn "hades-2020"
                    Expect.equal (List.length migrated) 1 "old block converted"
                    Expect.equal migrated.Head.Content "old journal entry" "content carried over"
                    Expect.equal migrated.Head.BlockType JournalBlockTypes.text "type mapped"

                    let dumpPath = Path.Combine(dataDir, "journal-export", "hades-2020.md")
                    Expect.isTrue (File.Exists dumpPath) "markdown dump written"
                    Expect.stringContains (File.ReadAllText dumpPath) "old journal entry" "dump has content"

                    Expect.equal (SettingsStore.getSetting conn "game_journal_migrated") (Some "1") "migration flag set"

                    // running again must not duplicate anything
                    GameJournal.migrateFromContentBlocks conn testDbLock dataDir
                    Expect.equal (List.length (GameJournal.get conn "hades-2020")) 1 "second run is a no-op"
                finally
                    try Directory.Delete(dataDir, true) with _ -> ()

            testCase "does not overwrite an existing new journal" <| fun _ ->
                use conn = createInMemoryConnection ()
                let dataDir = Path.Combine(Path.GetTempPath(), "mediatheca-journal-test-" + System.Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory(dataDir) |> ignore
                try
                    conn
                    |> Db.newCommand "INSERT INTO game_detail (slug, name, year) VALUES ('celeste-2018', 'Celeste', 2018)"
                    |> Db.exec
                    conn
                    |> Db.newCommand """
                        INSERT INTO content_blocks (block_id, movie_slug, block_type, content, position)
                        VALUES ('ob1', 'celeste-2018', 'text', 'legacy', 0)
                    """
                    |> Db.exec
                    // the new journal already has content
                    GameJournal.save conn testDbLock "celeste-2018" [ { mkBlock "n1" JournalBlockTypes.text 0 with Content = "already here" } ]
                    |> ignore

                    GameJournal.migrateFromContentBlocks conn testDbLock dataDir

                    let blocks = GameJournal.get conn "celeste-2018"
                    Expect.equal (blocks |> List.map (fun b -> b.Id)) [ "n1" ] "existing journal untouched"
                finally
                    try Directory.Delete(dataDir, true) with _ -> ()
        ]
    ]
