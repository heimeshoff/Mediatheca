namespace Mediatheca.Server

open Thoth.Json.Net

module ContentBlocks =

    // Data records for events

    type ContentBlockData = {
        BlockId: string
        BlockType: string  // "text", "image", "link"
        Content: string
        ImageRef: string option
        Url: string option
        Caption: string option
    }

    // Events

    type ContentBlockEvent =
        | Content_block_added of ContentBlockData * position: int * sessionId: string option
        | Content_block_updated of blockId: string * content: string * imageRef: string option * url: string option * caption: string option
        | Content_block_removed of blockId: string
        | Content_blocks_reordered of blockIds: string list * sessionId: string option

    // State

    type BlockState = {
        BlockId: string
        BlockType: string
        Content: string
        ImageRef: string option
        Url: string option
        Caption: string option
        Position: int
        SessionId: string option
    }

    type ContentBlocksState = {
        Blocks: Map<string, BlockState>
    }
        with static member empty = { Blocks = Map.empty }

    // Commands

    type ContentBlockCommand =
        | Add_content_block of ContentBlockData * sessionId: string option
        | Update_content_block of blockId: string * content: string * imageRef: string option * url: string option * caption: string option
        | Remove_content_block of blockId: string
        | Reorder_content_blocks of blockIds: string list * sessionId: string option

    // Evolve

    let evolve (state: ContentBlocksState) (event: ContentBlockEvent) : ContentBlocksState =
        match event with
        | Content_block_added (data, pos, sid) ->
            let block: BlockState = {
                BlockId = data.BlockId
                BlockType = data.BlockType
                Content = data.Content
                ImageRef = data.ImageRef
                Url = data.Url
                Caption = data.Caption
                Position = pos
                SessionId = sid
            }
            { state with Blocks = state.Blocks |> Map.add data.BlockId block }
        | Content_block_updated (bid, content, imgRef, url, caption) ->
            match state.Blocks |> Map.tryFind bid with
            | Some block ->
                let updated = { block with Content = content; ImageRef = imgRef; Url = url; Caption = caption }
                { state with Blocks = state.Blocks |> Map.add bid updated }
            | None -> state
        | Content_block_removed bid ->
            { state with Blocks = state.Blocks |> Map.remove bid }
        | Content_blocks_reordered (bids, sid) ->
            let updatedBlocks =
                bids
                |> List.mapi (fun i bid ->
                    match state.Blocks |> Map.tryFind bid with
                    | Some block -> Some (bid, { block with Position = i })
                    | None -> None)
                |> List.choose id
                |> List.fold (fun m (k, v) -> Map.add k v m) state.Blocks
            { state with Blocks = updatedBlocks }

    let reconstitute (events: ContentBlockEvent list) : ContentBlocksState =
        List.fold evolve ContentBlocksState.empty events

    // Decide

    let decide (state: ContentBlocksState) (command: ContentBlockCommand) : Result<ContentBlockEvent list, string> =
        match command with
        | Add_content_block (data, sid) ->
            if state.Blocks |> Map.containsKey data.BlockId then
                Error $"Block with id '{data.BlockId}' already exists"
            else
                let maxPos =
                    state.Blocks
                    |> Map.values
                    |> Seq.filter (fun b -> b.SessionId = sid)
                    |> Seq.map (fun b -> b.Position)
                    |> Seq.fold max -1
                let position = maxPos + 1
                Ok [ Content_block_added (data, position, sid) ]
        | Update_content_block (bid, content, imgRef, url, caption) ->
            if state.Blocks |> Map.containsKey bid then
                Ok [ Content_block_updated (bid, content, imgRef, url, caption) ]
            else
                Error $"Block with id '{bid}' does not exist"
        | Remove_content_block bid ->
            if state.Blocks |> Map.containsKey bid then
                Ok [ Content_block_removed bid ]
            else
                Ok []
        | Reorder_content_blocks (bids, sid) ->
            let allExist = bids |> List.forall (fun bid -> state.Blocks |> Map.containsKey bid)
            if not allExist then
                Error "One or more block ids do not exist"
            else
                Ok [ Content_blocks_reordered (bids, sid) ]

    // Stream ID

    let streamId (movieSlug: string) = sprintf "ContentBlocks-%s" movieSlug

    // Serialization

    module Serialization =

        let private encodeContentBlockData (data: ContentBlockData) =
            Encode.object [
                "blockId", Encode.string data.BlockId
                "blockType", Encode.string data.BlockType
                "content", Encode.string data.Content
                "imageRef", Encode.option Encode.string data.ImageRef
                "url", Encode.option Encode.string data.Url
                "caption", Encode.option Encode.string data.Caption
            ]

        let private decodeContentBlockData: Decoder<ContentBlockData> =
            Decode.object (fun get -> {
                BlockId = get.Required.Field "blockId" Decode.string
                BlockType = get.Required.Field "blockType" Decode.string
                Content = get.Required.Field "content" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
                Url = get.Optional.Field "url" Decode.string
                Caption = get.Optional.Field "caption" Decode.string
            })

        let serialize (event: ContentBlockEvent) : string * string =
            match event with
            | Content_block_added (data, position, sessionId) ->
                "Content_block_added", Encode.toString 0 (Encode.object [
                    "data", encodeContentBlockData data
                    "position", Encode.int position
                    "sessionId", Encode.option Encode.string sessionId
                ])
            | Content_block_updated (blockId, content, imageRef, url, caption) ->
                "Content_block_updated", Encode.toString 0 (Encode.object [
                    "blockId", Encode.string blockId
                    "content", Encode.string content
                    "imageRef", Encode.option Encode.string imageRef
                    "url", Encode.option Encode.string url
                    "caption", Encode.option Encode.string caption
                ])
            | Content_block_removed blockId ->
                "Content_block_removed", Encode.toString 0 (Encode.object [ "blockId", Encode.string blockId ])
            | Content_blocks_reordered (blockIds, sessionId) ->
                "Content_blocks_reordered", Encode.toString 0 (Encode.object [
                    "blockIds", blockIds |> List.map Encode.string |> Encode.list
                    "sessionId", Encode.option Encode.string sessionId
                ])

        let deserialize (eventType: string) (data: string) : ContentBlockEvent option =
            match eventType with
            | "Content_block_added" ->
                let decoder =
                    Decode.object (fun get ->
                        let d = get.Required.Field "data" decodeContentBlockData
                        let pos = get.Required.Field "position" Decode.int
                        let sid = get.Optional.Field "sessionId" Decode.string
                        Content_block_added (d, pos, sid))
                Decode.fromString decoder data
                |> Result.toOption
            | "Content_block_updated" ->
                let decoder =
                    Decode.object (fun get ->
                        let blockId = get.Required.Field "blockId" Decode.string
                        let content = get.Required.Field "content" Decode.string
                        let imageRef = get.Optional.Field "imageRef" Decode.string
                        let url = get.Optional.Field "url" Decode.string
                        let caption = get.Optional.Field "caption" Decode.string
                        Content_block_updated (blockId, content, imageRef, url, caption))
                Decode.fromString decoder data
                |> Result.toOption
            | "Content_block_removed" ->
                Decode.fromString (Decode.field "blockId" Decode.string) data
                |> Result.toOption
                |> Option.map Content_block_removed
            | "Content_blocks_reordered" ->
                let decoder =
                    Decode.object (fun get ->
                        let blockIds = get.Required.Field "blockIds" (Decode.list Decode.string)
                        let sessionId = get.Optional.Field "sessionId" Decode.string
                        Content_blocks_reordered (blockIds, sessionId))
                Decode.fromString decoder data
                |> Result.toOption
            | _ -> None

        let toEventData (event: ContentBlockEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : ContentBlockEvent option =
            deserialize storedEvent.EventType storedEvent.Data
