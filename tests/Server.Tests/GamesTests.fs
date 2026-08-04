module Mediatheca.Tests.GamesTests

open Expecto
open Mediatheca.Server.Games
open Mediatheca.Shared

let private sampleGameData: GameAddedData = {
    Name = "The Witcher 3"
    Year = 2015
    Genres = [ "RPG"; "Action" ]
    Description = "An open-world RPG about a monster hunter"
    ShortDescription = "Monster hunting RPG"
    WebsiteUrl = Some "https://thewitcher.com"
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
                    Expect.isTrue (Set.isEmpty game.FamilyOwners) "FamilyOwners should be empty"
                    Expect.isTrue (Set.isEmpty game.RecommendedBy) "RecommendedBy should be empty"
                    Expect.isTrue (Set.isEmpty game.WantToPlayWith) "WantToPlayWith should be empty"
                    Expect.isTrue (Set.isEmpty game.PlayedWith) "PlayedWith should be empty"
                    Expect.equal game.SteamLibraryDate None "SteamLibraryDate should default to None"
                    Expect.equal game.SteamLastPlayed None "SteamLastPlayed should default to None"
                    Expect.isFalse game.IsOwnedByMe "IsOwnedByMe should default to false"
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
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Change_status Abandoned)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.Status Abandoned "Status should be Abandoned"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Transition Backlog to InFocus" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Change_status InFocus)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.Status InFocus "Status should be InFocus"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Transition InFocus to Retired" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_status_changed InFocus ]
                            (Change_status Retired)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_status_changed InFocus ] @ events)
                match state with
                | Active game -> Expect.equal game.Status Retired "Status should be Retired"
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

        testCase "Adding a different family owner is not idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_family_owner_added "marco" ]
                            (Add_family_owner "sophie")
            match result with
            | Ok events -> Expect.equal (List.length events) 1 "A different friend slug should produce one event"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Reconstitute yields identical state with and without a duplicate family owner event" <| fun _ ->
            let withoutDuplicate = reconstitute [ Game_added_to_library sampleGameData; Game_family_owner_added "marco" ]
            let withDuplicate = reconstitute [ Game_added_to_library sampleGameData; Game_family_owner_added "marco"; Game_family_owner_added "marco" ]
            Expect.equal withDuplicate withoutDuplicate "State should be identical regardless of duplicate Game_family_owner_added events"

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
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Set_hltb_hours (Some 50.5, Some 80.0, Some 120.0))
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

        testCase "Setting a different steam app id is not idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_steam_app_id_set 292030 ]
                            (Set_steam_app_id 292031)
            match result with
            | Ok events -> Expect.equal (List.length events) 1 "A different appId should produce one event"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Reconstitute yields identical state with and without a duplicate steam app id event" <| fun _ ->
            let withoutDuplicate = reconstitute [ Game_added_to_library sampleGameData; Game_steam_app_id_set 292030 ]
            let withDuplicate = reconstitute [ Game_added_to_library sampleGameData; Game_steam_app_id_set 292030; Game_steam_app_id_set 292030 ]
            Expect.equal withDuplicate withoutDuplicate "State should be identical regardless of duplicate Game_steam_app_id_set events"

        testCase "Marking a game as owned" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] Mark_as_owned
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.isTrue game.IsOwnedByMe "IsOwnedByMe should be true"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Already-owned game is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_marked_as_owned ]
                            Mark_as_owned
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing ownership" <| fun _ ->
            let result = givenWhenThen
                            [ Game_added_to_library sampleGameData; Game_marked_as_owned ]
                            Remove_ownership
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Game_added_to_library sampleGameData; Game_marked_as_owned ] @ events)
                match state with
                | Active game -> Expect.isFalse game.IsOwnedByMe "IsOwnedByMe should be false"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing when not owned is idempotent" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] Remove_ownership
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
                Change_status Retired
                Set_hltb_hours (Some 10.0, None, None)
                Add_family_owner "marco"
                Remove_family_owner "marco"
                Recommend_game "marco"
                Remove_recommendation "marco"
                Add_want_to_play_with "sarah"
                Remove_from_want_to_play_with "sarah"
                Add_played_with "marco"
                Remove_played_with "marco"
                Set_steam_app_id 292030
                Record_prior_play_time 3600
                Record_play_session ("2024-06-01", 60)
                Correct_play_session_minutes ("2024-06-01", 90)
                Move_play_session ("2024-06-01", "2024-06-02")
                Remove_play_session "2024-06-01"
                Reconcile_steam_observed_total 3600
                Record_steam_observed_total (3600, "2024-06-01")
                Set_short_description "A short desc"
                Set_website_url (Some "https://example.com")
                Add_play_mode "Co-op"
                Remove_play_mode "Co-op"
                Set_steam_library_date (Some "2024-01-15")
                Set_steam_last_played (Some "2024-06-20")
                Mark_as_owned
                Remove_ownership
                Override_play_facets { noPlayFacetsOverride with Solo = Some true }
            ]
            for cmd in commands do
                let result = givenWhenThen removedEvents cmd
                match result with
                | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                | Ok _ -> failtest $"Expected error for command on removed game: {cmd}"
    ]

