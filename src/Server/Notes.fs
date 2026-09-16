namespace Mediatheca.Server

open Thoth.Json.Net
open Mediatheca.Shared

/// Notes (ADR-0080): a document stream, not an aggregate — there is no
/// cross-block invariant to protect, only "the user typed; store what they
/// typed." Built with the codebase's aggregate machinery (decide/evolve/
/// reconstitute/streamId/Serialization, matching the codebase's other
/// aggregate-shaped modules' file shape) purely because that machinery is
/// the cheapest path to free
/// expected-position concurrency, no-op handling, and projection catch-up —
/// not because the concept deserves aggregate ceremony. See the curation
/// README's "Document streams (not aggregates)" heading, not "Aggregates".
module Notes =

    // Events
    //
    // One event per debounced save, carrying the FULL current block list —
    // not a diff, not per-block events (the editor has no per-block save
    // path; synthesizing a diff server-side would invent precision the
    // write side doesn't have). The owner (MediaType, slug) is NOT
    // duplicated into the payload — it lives only in the stream id (see
    // `streamId` below) so there is exactly one place event-surgery tooling
    // could ever desync it from.

    type NotesEvent =
        | Notes_saved of blocks: JournalBlockDto list

    // State

    type NotesState = {
        Blocks: JournalBlockDto list
    }
        with static member empty = { Blocks = [] }

    // Commands

    type NotesCommand =
        | Save_notes of blocks: JournalBlockDto list

    // Evolve — last-snapshot-wins: each `Notes_saved` IS the whole document.

    let evolve (state: NotesState) (event: NotesEvent) : NotesState =
        match event with
        | Notes_saved blocks -> { Blocks = blocks }

    let reconstitute (events: NotesEvent list) : NotesState =
        List.fold evolve NotesState.empty events

    // Decide
    //
    // Two refusals only, both log/projection-integrity guards, never
    // document-shape validation (ADR-0080 decision 4): an orphan ParentId,
    // a column outside a columnList, an unrecognized BlockType — none of
    // that is refused here. The client's `Doc.normalize` already repairs
    // those permissively, and refusing a save inside a debounced autosave
    // editor would silently stop persisting the user's work.

    let decide (state: NotesState) (command: NotesCommand) : Result<NotesEvent list, string> =
        match command with
        | Save_notes blocks ->
            let ids = blocks |> List.map (fun b -> b.Id)
            let hasDuplicateIds = (ids |> List.length) <> (ids |> List.distinct |> List.length)
            if hasDuplicateIds then
                Error "Notes document contains two or more blocks with the same id"
            elif List.length blocks > 5000 then
                Error "Notes document exceeds the maximum of 5000 blocks"
            elif blocks = state.Blocks then
                // ADR-0080 decision 5: F# structural, order-sensitive
                // equality over the full block list — Position and list
                // order ARE the document's layout, so a reorder-only save
                // with identical content still produces an event.
                Ok []
            else
                Ok [ Notes_saved blocks ]

    // Stream ID
    //
    // `storageToken` is FROZEN and deliberately independent from both
    // `MediaType.routePrefix` (a UI concern free to change when the router
    // changes) and a DU `.ToString()` (free to change on a rename) — a
    // route rename must never orphan every Notes stream (ADR-0080
    // decision 3). Never derive this from anything else.

    let storageToken (mediaType: MediaType) : string =
        match mediaType with
        | Movie -> "movie"
        | Series -> "series"
        | Game -> "game"
        | Book -> "book"

    let streamId (mediaType: MediaType) (slug: string) : string =
        sprintf "Notes-%s-%s" (storageToken mediaType) slug

    /// The inverse of `streamId`. `None` for a stream id that isn't a
    /// well-formed `Notes-{token}-{slug}` id.
    let parseStreamId (streamId: string) : (MediaType * string) option =
        let tryToken (token: string) (mediaType: MediaType) =
            let prefix = sprintf "Notes-%s-" token
            if streamId.StartsWith(prefix) then
                Some (mediaType, streamId.Substring(prefix.Length))
            else
                None
        [ tryToken "movie" Movie
          tryToken "series" Series
          tryToken "game" Game
          tryToken "book" Book ]
        |> List.tryPick id

    // Serialization

    module Serialization =

        let private encodeBlock (b: JournalBlockDto) =
            Encode.object [
                "id", Encode.string b.Id
                "parentId", Encode.option Encode.string b.ParentId
                "blockType", Encode.string b.BlockType
                "content", Encode.string b.Content
                "checked", Encode.bool b.Checked
                "collapsed", Encode.bool b.Collapsed
                "language", Encode.option Encode.string b.Language
                "url", Encode.option Encode.string b.Url
                "imageRef", Encode.option Encode.string b.ImageRef
                "caption", Encode.option Encode.string b.Caption
                "position", Encode.int b.Position
                "width", Encode.float b.Width
            ]

        let private decodeBlock : Decoder<JournalBlockDto> =
            Decode.object (fun get -> {
                Id = get.Required.Field "id" Decode.string
                ParentId = get.Optional.Field "parentId" Decode.string
                BlockType = get.Required.Field "blockType" Decode.string
                Content = get.Required.Field "content" Decode.string
                Checked = get.Required.Field "checked" Decode.bool
                Collapsed = get.Required.Field "collapsed" Decode.bool
                Language = get.Optional.Field "language" Decode.string
                Url = get.Optional.Field "url" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
                Caption = get.Optional.Field "caption" Decode.string
                Position = get.Required.Field "position" Decode.int
                Width = get.Required.Field "width" Decode.float
            })

        let serialize (event: NotesEvent) : string * string =
            match event with
            | Notes_saved blocks ->
                "Notes_saved", Encode.toString 0 (Encode.object [
                    "blocks", blocks |> List.map encodeBlock |> Encode.list
                ])

        let deserialize (eventType: string) (data: string) : NotesEvent option =
            match eventType with
            | "Notes_saved" ->
                Decode.fromString (Decode.field "blocks" (Decode.list decodeBlock)) data
                |> Result.toOption
                |> Option.map Notes_saved
            | _ -> None

        /// Hand-maintained mirror of the `deserialize` match-arm strings
        /// above — see e.g. Movies.Serialization.handledEventTypes for
        /// the pattern this follows.
        let handledEventTypes : string list = [
            "Notes_saved"
        ]

        let toEventData (event: NotesEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : NotesEvent option =
            deserialize storedEvent.EventType storedEvent.Data
