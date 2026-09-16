module Mediatheca.Tests.NotesTests

open Expecto
open Mediatheca.Server.Notes
open Mediatheca.Shared

/// curation-h98ve (ADR-0080): pure decide/evolve coverage for Notes — a
/// document stream, not an aggregate (see the module's own doc comment).
/// Mirrors ContentBlocksTests.fs's given/when/then shape.

let private mkBlock (id: string) (position: int) : JournalBlockDto = {
    Id = id
    ParentId = None
    BlockType = JournalBlockTypes.text
    Content = sprintf "content-%s" id
    Checked = false
    Collapsed = false
    Language = None
    Url = None
    ImageRef = None
    Caption = None
    Position = position
    Width = 1.0
}

let private givenWhenThen (given: NotesEvent list) (command: NotesCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let notesTests =
    testList "Notes" [

        testList "Save_notes" [
            testCase "saving into an empty document produces one Notes_saved event" <| fun _ ->
                let blocks = [ mkBlock "b1" 0; mkBlock "b2" 1 ]
                match givenWhenThen [] (Save_notes blocks) with
                | Ok [ Notes_saved saved ] -> Expect.equal saved blocks "the whole block list is carried in the event"
                | other -> failtestf "expected one Notes_saved event, got %A" other

            testCase "two identical saves in a row produce no second event (no-op save)" <| fun _ ->
                let blocks = [ mkBlock "b1" 0; mkBlock "b2" 1 ]
                let result = givenWhenThen [ Notes_saved blocks ] (Save_notes blocks)
                Expect.equal result (Ok []) "an identical save must append nothing"

            testCase "a reorder-only save (same ids/content, different order/position) still appends an event" <| fun _ ->
                let original = [ mkBlock "b1" 0; mkBlock "b2" 1 ]
                let reordered = [ { mkBlock "b2" 0 with Content = "content-b2" }; { mkBlock "b1" 1 with Content = "content-b1" } ]
                match givenWhenThen [ Notes_saved original ] (Save_notes reordered) with
                | Ok [ Notes_saved saved ] -> Expect.equal saved reordered "the reordered document is what gets appended"
                | other -> failtestf "expected a reorder to append one Notes_saved event, got %A" other

            testCase "saving two blocks that share an id is refused" <| fun _ ->
                let blocks = [ mkBlock "dup" 0; { mkBlock "dup" 1 with Content = "different content" } ]
                match givenWhenThen [] (Save_notes blocks) with
                | Error msg -> Expect.stringContains msg "same id" "should mention the duplicate-id refusal"
                | Ok _ -> failtest "expected duplicate block ids to be refused"

            testCase "saving more than 5000 blocks is refused" <| fun _ ->
                let blocks = [ for i in 0 .. 5000 -> mkBlock (sprintf "b%d" i) i ]
                match givenWhenThen [] (Save_notes blocks) with
                | Error msg -> Expect.stringContains msg "5000" "should mention the size cap"
                | Ok _ -> failtest "expected more than 5000 blocks to be refused"

            testCase "saving exactly 5000 blocks is accepted" <| fun _ ->
                let blocks = [ for i in 0 .. 4999 -> mkBlock (sprintf "b%d" i) i ]
                match givenWhenThen [] (Save_notes blocks) with
                | Ok [ Notes_saved _ ] -> ()
                | other -> failtestf "expected exactly 5000 blocks to be accepted, got %A" other
        ]

        testList "streamId / storageToken" [
            testCase "storageToken is frozen to movie/series/game/book, independent of any DU rename" <| fun _ ->
                Expect.equal (storageToken Movie) "movie" "movie token"
                Expect.equal (storageToken Series) "series" "series token"
                Expect.equal (storageToken Game) "game" "game token"
                Expect.equal (storageToken Book) "book" "book token"

            testCase "streamId embeds the owner's media type and slug" <| fun _ ->
                Expect.equal (streamId Game "elden-ring-2022") "Notes-game-elden-ring-2022" "game stream id"
                Expect.equal (streamId Movie "the-matrix-1999") "Notes-movie-the-matrix-1999" "movie stream id"

            testCase "parseStreamId is the inverse of streamId, including hyphenated slugs" <| fun _ ->
                let cases = [ Movie, "the-matrix-1999"; Series, "the-wire-2002"; Game, "elden-ring-2022"; Book, "project-hail-mary-2021" ]
                for (mediaType, slug) in cases do
                    Expect.equal (parseStreamId (streamId mediaType slug)) (Some (mediaType, slug))
                        (sprintf "round-trip for %A/%s" mediaType slug)

            testCase "parseStreamId returns None for a stream id from a different bounded context" <| fun _ ->
                Expect.isNone (parseStreamId "ContentBlocks-some-slug") "not a Notes- stream"
                Expect.isNone (parseStreamId "Notes-unknowntoken-some-slug") "unrecognized storage token"
        ]
    ]