/// games-a7dqx (ADR-0053): the manual play-facets override — one event
/// carrying the whole all-`Option` record, equality-checked no-op in
/// `decide`, cache-blind by construction (no read path into
/// game_metadata_cache from this module at all).
[<Tests>]
let playFacetsOverrideTests =
    testList "Games play facets override (ADR-0053)" [

        testCase "A new game defaults to no override on every facet" <| fun _ ->
            let state = applyEvents [ Game_added_to_library sampleGameData ]
            match state with
            | Active game -> Expect.equal game.PlayFacetsOverride noPlayFacetsOverride "Fresh game defers to the cache on every facet"
            | _ -> failtest "Expected Active state"

        testCase "Overriding a facet for the first time produces an event" <| fun _ ->
            let ovr = { noPlayFacetsOverride with Solo = Some true; Vr = Some VrOnly }
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Override_play_facets ovr)
            match result with
            | Ok events ->
                Expect.equal events [ Game_play_facets_overridden ovr ] "Should produce exactly the override event"
                let state = applyEvents ([ Game_added_to_library sampleGameData ] @ events)
                match state with
                | Active game -> Expect.equal game.PlayFacetsOverride ovr "ActiveGame.PlayFacetsOverride should hold the new override"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Some false is a real, distinct override — not treated as unset" <| fun _ ->
            let ovr = { noPlayFacetsOverride with CoopOnline = Some false }
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Override_play_facets ovr)
            match result with
            | Ok events -> Expect.equal events [ Game_play_facets_overridden ovr ] "Some false must still produce an event, distinct from None"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Re-sending an identical override is a no-op" <| fun _ ->
            let ovr = { noPlayFacetsOverride with Solo = Some true }
            let given = [ Game_added_to_library sampleGameData; Game_play_facets_overridden ovr ]
            let result = givenWhenThen given (Override_play_facets ovr)
            match result with
            | Ok events -> Expect.equal events [] "Identical override should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Sending an all-None record un-overrides every facet in one command, no separate clear command needed" <| fun _ ->
            let ovr = { noPlayFacetsOverride with Solo = Some true; CoopOnline = Some false }
            let given = [ Game_added_to_library sampleGameData; Game_play_facets_overridden ovr ]
            let result = givenWhenThen given (Override_play_facets noPlayFacetsOverride)
            match result with
            | Ok events ->
                Expect.equal events [ Game_play_facets_overridden noPlayFacetsOverride ] "Clearing is the same event shape, just an all-None payload"
                let state = applyEvents (given @ events)
                match state with
                | Active game -> Expect.equal game.PlayFacetsOverride noPlayFacetsOverride "Every facet defers to the cache again"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "A redundant-but-harmless override (matching what the cache would say) is accepted, not refused — cache-blind by construction" <| fun _ ->
            // The aggregate has no read path into game_metadata_cache, so it
            // cannot know whether this override happens to match the cache
            // default — and must not refuse it on that basis (ADR-0053).
            let ovr = { noPlayFacetsOverride with Solo = Some true }
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Override_play_facets ovr)
            match result with
            | Ok events -> Expect.equal (List.length events) 1 "A first-time override is accepted regardless of what any cache might say"
            | Error e -> failtest $"Expected success but got: {e}"
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
            let event = Game_status_changed Retired
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Legacy 'Playing' status payload deserializes to InFocus" <| fun _ ->
            // Task 048: the Playing case was removed from GameStatus; events / projection rows
            // that still contain the literal string "Playing" must fold into InFocus on read.
            let legacyData = """{"status":"Playing"}"""
            let deserialized = Serialization.deserialize "Game_status_changed" legacyData
            Expect.equal deserialized (Some (Game_status_changed InFocus)) "Legacy Playing should map to InFocus"

        testCase "Legacy 'OnHold' status payload deserializes to InFocus" <| fun _ ->
            // games-status-vocabulary-reconcile: OnHold was removed from GameStatus; events /
            // projection rows that still contain the literal string "OnHold" must upcast to
            // InFocus on read (no event rewriting).
            let legacyData = """{"status":"OnHold"}"""
            let deserialized = Serialization.deserialize "Game_status_changed" legacyData
            Expect.equal deserialized (Some (Game_status_changed InFocus)) "Legacy OnHold should map to InFocus"

        testCase "Legacy 'Completed' status payload deserializes to Retired" <| fun _ ->
            // games-status-vocabulary-reconcile: Completed was renamed Retired; events /
            // projection rows that still contain the literal string "Completed" must upcast
            // to Retired on read (no event rewriting).
            let legacyData = """{"status":"Completed"}"""
            let deserialized = Serialization.deserialize "Game_status_changed" legacyData
            Expect.equal deserialized (Some (Game_status_changed Retired)) "Legacy Completed should map to Retired"

        testCase "Game_status_changed InFocus round-trips" <| fun _ ->
            let event = Game_status_changed InFocus
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_status_changed Dismissed round-trips" <| fun _ ->
            let event = Game_status_changed Dismissed
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

        testCase "Prior_play_time_recorded round-trips" <| fun _ ->
            let event = Prior_play_time_recorded 30000
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Play_session_recorded round-trips (SteamSync)" <| fun _ ->
            let event = Play_session_recorded { Day = "2024-06-01"; Minutes = 120; Source = SteamSync }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Play_session_recorded round-trips (Manual)" <| fun _ ->
            let event = Play_session_recorded { Day = "2024-06-01"; Minutes = 60; Source = Manual }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Play_session_minutes_corrected round-trips" <| fun _ ->
            let event = Play_session_minutes_corrected ("2024-06-01", 90, 60)
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Play_session_moved round-trips" <| fun _ ->
            let event = Play_session_moved ("2024-06-01", "2024-06-02", 60)
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Play_session_removed round-trips" <| fun _ ->
            let event = Play_session_removed ("2024-06-01", 60)
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Steam_observed_total_reconciled round-trips" <| fun _ ->
            let event = Steam_observed_total_reconciled 2952
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_steam_library_date_set round-trips (Some)" <| fun _ ->
            let event = Game_steam_library_date_set (Some "2024-01-15")
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_steam_library_date_set round-trips (None)" <| fun _ ->
            let event = Game_steam_library_date_set None
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_steam_last_played_set round-trips (Some)" <| fun _ ->
            let event = Game_steam_last_played_set (Some "2024-06-20")
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_steam_last_played_set round-trips (None)" <| fun _ ->
            let event = Game_steam_last_played_set None
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_marked_as_owned round-trips" <| fun _ ->
            let event = Game_marked_as_owned
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_ownership_removed round-trips" <| fun _ ->
            let event = Game_ownership_removed
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_play_facets_overridden round-trips (all facets set, including Some false)" <| fun _ ->
            let event = Game_play_facets_overridden {
                Solo = Some true
                CoopCouch = Some false
                CoopOnline = Some true
                VersusCouch = Some false
                VersusOnline = Some true
                RemotePlayTogether = Some false
                Vr = Some VrOnly
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_play_facets_overridden round-trips (all-None, the 'un-override everything' payload)" <| fun _ ->
            let event = Game_play_facets_overridden noPlayFacetsOverride
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Game_play_facets_overridden round-trips (Some NoVr — a real, distinct statement, not absence)" <| fun _ ->
            let event = Game_play_facets_overridden { noPlayFacetsOverride with Vr = Some NoVr }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip, and Vr must decode as Some NoVr, not None"

        testCase "All event types serialize and deserialize" <| fun _ ->
            let events: GameEvent list = [
                Game_added_to_library sampleGameData
                Game_removed_from_library
                Game_categorized [ "RPG"; "Indie" ]
                Game_cover_replaced "posters/new.jpg"
                Game_backdrop_replaced "backdrops/new.jpg"
                Game_personal_rating_set (Some 4)
                Game_personal_rating_set None
                Game_status_changed InFocus
                Game_status_changed Retired
                Game_status_changed Abandoned
                Game_status_changed Backlog
                Game_status_changed Dismissed
                Game_hltb_hours_set (Some 50.5, Some 80.0, Some 120.0)
                Game_hltb_hours_set (None, None, None)
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
                Prior_play_time_recorded 30000
                Play_session_recorded { Day = "2024-06-01"; Minutes = 120; Source = SteamSync }
                Play_session_recorded { Day = "2024-06-02"; Minutes = 60; Source = Manual }
                Play_session_minutes_corrected ("2024-06-01", 90, 60)
                Play_session_moved ("2024-06-01", "2024-06-02", 60)
                Play_session_removed ("2024-06-01", 60)
                Steam_observed_total_reconciled 2952
                Game_short_description_set "A short description"
                Game_website_url_set (Some "https://example.com")
                Game_website_url_set None
                Game_play_mode_added "Co-op"
                Game_play_mode_removed "Co-op"
                Game_steam_library_date_set (Some "2024-01-15")
                Game_steam_library_date_set None
                Game_steam_last_played_set (Some "2024-06-20")
                Game_steam_last_played_set None
                Game_marked_as_owned
                Game_ownership_removed
                Game_play_facets_overridden { noPlayFacetsOverride with Solo = Some true; Vr = Some VrSupported }
                Game_play_facets_overridden noPlayFacetsOverride
            ]
            for event in events do
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) $"Should round-trip: {eventType}"
    ]

