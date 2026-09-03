/// games-t69rb: client-side coverage for `JournalBlock.hasContent` (defined
/// in Shared.fs, shared with the server's `GameDetail.HasJournalContent`
/// computation) — the blank-block rule that decides whether a game's detail
/// page defaults to the Journal tab or the Overview tab.
module Mediatheca.Client.Pages.GameDetail.JournalHasContentTests

open Fable.Mocha
open Mediatheca.Shared

/// Fills every field with a neutral, content-free default so each test case
/// reads as its scenario rather than as DTO plumbing.
let private block
    (blockType: string)
    (content: string)
    (imageRef: string option)
    (url: string option)
    : JournalBlockDto =
    { Id = System.Guid.NewGuid().ToString("N")
      ParentId = None
      BlockType = blockType
      Content = content
      Checked = false
      Collapsed = false
      Language = None
      Url = url
      ImageRef = imageRef
      Caption = None
      Position = 0
      Width = 1.0 }

let private textBlock (content: string) = block JournalBlockTypes.text content None None
let private imageBlock (imageRef: string option) = block JournalBlockTypes.image "" imageRef None

let journalHasContentTests =
    testList "games-t69rb: JournalBlock.hasContent" [

        testCase "(1) an empty block list has no content" <| fun () ->
            Expect.isFalse (JournalBlock.hasContent []) "no blocks at all means nothing to show"

        testCase "(2) only-blank text blocks have no content" <| fun () ->
            let blocks = [ textBlock ""; textBlock "   "; textBlock "\t\n " ]
            Expect.isFalse (JournalBlock.hasContent blocks) "whitespace-only text blocks count as empty"

        testCase "(3) one text block with content counts as content" <| fun () ->
            let blocks = [ textBlock ""; textBlock "Finally beat the final boss." ]
            Expect.isTrue (JournalBlock.hasContent blocks) "a non-whitespace text block is content"

        testCase "(4) one image block with ImageRef and empty Content counts as content" <| fun () ->
            let blocks = [ imageBlock (Some "content/screenshot.png") ]
            Expect.isTrue (JournalBlock.hasContent blocks) "image blocks carry no text but still count via ImageRef"
    ]

Mocha.runTests journalHasContentTests |> ignore
