module Mediatheca.Tests.ContentBlocksTests

open Expecto
open Mediatheca.Server.ContentBlocks

let private sampleBlockData: ContentBlockData = {
    BlockId = "block-1"
    BlockType = "text"
    Content = "This is a text block"
    ImageRef = None
    Url = None
    Caption = None
}

let private sampleImageBlockData: ContentBlockData = {
    BlockId = "block-2"
    BlockType = "image"
    Content = ""
    ImageRef = Some "images/screenshot.png"
    Url = None
    Caption = Some "A screenshot"
}

let private sampleLinkBlockData: ContentBlockData = {
    BlockId = "block-3"
    BlockType = "link"
    Content = "Check out this review"
    ImageRef = None
    Url = Some "https://example.com/review"
    Caption = Some "External review"
}

let private givenWhenThen (given: ContentBlockEvent list) (command: ContentBlockCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let contentBlockTests =
    testList "ContentBlocks" [

        testList "Add_content_block" [
            testCase "adding content block produces event" <| fun _ ->
                let result = givenWhenThen [] (Add_content_block (sampleBlockData, None))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Content_block_added (data, pos, sid) ->
                        Expect.equal data.BlockId "block-1" "BlockId should match"
                        Expect.equal data.BlockType "text" "BlockType should match"
                        Expect.equal data.Content "This is a text block" "Content should match"
                        Expect.equal pos 0 "Position should be 0 for first block"
                        Expect.equal sid None "SessionId should be None"
                    | _ -> failtest "Expected Content_block_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding content block with duplicate blockId fails" <| fun _ ->
                let result = givenWhenThen
                                [ Content_block_added (sampleBlockData, 0, None) ]
                                (Add_content_block (sampleBlockData, None))
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"

            testCase "adding second block gets next position" <| fun _ ->
                let result = givenWhenThen
                                [ Content_block_added (sampleBlockData, 0, None) ]
                                (Add_content_block (sampleImageBlockData, None))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Content_block_added (_, pos, _) ->
                        Expect.equal pos 1 "Position should be 1 for second block"
                    | _ -> failtest "Expected Content_block_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding block with session id scopes position" <| fun _ ->
                let result = givenWhenThen
                                [ Content_block_added (sampleBlockData, 0, None) ]
                                (Add_content_block (sampleImageBlockData, Some "session-1"))
                match result with
                | Ok events ->
                    match events.[0] with
                    | Content_block_added (_, pos, sid) ->
                        Expect.equal pos 0 "Position should be 0 for first block in session"
                        Expect.equal sid (Some "session-1") "SessionId should match"
                    | _ -> failtest "Expected Content_block_added"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Update_content_block" [
            testCase "updating content block produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Content_block_added (sampleBlockData, 0, None) ]
                                (Update_content_block ("block-1", "Updated content", None, None, Some "New caption"))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Content_block_updated (bid, content, imgRef, url, caption) ->
                        Expect.equal bid "block-1" "BlockId should match"
                        Expect.equal content "Updated content" "Content should match"
                        Expect.equal imgRef None "ImageRef should be None"
                        Expect.equal url None "Url should be None"
                        Expect.equal caption (Some "New caption") "Caption should match"
                    | _ -> failtest "Expected Content_block_updated"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "updating non-existent block fails" <| fun _ ->
                let result = givenWhenThen [] (Update_content_block ("block-99", "Content", None, None, None))
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Remove_content_block" [
            testCase "removing content block produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Content_block_added (sampleBlockData, 0, None) ]
                                (Remove_content_block "block-1")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Content_block_removed bid ->
                        Expect.equal bid "block-1" "BlockId should match"
                    | _ -> failtest "Expected Content_block_removed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent block is no-op" <| fun _ ->
                let result = givenWhenThen [] (Remove_content_block "block-99")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Reorder_content_blocks" [
            testCase "reordering content blocks produces event" <| fun _ ->
                let given = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_block_added (sampleLinkBlockData, 2, None)
                ]
                let result = givenWhenThen given (Reorder_content_blocks ([ "block-3"; "block-1"; "block-2" ], None))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Content_blocks_reordered (bids, sid) ->
                        Expect.equal bids [ "block-3"; "block-1"; "block-2" ] "Block ids should match new order"
                        Expect.equal sid None "SessionId should be None"
                    | _ -> failtest "Expected Content_blocks_reordered"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "reordering with non-existent block fails" <| fun _ ->
                let given = [ Content_block_added (sampleBlockData, 0, None) ]
                let result = givenWhenThen given (Reorder_content_blocks ([ "block-1"; "block-99" ], None))
                match result with
                | Error msg -> Expect.stringContains msg "do not exist" "Should say do not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Evolve" [
            testCase "evolve applies reorder correctly" <| fun _ ->
                let events = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_blocks_reordered ([ "block-2"; "block-1" ], None)
                ]
                let state = reconstitute events
                let block1 = state.Blocks |> Map.find "block-1"
                let block2 = state.Blocks |> Map.find "block-2"
                Expect.equal block2.Position 0 "block-2 should be at position 0"
                Expect.equal block1.Position 1 "block-1 should be at position 1"

            testCase "evolve removes block from state" <| fun _ ->
                let events = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_removed "block-1"
                ]
                let state = reconstitute events
                Expect.equal (Map.count state.Blocks) 0 "Should have no blocks"
        ]

        testList "Group_content_blocks_in_row" [
            testCase "grouping two blocks produces event" <| fun _ ->
                let given = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                ]
                let result = givenWhenThen given (Group_content_blocks_in_row ("block-1", "block-2", "row-1"))
                match result with
                | Ok events ->
                    let grouped = events |> List.filter (fun e -> match e with Content_blocks_row_grouped _ -> true | _ -> false)
                    Expect.equal (List.length grouped) 1 "Should produce one grouped event"
                    match grouped.[0] with
                    | Content_blocks_row_grouped (lid, rid, rg) ->
                        Expect.equal lid "block-1" "Left id should match"
                        Expect.equal rid "block-2" "Right id should match"
                        Expect.equal rg "row-1" "Row group should match"
                    | _ -> failtest "Expected Content_blocks_row_grouped"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "self-group fails" <| fun _ ->
                let given = [ Content_block_added (sampleBlockData, 0, None) ]
                let result = givenWhenThen given (Group_content_blocks_in_row ("block-1", "block-1", "row-1"))
                match result with
                | Error msg -> Expect.stringContains msg "itself" "Should say cannot group with itself"
                | Ok _ -> failtest "Expected error"

            testCase "re-grouping auto-ungroups old partner" <| fun _ ->
                let given = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_block_added (sampleLinkBlockData, 2, None)
                    Content_blocks_row_grouped ("block-1", "block-2", "row-1")
                ]
                let result = givenWhenThen given (Group_content_blocks_in_row ("block-1", "block-3", "row-2"))
                match result with
                | Ok events ->
                    let ungroupEvents = events |> List.filter (fun e -> match e with Content_block_row_ungrouped _ -> true | _ -> false)
                    let ungroupedIds = ungroupEvents |> List.map (fun e -> match e with Content_block_row_ungrouped bid -> bid | _ -> "")
                    Expect.isTrue (List.contains "block-1" ungroupedIds) "block-1 should be ungrouped"
                    Expect.isTrue (List.contains "block-2" ungroupedIds) "block-2 should be ungrouped"
                    let grouped = events |> List.filter (fun e -> match e with Content_blocks_row_grouped _ -> true | _ -> false)
                    Expect.equal (List.length grouped) 1 "Should produce new grouped event"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "grouping non-existent block fails" <| fun _ ->
                let given = [ Content_block_added (sampleBlockData, 0, None) ]
                let result = givenWhenThen given (Group_content_blocks_in_row ("block-1", "block-99", "row-1"))
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Ungroup_content_block" [
            testCase "ungrouping both partners" <| fun _ ->
                let given = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_blocks_row_grouped ("block-1", "block-2", "row-1")
                ]
                let result = givenWhenThen given (Ungroup_content_block "block-1")
                match result with
                | Ok events ->
                    let ungroupedIds = events |> List.map (fun e -> match e with Content_block_row_ungrouped bid -> bid | _ -> "")
                    Expect.isTrue (List.contains "block-1" ungroupedIds) "block-1 should be ungrouped"
                    Expect.isTrue (List.contains "block-2" ungroupedIds) "block-2 should be ungrouped"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "ungrouping standalone block is no-op" <| fun _ ->
                let given = [ Content_block_added (sampleBlockData, 0, None) ]
                let result = givenWhenThen given (Ungroup_content_block "block-1")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "ungrouping non-existent block is no-op" <| fun _ ->
                let result = givenWhenThen [] (Ungroup_content_block "block-99")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Remove auto-ungroups partner" [
            testCase "removing grouped block ungroups partner" <| fun _ ->
                let given = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_blocks_row_grouped ("block-1", "block-2", "row-1")
                ]
                let result = givenWhenThen given (Remove_content_block "block-1")
                match result with
                | Ok events ->
                    let ungroupEvents = events |> List.filter (fun e -> match e with Content_block_row_ungrouped _ -> true | _ -> false)
                    Expect.equal (List.length ungroupEvents) 1 "Should ungroup partner"
                    match ungroupEvents.[0] with
                    | Content_block_row_ungrouped bid ->
                        Expect.equal bid "block-2" "Should ungroup block-2"
                    | _ -> failtest "Expected Content_block_row_ungrouped"
                    let removeEvents = events |> List.filter (fun e -> match e with Content_block_removed _ -> true | _ -> false)
                    Expect.equal (List.length removeEvents) 1 "Should still remove block"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Evolve row grouping" [
            testCase "evolve applies row grouping" <| fun _ ->
                let events = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_blocks_row_grouped ("block-1", "block-2", "row-1")
                ]
                let state = reconstitute events
                let block1 = state.Blocks |> Map.find "block-1"
                let block2 = state.Blocks |> Map.find "block-2"
                Expect.equal block1.RowGroup (Some "row-1") "block-1 should have row group"
                Expect.equal block1.RowPosition (Some 0) "block-1 should be position 0"
                Expect.equal block2.RowGroup (Some "row-1") "block-2 should have row group"
                Expect.equal block2.RowPosition (Some 1) "block-2 should be position 1"

            testCase "evolve applies row ungrouping" <| fun _ ->
                let events = [
                    Content_block_added (sampleBlockData, 0, None)
                    Content_block_added (sampleImageBlockData, 1, None)
                    Content_blocks_row_grouped ("block-1", "block-2", "row-1")
                    Content_block_row_ungrouped "block-1"
                ]
                let state = reconstitute events
                let block1 = state.Blocks |> Map.find "block-1"
                Expect.equal block1.RowGroup None "block-1 should have no row group"
                Expect.equal block1.RowPosition None "block-1 should have no row position"
        ]

        testList "Serialization" [
            testCase "Content_block_added round-trip" <| fun _ ->
                let event = Content_block_added (sampleBlockData, 0, None)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_block_added with session round-trip" <| fun _ ->
                let event = Content_block_added (sampleImageBlockData, 3, Some "session-1")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_block_updated round-trip" <| fun _ ->
                let event = Content_block_updated ("block-1", "New content", Some "img.png", Some "https://example.com", Some "Caption")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_block_updated with None fields round-trip" <| fun _ ->
                let event = Content_block_updated ("block-1", "Content", None, None, None)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_block_removed round-trip" <| fun _ ->
                let event = Content_block_removed "block-1"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_blocks_reordered round-trip" <| fun _ ->
                let event = Content_blocks_reordered ([ "block-3"; "block-1"; "block-2" ], None)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_blocks_reordered with session round-trip" <| fun _ ->
                let event = Content_blocks_reordered ([ "block-1" ], Some "session-42")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_blocks_row_grouped round-trip" <| fun _ ->
                let event = Content_blocks_row_grouped ("block-1", "block-2", "row-group-abc")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Content_block_row_ungrouped round-trip" <| fun _ ->
                let event = Content_block_row_ungrouped "block-1"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