/// games-p6vkz: play sessions and pre-tracking playtime as first-class Games
/// events. `Record_steam_observed_total` is the whole Steam-sync policy as
/// one pure decision (see its doc comment in Games.fs); the two-fold design
/// (TotalPlayTimeMinutes vs. SteamObservedMinutes) is what makes the sync
/// cursor (`steam_playtime_snapshot`) derivable rather than merely guardable
/// — the phantom-session regression below is the reason why.
[<Tests>]
let playSessionDecideTests =
    testList "Games play sessions" [

        testCase "Record_steam_observed_total on an unseen game above the threshold records prior playtime only" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Record_steam_observed_total (30000, "2024-06-01"))
            match result with
            | Ok events -> Expect.equal events [ Prior_play_time_recorded 30000 ] "Should emit exactly one Prior_play_time_recorded event, no session, no promotion"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_steam_observed_total on an unseen game at or under the threshold records a dated session and promotes" <| fun _ ->
            let result = givenWhenThen [ Game_added_to_library sampleGameData ] (Record_steam_observed_total (180, "2024-06-01"))
            match result with
            | Ok events ->
                Expect.equal events
                    [ Play_session_recorded { Day = "2024-06-01"; Minutes = 180; Source = SteamSync }; Game_status_changed InFocus ]
                    "Should emit a dated session plus promotion (default status is Backlog)"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_steam_observed_total boundary: 960 minutes is a session, 961 minutes is prior playtime" <| fun _ ->
            match givenWhenThen [ Game_added_to_library sampleGameData ] (Record_steam_observed_total (960, "2024-06-01")) with
            | Ok events ->
                Expect.isTrue
                    (events |> List.exists (function Play_session_recorded d -> d.Minutes = 960 | _ -> false))
                    "960 minutes (at the threshold) should record a session, not prior playtime"
            | Error e -> failtest $"Expected success but got: {e}"

            match givenWhenThen [ Game_added_to_library sampleGameData ] (Record_steam_observed_total (961, "2024-06-01")) with
            | Ok events -> Expect.equal events [ Prior_play_time_recorded 961 ] "961 minutes (above the threshold) should record prior playtime"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_steam_observed_total after prior playtime records only the delta as a session" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Prior_play_time_recorded 30000 ]
            match givenWhenThen given (Record_steam_observed_total (30120, "2024-06-02")) with
            | Ok events ->
                Expect.equal events
                    [ Play_session_recorded { Day = "2024-06-02"; Minutes = 120; Source = SteamSync }; Game_status_changed InFocus ]
                    "Should emit exactly a 120-minute session (30120 - 30000)"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "phantom-session regression: removing a Steam-sourced session doesn't get silently re-added by the next sync" <| fun _ ->
            // 509 prior + sessions summing to 2443 (1000 + 773 + 670) = 2952 total,
            // matching what Steam has always reported. Removing the 670-minute
            // session drops TotalPlayTimeMinutes to 2282, but SteamObservedMinutes
            // — computed as originally recorded, never reduced by a later removal
            // — stays at 2952. A sync reporting 2952 again must emit nothing.
            let given =
                [ Game_added_to_library sampleGameData
                  Prior_play_time_recorded 509
                  Play_session_recorded { Day = "2024-01-01"; Minutes = 1000; Source = SteamSync }
                  Play_session_recorded { Day = "2024-01-02"; Minutes = 773; Source = SteamSync }
                  Play_session_recorded { Day = "2024-01-03"; Minutes = 670; Source = SteamSync }
                  Play_session_removed ("2024-01-03", 670) ]
            let state = applyEvents given
            match state with
            | Active game ->
                Expect.equal game.TotalPlayTimeMinutes 2282 "Total should be 509 + 1000 + 773 = 2282 after the removal"
                Expect.equal game.SteamObservedMinutes 2952 "SteamObservedMinutes should stay at 509 + 2443 = 2952, unaffected by the removal"
                match decide state (Record_steam_observed_total (2952, "2024-01-04")) with
                | Ok events -> Expect.equal events [] "A subsequent sync reporting the same total Steam has always reported must emit nothing"
                | Error e -> failtest $"Expected success but got: {e}"
            | _ -> failtest "Expected Active state"

        testCase "Steam_observed_total_reconciled repairs a desynced cursor without touching the recorded total" <| fun _ ->
            let given =
                [ Game_added_to_library sampleGameData
                  Play_session_recorded { Day = "2024-01-01"; Minutes = 2282; Source = SteamSync } ]
            let state = applyEvents given
            match decide state (Reconcile_steam_observed_total 2952) with
            | Ok events ->
                Expect.equal events [ Steam_observed_total_reconciled 2952 ] "Should emit the reconciliation event"
                let state2 = evolve state (List.head events)
                match state2 with
                | Active game ->
                    Expect.equal game.TotalPlayTimeMinutes 2282 "TotalPlayTimeMinutes must stay at 2282"
                    Expect.equal game.SteamObservedMinutes 2952 "SteamObservedMinutes should now be 2952"
                    match decide state2 (Record_steam_observed_total (2952, "2024-01-02")) with
                    | Ok followUp -> Expect.equal followUp [] "A following sync reporting the same total should emit nothing"
                    | Error e -> failtest $"Expected success but got: {e}"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_play_time on a game that already has prior playtime returns Error" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Prior_play_time_recorded 500 ]
            match givenWhenThen given (Record_prior_play_time 200) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an error — prior playtime is recorded once per game"

        testCase "decide rejects zero or negative minutes on Record_play_session" <| fun _ ->
            match givenWhenThen [ Game_added_to_library sampleGameData ] (Record_play_session ("2024-06-01", 0)) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an error for zero minutes"

        testCase "decide rejects zero or negative minutes on Correct_play_session_minutes" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Play_session_recorded { Day = "2024-06-01"; Minutes = 60; Source = Manual } ]
            match givenWhenThen given (Correct_play_session_minutes ("2024-06-01", 0)) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an error for zero minutes — correcting to 0 is refused, use remove"

        testCase "Record_play_session on a Retired game promotes to InFocus" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Game_status_changed Retired ]
            match givenWhenThen given (Record_play_session ("2024-06-01", 60)) with
            | Ok events ->
                Expect.equal events
                    [ Play_session_recorded { Day = "2024-06-01"; Minutes = 60; Source = Manual }; Game_status_changed InFocus ]
                    "Should promote from Retired"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_play_session on an InFocus game emits only the session event" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Game_status_changed InFocus ]
            match givenWhenThen given (Record_play_session ("2024-06-01", 60)) with
            | Ok events ->
                Expect.equal events
                    [ Play_session_recorded { Day = "2024-06-01"; Minutes = 60; Source = Manual } ]
                    "Should not re-promote an already-InFocus game"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "correct, move, remove, and recording prior playtime never promote, in any status" <| fun _ ->
            let statuses = [ Backlog; InFocus; Retired; Abandoned; Dismissed ]
            let emittedPromotion (events: GameEvent list) =
                events |> List.exists (function Game_status_changed _ -> true | _ -> false)
            for status in statuses do
                let baseEvents =
                    [ Game_added_to_library sampleGameData
                      Game_status_changed status
                      Play_session_recorded { Day = "2024-06-01"; Minutes = 60; Source = Manual } ]

                match givenWhenThen baseEvents (Correct_play_session_minutes ("2024-06-01", 90)) with
                | Ok events -> Expect.isFalse (emittedPromotion events) $"Correct should not promote from {status}"
                | Error e -> failtest $"Expected success but got: {e}"

                match givenWhenThen baseEvents (Move_play_session ("2024-06-01", "2024-06-02")) with
                | Ok events -> Expect.isFalse (emittedPromotion events) $"Move should not promote from {status}"
                | Error e -> failtest $"Expected success but got: {e}"

                match givenWhenThen baseEvents (Remove_play_session "2024-06-01") with
                | Ok events -> Expect.isFalse (emittedPromotion events) $"Remove should not promote from {status}"
                | Error e -> failtest $"Expected success but got: {e}"

                let priorEvents = [ Game_added_to_library sampleGameData; Game_status_changed status ]
                match givenWhenThen priorEvents (Record_prior_play_time 500) with
                | Ok events -> Expect.isFalse (emittedPromotion events) $"Recording prior playtime should not promote from {status}"
                | Error e -> failtest $"Expected success but got: {e}"

        testCase "correct, move, and remove against a nonexistent session return Error" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData ]
            match givenWhenThen given (Correct_play_session_minutes ("2024-06-01", 90)) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for correct against a nonexistent session"
            match givenWhenThen given (Move_play_session ("2024-06-01", "2024-06-02")) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for move against a nonexistent session"
            match givenWhenThen given (Remove_play_session "2024-06-01") with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for remove against a nonexistent session"

        testCase "Games.evolve on Game_play_time_set is a no-op (legacy, superseded)" <| fun _ ->
            let given = [ Game_added_to_library sampleGameData; Prior_play_time_recorded 500 ]
            let stateBefore = applyEvents given
            let stateAfter = applyEvents (given @ [ Game_play_time_set 999999 ])
            Expect.equal stateAfter stateBefore "Replaying Game_play_time_set must not change state"
    ]
