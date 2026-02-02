namespace Mediatheca.Server

open Thoth.Json.Net

module Friends =

    // Data records for events

    type FriendAddedData = {
        Name: string
        ImageRef: string option
    }

    type FriendUpdatedData = {
        Name: string
        ImageRef: string option
    }

    // Events

    type FriendEvent =
        | FriendAdded of FriendAddedData
        | FriendUpdated of FriendUpdatedData
        | FriendRemoved

    // State

    type ActiveFriend = {
        Name: string
        ImageRef: string option
    }

    type FriendState =
        | NotCreated
        | Active of ActiveFriend
        | Removed

    // Commands

    type FriendCommand =
        | AddFriend of name: string * imageRef: string option
        | UpdateFriend of name: string * imageRef: string option
        | RemoveFriend

    // Evolve

    let evolve (state: FriendState) (event: FriendEvent) : FriendState =
        match state, event with
        | NotCreated, FriendAdded data ->
            Active { Name = data.Name; ImageRef = data.ImageRef }
        | Active _, FriendUpdated data ->
            Active { Name = data.Name; ImageRef = data.ImageRef }
        | Active _, FriendRemoved -> Removed
        | _ -> state

    let reconstitute (events: FriendEvent list) : FriendState =
        List.fold evolve NotCreated events

    // Decide

    let decide (state: FriendState) (command: FriendCommand) : Result<FriendEvent list, string> =
        match state, command with
        | NotCreated, AddFriend (name, imageRef) ->
            Ok [ FriendAdded { Name = name; ImageRef = imageRef } ]
        | Active _, AddFriend _ ->
            Error "Friend already exists"
        | Active _, UpdateFriend (name, imageRef) ->
            Ok [ FriendUpdated { Name = name; ImageRef = imageRef } ]
        | NotCreated, UpdateFriend _ ->
            Error "Friend does not exist"
        | Active _, RemoveFriend ->
            Ok [ FriendRemoved ]
        | NotCreated, RemoveFriend ->
            Error "Friend does not exist"
        | Removed, _ ->
            Error "Friend has been removed"

    // Stream ID

    let streamId (slug: string) = sprintf "Friend-%s" slug

    // Serialization

    module Serialization =

        let private encodeFriendAddedData (data: FriendAddedData) =
            Encode.object [
                "name", Encode.string data.Name
                "imageRef", Encode.option Encode.string data.ImageRef
            ]

        let private decodeFriendAddedData: Decoder<FriendAddedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
            })

        let private encodeFriendUpdatedData (data: FriendUpdatedData) =
            Encode.object [
                "name", Encode.string data.Name
                "imageRef", Encode.option Encode.string data.ImageRef
            ]

        let private decodeFriendUpdatedData: Decoder<FriendUpdatedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
            })

        let serialize (event: FriendEvent) : string * string =
            match event with
            | FriendAdded data ->
                "FriendAdded", Encode.toString 0 (encodeFriendAddedData data)
            | FriendUpdated data ->
                "FriendUpdated", Encode.toString 0 (encodeFriendUpdatedData data)
            | FriendRemoved ->
                "FriendRemoved", "{}"

        let deserialize (eventType: string) (data: string) : FriendEvent option =
            match eventType with
            | "FriendAdded" ->
                Decode.fromString decodeFriendAddedData data
                |> Result.toOption
                |> Option.map FriendAdded
            | "FriendUpdated" ->
                Decode.fromString decodeFriendUpdatedData data
                |> Result.toOption
                |> Option.map FriendUpdated
            | "FriendRemoved" ->
                Some FriendRemoved
            | _ -> None

        let toEventData (event: FriendEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : FriendEvent option =
            deserialize storedEvent.EventType storedEvent.Data
