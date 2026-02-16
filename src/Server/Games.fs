namespace Mediatheca.Server

open Thoth.Json.Net
open Mediatheca.Shared

module Games =

    // Data records for events

    type GameAddedData = {
        Name: string
        Year: int
        Genres: string list
        Description: string
        CoverRef: string option
        BackdropRef: string option
        RawgId: int option
        RawgRating: float option
    }

    // Events

    type GameEvent =
        | Game_added_to_library of GameAddedData
        | Game_removed_from_library
        | Game_categorized of genres: string list
        | Game_cover_replaced of coverRef: string
        | Game_backdrop_replaced of backdropRef: string
        | Game_personal_rating_set of rating: int option
        | Game_status_changed of GameStatus
        | Game_hltb_hours_set of hours: float option
        | Game_store_added of store: string
        | Game_store_removed of store: string
        | Game_family_owner_added of friendSlug: string
        | Game_family_owner_removed of friendSlug: string
        | Game_recommended_by of friendSlug: string
        | Game_recommendation_removed of friendSlug: string
        | Want_to_play_with of friendSlug: string
        | Removed_want_to_play_with of friendSlug: string
        | Game_played_with of friendSlug: string
        | Game_played_with_removed of friendSlug: string
        | Game_steam_app_id_set of steamAppId: int
        | Game_play_time_set of totalMinutes: int

    // State

    type ActiveGame = {
        Name: string
        Year: int
        Genres: string list
        Description: string
        CoverRef: string option
        BackdropRef: string option
        RawgId: int option
        RawgRating: float option
        HltbHours: float option
        PersonalRating: int option
        Status: GameStatus
        SteamAppId: int option
        TotalPlayTimeMinutes: int
        Stores: Set<string>
        FamilyOwners: Set<string>
        RecommendedBy: Set<string>
        WantToPlayWith: Set<string>
        PlayedWith: Set<string>
    }

    type GameState =
        | Not_created
        | Active of ActiveGame
        | Removed

    // Commands

    type GameCommand =
        | Add_game of GameAddedData
        | Remove_game
        | Categorize_game of genres: string list
        | Replace_cover of coverRef: string
        | Replace_backdrop of backdropRef: string
        | Set_personal_rating of rating: int option
        | Change_status of GameStatus
        | Set_hltb_hours of hours: float option
        | Add_store of store: string
        | Remove_store of store: string
        | Add_family_owner of friendSlug: string
        | Remove_family_owner of friendSlug: string
        | Recommend_game of friendSlug: string
        | Remove_recommendation of friendSlug: string
        | Add_want_to_play_with of friendSlug: string
        | Remove_from_want_to_play_with of friendSlug: string
        | Add_played_with of friendSlug: string
        | Remove_played_with of friendSlug: string
        | Set_steam_app_id of steamAppId: int
        | Set_play_time of totalMinutes: int

    // Evolve

    let evolve (state: GameState) (event: GameEvent) : GameState =
        match state, event with
        | Not_created, Game_added_to_library data ->
            Active {
                Name = data.Name
                Year = data.Year
                Genres = data.Genres
                Description = data.Description
                CoverRef = data.CoverRef
                BackdropRef = data.BackdropRef
                RawgId = data.RawgId
                RawgRating = data.RawgRating
                HltbHours = None
                PersonalRating = None
                Status = Backlog
                SteamAppId = None
                TotalPlayTimeMinutes = 0
                Stores = Set.empty
                FamilyOwners = Set.empty
                RecommendedBy = Set.empty
                WantToPlayWith = Set.empty
                PlayedWith = Set.empty
            }
        | Active _, Game_removed_from_library -> Removed
        | Active game, Game_categorized genres ->
            Active { game with Genres = genres }
        | Active game, Game_cover_replaced coverRef ->
            Active { game with CoverRef = Some coverRef }
        | Active game, Game_backdrop_replaced backdropRef ->
            Active { game with BackdropRef = Some backdropRef }
        | Active game, Game_personal_rating_set rating ->
            Active { game with PersonalRating = rating }
        | Active game, Game_status_changed status ->
            Active { game with Status = status }
        | Active game, Game_hltb_hours_set hours ->
            Active { game with HltbHours = hours }
        | Active game, Game_store_added store ->
            Active { game with Stores = game.Stores |> Set.add store }
        | Active game, Game_store_removed store ->
            Active { game with Stores = game.Stores |> Set.remove store }
        | Active game, Game_family_owner_added friendSlug ->
            Active { game with FamilyOwners = game.FamilyOwners |> Set.add friendSlug }
        | Active game, Game_family_owner_removed friendSlug ->
            Active { game with FamilyOwners = game.FamilyOwners |> Set.remove friendSlug }
        | Active game, Game_recommended_by friendSlug ->
            Active { game with RecommendedBy = game.RecommendedBy |> Set.add friendSlug }
        | Active game, Game_recommendation_removed friendSlug ->
            Active { game with RecommendedBy = game.RecommendedBy |> Set.remove friendSlug }
        | Active game, Want_to_play_with friendSlug ->
            Active { game with WantToPlayWith = game.WantToPlayWith |> Set.add friendSlug }
        | Active game, Removed_want_to_play_with friendSlug ->
            Active { game with WantToPlayWith = game.WantToPlayWith |> Set.remove friendSlug }
        | Active game, Game_played_with friendSlug ->
            Active { game with PlayedWith = game.PlayedWith |> Set.add friendSlug }
        | Active game, Game_played_with_removed friendSlug ->
            Active { game with PlayedWith = game.PlayedWith |> Set.remove friendSlug }
        | Active game, Game_steam_app_id_set steamAppId ->
            Active { game with SteamAppId = Some steamAppId }
        | Active game, Game_play_time_set totalMinutes ->
            Active { game with TotalPlayTimeMinutes = totalMinutes }
        | _ -> state

    let reconstitute (events: GameEvent list) : GameState =
        List.fold evolve Not_created events

    // Decide

    let decide (state: GameState) (command: GameCommand) : Result<GameEvent list, string> =
        match state, command with
        | Not_created, Add_game data ->
            Ok [ Game_added_to_library data ]
        | Active _, Add_game _ ->
            Error "Game already exists in library"
        | Active _, Remove_game ->
            Ok [ Game_removed_from_library ]
        | Not_created, Remove_game ->
            Error "Game does not exist"
        | Active game, Categorize_game genres ->
            if game.Genres = genres then Ok []
            else Ok [ Game_categorized genres ]
        | Active _, Replace_cover coverRef ->
            Ok [ Game_cover_replaced coverRef ]
        | Active _, Replace_backdrop backdropRef ->
            Ok [ Game_backdrop_replaced backdropRef ]
        | Active game, Set_personal_rating rating ->
            if game.PersonalRating = rating then Ok []
            else Ok [ Game_personal_rating_set rating ]
        | Active game, Change_status status ->
            if game.Status = status then Ok []
            else Ok [ Game_status_changed status ]
        | Active game, Set_hltb_hours hours ->
            if game.HltbHours = hours then Ok []
            else Ok [ Game_hltb_hours_set hours ]
        | Active game, Add_store store ->
            if game.Stores |> Set.contains store then Ok []
            else Ok [ Game_store_added store ]
        | Active game, Remove_store store ->
            if game.Stores |> Set.contains store then
                Ok [ Game_store_removed store ]
            else Ok []
        | Active game, Add_family_owner friendSlug ->
            if game.FamilyOwners |> Set.contains friendSlug then Ok []
            else Ok [ Game_family_owner_added friendSlug ]
        | Active game, Remove_family_owner friendSlug ->
            if game.FamilyOwners |> Set.contains friendSlug then
                Ok [ Game_family_owner_removed friendSlug ]
            else Ok []
        | Active game, Recommend_game friendSlug ->
            if game.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ Game_recommended_by friendSlug ]
        | Active game, Remove_recommendation friendSlug ->
            if game.RecommendedBy |> Set.contains friendSlug then
                Ok [ Game_recommendation_removed friendSlug ]
            else Ok []
        | Active game, Add_want_to_play_with friendSlug ->
            if game.WantToPlayWith |> Set.contains friendSlug then Ok []
            else Ok [ Want_to_play_with friendSlug ]
        | Active game, Remove_from_want_to_play_with friendSlug ->
            if game.WantToPlayWith |> Set.contains friendSlug then
                Ok [ Removed_want_to_play_with friendSlug ]
            else Ok []
        | Active game, Add_played_with friendSlug ->
            if game.PlayedWith |> Set.contains friendSlug then Ok []
            else Ok [ Game_played_with friendSlug ]
        | Active game, Remove_played_with friendSlug ->
            if game.PlayedWith |> Set.contains friendSlug then
                Ok [ Game_played_with_removed friendSlug ]
            else Ok []
        | Active game, Set_steam_app_id steamAppId ->
            if game.SteamAppId = Some steamAppId then Ok []
            else Ok [ Game_steam_app_id_set steamAppId ]
        | Active game, Set_play_time totalMinutes ->
            if game.TotalPlayTimeMinutes = totalMinutes then Ok []
            else Ok [ Game_play_time_set totalMinutes ]
        | Removed, _ ->
            Error "Game has been removed"
        | Not_created, _ ->
            Error "Game does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Game-%s" slug

    // Serialization

    module Serialization =

        let private encodeGameAddedData (data: GameAddedData) =
            Encode.object [
                "name", Encode.string data.Name
                "year", Encode.int data.Year
                "genres", data.Genres |> List.map Encode.string |> Encode.list
                "description", Encode.string data.Description
                "coverRef", Encode.option Encode.string data.CoverRef
                "backdropRef", Encode.option Encode.string data.BackdropRef
                "rawgId", Encode.option Encode.int data.RawgId
                "rawgRating", Encode.option Encode.float data.RawgRating
            ]

        let private decodeGameAddedData: Decoder<GameAddedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Year = get.Required.Field "year" Decode.int
                Genres = get.Required.Field "genres" (Decode.list Decode.string)
                Description = get.Required.Field "description" Decode.string
                CoverRef = get.Optional.Field "coverRef" Decode.string
                BackdropRef = get.Optional.Field "backdropRef" Decode.string
                RawgId = get.Optional.Field "rawgId" Decode.int
                RawgRating = get.Optional.Field "rawgRating" Decode.float
            })

        let private encodeGameStatus (status: GameStatus) =
            match status with
            | Backlog -> "Backlog"
            | Playing -> "Playing"
            | Completed -> "Completed"
            | Abandoned -> "Abandoned"
            | OnHold -> "OnHold"

        let private decodeGameStatus (s: string) : GameStatus =
            match s with
            | "Backlog" -> Backlog
            | "Playing" -> Playing
            | "Completed" -> Completed
            | "Abandoned" -> Abandoned
            | "OnHold" -> OnHold
            | _ -> Backlog

        let serialize (event: GameEvent) : string * string =
            match event with
            | Game_added_to_library data ->
                "Game_added_to_library", Encode.toString 0 (encodeGameAddedData data)
            | Game_removed_from_library ->
                "Game_removed_from_library", "{}"
            | Game_categorized genres ->
                "Game_categorized", Encode.toString 0 (Encode.object [ "genres", genres |> List.map Encode.string |> Encode.list ])
            | Game_cover_replaced coverRef ->
                "Game_cover_replaced", Encode.toString 0 (Encode.object [ "coverRef", Encode.string coverRef ])
            | Game_backdrop_replaced backdropRef ->
                "Game_backdrop_replaced", Encode.toString 0 (Encode.object [ "backdropRef", Encode.string backdropRef ])
            | Game_personal_rating_set rating ->
                "Game_personal_rating_set", Encode.toString 0 (Encode.object [ "rating", Encode.option Encode.int rating ])
            | Game_status_changed status ->
                "Game_status_changed", Encode.toString 0 (Encode.object [ "status", Encode.string (encodeGameStatus status) ])
            | Game_hltb_hours_set hours ->
                "Game_hltb_hours_set", Encode.toString 0 (Encode.object [ "hours", Encode.option Encode.float hours ])
            | Game_store_added store ->
                "Game_store_added", Encode.toString 0 (Encode.object [ "store", Encode.string store ])
            | Game_store_removed store ->
                "Game_store_removed", Encode.toString 0 (Encode.object [ "store", Encode.string store ])
            | Game_family_owner_added friendSlug ->
                "Game_family_owner_added", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_family_owner_removed friendSlug ->
                "Game_family_owner_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_recommended_by friendSlug ->
                "Game_recommended_by", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_recommendation_removed friendSlug ->
                "Game_recommendation_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Want_to_play_with friendSlug ->
                "Want_to_play_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Removed_want_to_play_with friendSlug ->
                "Removed_want_to_play_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_played_with friendSlug ->
                "Game_played_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_played_with_removed friendSlug ->
                "Game_played_with_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_steam_app_id_set steamAppId ->
                "Game_steam_app_id_set", Encode.toString 0 (Encode.object [ "steamAppId", Encode.int steamAppId ])
            | Game_play_time_set totalMinutes ->
                "Game_play_time_set", Encode.toString 0 (Encode.object [ "totalMinutes", Encode.int totalMinutes ])

        let deserialize (eventType: string) (data: string) : GameEvent option =
            match eventType with
            | "Game_added_to_library" ->
                Decode.fromString decodeGameAddedData data
                |> Result.toOption
                |> Option.map Game_added_to_library
            | "Game_removed_from_library" ->
                Some Game_removed_from_library
            | "Game_categorized" ->
                Decode.fromString (Decode.field "genres" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map Game_categorized
            | "Game_cover_replaced" ->
                Decode.fromString (Decode.field "coverRef" Decode.string) data
                |> Result.toOption
                |> Option.map Game_cover_replaced
            | "Game_backdrop_replaced" ->
                Decode.fromString (Decode.field "backdropRef" Decode.string) data
                |> Result.toOption
                |> Option.map Game_backdrop_replaced
            | "Game_personal_rating_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "rating" Decode.int)) data
                |> Result.toOption
                |> Option.map Game_personal_rating_set
            | "Game_status_changed" ->
                Decode.fromString (Decode.field "status" Decode.string) data
                |> Result.toOption
                |> Option.map (decodeGameStatus >> Game_status_changed)
            | "Game_hltb_hours_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "hours" Decode.float)) data
                |> Result.toOption
                |> Option.map Game_hltb_hours_set
            | "Game_store_added" ->
                Decode.fromString (Decode.field "store" Decode.string) data
                |> Result.toOption
                |> Option.map Game_store_added
            | "Game_store_removed" ->
                Decode.fromString (Decode.field "store" Decode.string) data
                |> Result.toOption
                |> Option.map Game_store_removed
            | "Game_family_owner_added" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_family_owner_added
            | "Game_family_owner_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_family_owner_removed
            | "Game_recommended_by" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_recommended_by
            | "Game_recommendation_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_recommendation_removed
            | "Want_to_play_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Want_to_play_with
            | "Removed_want_to_play_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Removed_want_to_play_with
            | "Game_played_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_played_with
            | "Game_played_with_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_played_with_removed
            | "Game_steam_app_id_set" ->
                Decode.fromString (Decode.field "steamAppId" Decode.int) data
                |> Result.toOption
                |> Option.map Game_steam_app_id_set
            | "Game_play_time_set" ->
                Decode.fromString (Decode.field "totalMinutes" Decode.int) data
                |> Result.toOption
                |> Option.map Game_play_time_set
            | _ -> None

        let toEventData (event: GameEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : GameEvent option =
            deserialize storedEvent.EventType storedEvent.Data
