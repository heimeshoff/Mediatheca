module Mediatheca.Tests.GamesTests

open Expecto
open Mediatheca.Server.Games
open Mediatheca.Shared

let private sampleGameData: GameAddedData = {
    Name = "The Witcher 3"
    Year = 2015
    Genres = [ "RPG"; "Action" ]
    Description = "An open-world RPG about a monster hunter"
    CoverRef = Some "posters/game-the-witcher-3-2015.jpg"
    BackdropRef = Some "backdrops/game-the-witcher-3-2015.jpg"
    RawgId = Some 3328
    RawgRating = Some 4.66
}

let private givenWhenThen (given: GameEvent list) (command: GameCommand) =
    let state = reconstitute given
    decide state command

let private applyEvents (events: GameEvent list) =
    reconstitute events

[<Tests>]
let gameTests =
    testList "Games" [

        testCase "Adding a game creates it with correct state" <| fun _ ->
            let result = givenWhenThen [] (Add_game sampleGameData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents events
                match state with
                | Active game ->
                    Expect.equal game.Name "The Witcher 3" "Name should match"
                    Expect.equal game.Year 2015 "Year should match"
                    Expect.equal game.Description "An open-world RPG about a monster hunter" "Description should match"
                    Expect.equal game.Genres [ "RPG"; "Action" ] "Genres should match"
                    Expect.equal game.RawgId (Some 3328) "RawgId should match"
                    Expect.equal game.Status Backlog "Status should default to Backlog"
                    Expect.equal game.PersonalRating None "PersonalRating should default to None"
                    Expect.equal game.HltbHours None "HltbHours should default to None"
                    Expect.equal game.SteamAppId None "SteamAppId should default to None"
                    Expect.equal game.TotalPlayTimeMinutes 0 "TotalPlayTimeMinutes should default to 0"
                    Expect.isTrue (Set.isEmpty game.Stores) "Stores should be empty"
                    Expect.isTrue (Set.isEmpty game.FamilyOwners) "FamilyOwners should be empty"
                    Expect.isTrue (Set.isEmpty game.RecommendedBy) "RecommendedBy should be empty"
                    Expect.isTrue (Set.isEmpty game.WantToPlayWith) "WantToPlayWith should be empty"
                    Expect.isTrue (Set.isEmpty game.PlayedWith) "PlayedWith should be empty"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot add a game that already exists" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Add_game sampleGameData)
            match result with
            | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
            | Ok _ -> failtest "Expected error"

        testCase "Cannot add to a removed game" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_removed_from_library ]
                            (Add_game sampleGameData)
            match result with
            | Error msg -> Expect.stringContains msg "removed" "Should say removed"
            | Ok _ -> failtest "Expected error"

        testCase "Removing a game" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] Remove_game
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                Expect.equal events.[0] Game_removed_from_library "Should be Game_removed_from_library"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                Expect.equal state Removed "State should be Removed"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot remove a game that doesn't exist" <| fun _ ->
            let result = givenWhenThen [] Remove_game
            match result with
            | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
            | Ok _ -> failtest "Expected error"

        testCase "Changing game status" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Change_status Playing)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.Status Playing "Status should be Playing"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Same status is idempotent" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Change_status Backlog)
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting personal rating" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Set_personal_rating (Some 5))
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.PersonalRating (Some 5) "Personal rating should be 5"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Clearing personal rating" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Game_personal_rating_set (Some 5) ]
            let result = givenWhenThen given (Set_personal_rating None)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents (given @ events)
                match state with
                | Active game -> Expect.equal game.PersonalRating None "Personal rating should be None"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding a store" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Add_store "Steam")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue (game.Stores |> Set.contains "Steam") "Steam should be in stores"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding same store is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_store_added "Steam" ]
                            (Add_store "Steam")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing a store" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_store_added "Steam" ]
                            (Remove_store "Steam")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_store_added "Steam" ] @ events)
                match state with
                | Active game -> Expect.isFalse (game.Stores |> Set.contains "Steam") "Steam should be removed"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing non-existent store produces no events" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Remove_store "Steam")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding a family owner" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Add_family_owner "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue (game.FamilyOwners |> Set.contains "marco") "marco should be a family owner"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding same family owner is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_family_owner_added "marco" ]
                            (Add_family_owner "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing a family owner" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_family_owner_added "marco" ]
                            (Remove_family_owner "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_family_owner_added "marco" ] @ events)
                match state with
                | Active game -> Expect.isFalse (game.FamilyOwners |> Set.contains "marco") "marco should be removed"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing non-existent family owner produces no events" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Remove_family_owner "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Recommending a game" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Recommend_game "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue (game.RecommendedBy |> Set.contains "marco") "marco should be in RecommendedBy"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Duplicate recommendation is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_recommended_by "marco" ]
                            (Recommend_game "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing recommendation" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_recommended_by "marco" ]
                            (Remove_recommendation "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_recommended_by "marco" ] @ events)
                match state with
                | Active game -> Expect.isFalse (game.RecommendedBy |> Set.contains "marco") "marco should be removed"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing non-existent recommendation produces no events" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Remove_recommendation "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Want to play with" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Add_want_to_play_with "sarah")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue (game.WantToPlayWith |> Set.contains "sarah") "sarah should be in WantToPlayWith"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing want to play with" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Want_to_play_with "sarah" ]
                            (Remove_from_want_to_play_with "sarah")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Want_to_play_with "sarah" ] @ events)
                match state with
                | Active game -> Expect.isFalse (game.WantToPlayWith |> Set.contains "sarah") "sarah should be removed"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding played with" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Add_played_with "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue (game.PlayedWith |> Set.contains "marco") "marco should be in PlayedWith"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Adding same played with is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_played_with "marco" ]
                            (Add_played_with "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing played with" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_played_with "marco" ]
                            (Remove_played_with "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_played_with "marco" ] @ events)
                match state with
                | Active game -> Expect.isFalse (game.PlayedWith |> Set.contains "marco") "marco should be removed"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing non-existent played with produces no events" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Remove_played_with "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting HLTB hours" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Set_hltb_hours (Some 50.5))
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.HltbHours (Some 50.5) "HltbHours should be 50.5"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting steam app id" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Set_steam_app_id 292030)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.SteamAppId (Some 292030) "SteamAppId should be 292030"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting same steam app id is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_steam_app_id_set 292030 ]
                            (Set_steam_app_id 292030)
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting play time" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Set_play_time 3600)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.TotalPlayTimeMinutes 3600 "TotalPlayTimeMinutes should be 3600"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting same play time is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_play_time_set 3600 ]
                            (Set_play_time 3600)
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Commands on removed game fail" <| fun _ ->
            let removedEvents = [ Game_added_to_library sampleGameData; Game_removed_from_library ]
            let commands: GameCommand list = [
                Add_game sampleGameData
                Remove_game
                Categorize_game [ "Drama" ]
                Replace_cover "x"
                Replace_backdrop "x"
                Set_personal_rating (Some 3)
                Change_status Playing
                Set_hltb_hours (Some 10.0)
                Add_store "Steam"
                Remove_store "Steam"
                Add_family_owner "marco"
                Remove_family_owner "marco"
                Recommend_game "marco"
                Remove_recommendation "marco"
                Add_want_to_play_with "sarah"
                Remove_from_want_to_play_with "sarah"
                Add_played_with "marco"
                Remove_played_with "marco"
                Set_steam_app_id 292030
                Set_play_time 3600
            ]
            for cmd in commands do
                let result = givenWhenThen removedEvents cmd
                match result with
                | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                | Ok _ -> failtest $"Expected error for command on removed game: {cmd}"
    ]

