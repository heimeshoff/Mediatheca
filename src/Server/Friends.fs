namespace Mediatheca.Server

open Thoth.Json.Net

module Friends =

    // Data records for events

    type Friend_addedData = {
        Name: string
        ImageRef: string option
    }

    type Friend_updatedData = {
        Name: string
        ImageRef: string option
    }

    // Events

    type FriendEvent =
        | Friend_added of Friend_addedData
        | Friend_updated of Friend_updatedData
        | Friend_removed

    // State

    type ActiveFriend = {
        Name: string
        ImageRef: string option
    }

    type FriendState =
        | Not_created
        | Active of ActiveFriend
        | Removed

    // Commands

    type FriendCommand =
        | Add_friend of name: string * imageRef: string option
        | Update_friend of name: string * imageRef: string option
        | Remove_friend

    // Evolve

    let evolve (state: FriendState) (event: FriendEvent) : FriendState =
        match state, event with
        | Not_created, Friend_added data ->
            Active { Name = data.Name; ImageRef = data.ImageRef }
        | Active _, Friend_updated data ->
            Active { Name = data.Name; ImageRef = data.ImageRef }
        | Active _, Friend_removed -> Removed
        | _ -> state

    let reconstitute (events: FriendEvent list) : FriendState =
        List.fold evolve Not_created events

    // Decide

    let decide (state: FriendState) (command: FriendCommand) : Result<FriendEvent list, string> =
        match state, command with
        | Not_created, Add_friend (name, imageRef) ->
            Ok [ Friend_added { Name = name; ImageRef = imageRef } ]
        | Active _, Add_friend _ ->
            Error "Friend already exists"
        | Active _, Update_friend (name, imageRef) ->
            Ok [ Friend_updated { Name = name; ImageRef = imageRef } ]
        | Not_created, Update_friend _ ->
            Error "Friend does not exist"
        | Active _, Remove_friend ->
            Ok [ Friend_removed ]
        | Not_created, Remove_friend ->
            Error "Friend does not exist"
        | Removed, _ ->
            Error "Friend has been removed"

    // Stream ID

    let streamId (slug: string) = sprintf "Friend-%s" slug

    // Serialization

    module Serialization =

        let private encodeFriend_addedData (data: Friend_addedData) =
            Encode.object [
                "name", Encode.string data.Name
                "imageRef", Encode.option Encode.string data.ImageRef
            ]

        let private decodeFriend_addedData: Decoder<Friend_addedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
            })

        let private encodeFriend_updatedData (data: Friend_updatedData) =
            Encode.object [
                "name", Encode.string data.Name
                "imageRef", Encode.option Encode.string data.ImageRef
            ]

        let private decodeFriend_updatedData: Decoder<Friend_updatedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                ImageRef = get.Optional.Field "imageRef" Decode.string
            })

        let serialize (event: FriendEvent) : string * string =
            match event with
            | Friend_added data ->
                "Friend_added", Encode.toString 0 (encodeFriend_addedData data)
            | Friend_updated data ->
                "Friend_updated", Encode.toString 0 (encodeFriend_updatedData data)
            | Friend_removed ->
                "Friend_removed", "{}"

        let deserialize (eventType: string) (data: string) : FriendEvent option =
            match eventType with
            | "Friend_added" ->
                Decode.fromString decodeFriend_addedData data
                |> Result.toOption
                |> Option.map Friend_added
            | "Friend_updated" ->
                Decode.fromString decodeFriend_updatedData data
                |> Result.toOption
                |> Option.map Friend_updated
            | "Friend_removed" ->
                Some Friend_removed
            | _ -> None

        let toEventData (event: FriendEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : FriendEvent option =
            deserialize storedEvent.EventType storedEvent.Data
