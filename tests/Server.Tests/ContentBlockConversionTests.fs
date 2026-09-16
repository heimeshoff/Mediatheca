module Mediatheca.Tests.ContentBlockConversionTests

open Expecto
open Mediatheca.Server
open Mediatheca.Server.ContentBlockConversion
open Mediatheca.Shared

/// curation-j4qqt (ADR-0080 §10): coverage for the conversion logic lifted
/// out of the old game-journal migration module before it was deleted —
/// the row-group -> columnList/column conversion and the old-type mapping
/// are unchanged, just retargeted onto `LegacyContentBlock` instead of the
/// (also deleted) `Shared.ContentBlockDto`.

let private mkOldBlock (id: string) (blockType: string) (position: int) : LegacyContentBlock = {
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
let contentBlockConversionTests =
    testList "ContentBlockConversion.convertOldBlocks" [

        testCase "maps old block types onto the new model, legacy link becomes a link block" <| fun _ ->
            let old = [
                { mkOldBlock "o1" "text" 0 with Content = "hello" }
                { mkOldBlock "o2" "quote" 1 with Content = "quoted" }
                { mkOldBlock "o3" "callout" 2 with Content = "note" }
                { mkOldBlock "o4" "code" 3 with Content = "let x = 1" }
                { mkOldBlock "o5" "screenshot" 4 with ImageRef = Some "content/x.png"; Caption = Some "cap" }
                { mkOldBlock "o6" "link" 5 with Content = "site"; Url = Some "https://x.io" }
            ]
            let converted = convertOldBlocks old
            let typeOf id = (converted |> List.find (fun b -> b.Id = id)).BlockType
            Expect.equal (typeOf "o1") JournalBlockTypes.text "text maps to text"
            Expect.equal (typeOf "o2") JournalBlockTypes.quote "quote maps to quote"
            Expect.equal (typeOf "o3") JournalBlockTypes.callout "callout maps to callout"
            Expect.equal (typeOf "o4") JournalBlockTypes.code "code maps to code"
            Expect.equal (typeOf "o5") JournalBlockTypes.image "screenshot maps to image"
            Expect.equal (typeOf "o6") JournalBlockTypes.link "legacy link converts to a dedicated link block"
            let o5 = converted |> List.find (fun b -> b.Id = "o5")
            Expect.equal o5.ImageRef (Some "content/x.png") "image ref carried over"
            Expect.equal o5.Caption (Some "cap") "caption carried over"
            let o6 = converted |> List.find (fun b -> b.Id = "o6")
            Expect.equal o6.Url (Some "https://x.io") "link url carried over"
            let rootIds =
                converted
                |> List.filter (fun b -> b.ParentId = None)
                |> List.sortBy (fun b -> b.Position)
                |> List.map (fun b -> b.Id)
            Expect.equal rootIds [ "o1"; "o2"; "o3"; "o4"; "o5"; "o6" ] "root order preserved"

        testCase "a row-grouped pair converts into one columnList with two 50% columns carrying the two blocks" <| fun _ ->
            let old = [
                { mkOldBlock "a" "text" 0 with Content = "left"; RowGroup = Some "rg1"; RowPosition = Some 0 }
                { mkOldBlock "b" "screenshot" 1 with ImageRef = Some "content/y.png"; RowGroup = Some "rg1"; RowPosition = Some 1 }
                { mkOldBlock "c" "text" 2 with Content = "below" }
            ]
            let converted = convertOldBlocks old
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