[<Tests>]
let gameSerializationTests =
    testList "Games Serialization" [

        testCase "Game_added_to_library round-trips" <| fun _ ->
            let event = Game_added_to_library sampleGameData
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_removed_from_library round-trips" <| fun _ ->
            let event = Game_removed_from_library
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_categorized round-trips" <| fun _ ->
            let event = Game_categorized [ "RPG"; "Indie" ]
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_status_changed round-trips" <| fun _ ->
            let event = Game_status_changed Playing
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_played_with round-trips" <| fun _ ->
            let event = Game_played_with "marco"
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_played_with_removed round-trips" <| fun _ ->
            let event = Game_played_with_removed "marco"
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_steam_app_id_set round-trips" <| fun _ ->
            let event = Game_steam_app_id_set 292030
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_play_time_set round-trips" <| fun _ ->
            let event = Game_play_time_set 3600
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "All event types serialize and deserialize" <| fun _ ->
            let events: GameEvent list = [
                Game_added_to_library sampleGameData
                Game_removed_from_library
                Game_categorized [ "RPG"; "Indie" ]
                Game_cover_replaced "posters/new.jpg"
                Game_backdrop_replaced "backdrops/new.jpg"
                Game_personal_rating_set (Some 4)
                Game_personal_rating_set None
                Game_status_changed Playing
                Game_status_changed Completed
                Game_status_changed Abandoned
                Game_status_changed OnHold
                Game_status_changed Backlog
                Game_hltb_hours_set (Some 50.5)
                Game_hltb_hours_set None
                Game_store_added "Steam"
                Game_store_removed "Steam"
                Game_family_owner_added "marco"
                Game_family_owner_removed "marco"
                Game_recommended_by "sarah"
                Game_recommendation_removed "sarah"
                Want_to_play_with "marco"
                Removed_want_to_play_with "marco"
                Game_played_with "marco"
                Game_played_with_removed "marco"
                Game_steam_app_id_set 292030
                Game_play_time_set 3600
            ]
            for event in events do
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) $"Should round-trip: {eventType}"
    ]
