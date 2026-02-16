module Mediatheca.Tests.SeriesTests

open Expecto
open Mediatheca.Server.Series

let private mkEpisode (num: int) : EpisodeImportData = {
    EpisodeNumber = num
    Name = $"Episode {num}"
    Overview = $"Overview for episode {num}"
    Runtime = Some 45
    AirDate = Some "2024-01-01"
    StillRef = None
    TmdbRating = Some 7.5
}

let private mkSeason (num: int) (episodeCount: int) : SeasonImportData = {
    SeasonNumber = num
    Name = $"Season {num}"
    Overview = $"Overview for season {num}"
    PosterRef = None
    AirDate = Some "2024-01-01"
    Episodes = [ for i in 1..episodeCount -> mkEpisode i ]
}

let private sampleSeriesData: SeriesAddedData = {
    Name = "Breaking Bad"
    Year = 2008
    Overview = "A chemistry teacher turned drug lord"
    Genres = [ "Drama"; "Crime" ]
    Status = "Ended"
    PosterRef = Some "posters/breaking-bad.jpg"
    BackdropRef = Some "backdrops/breaking-bad.jpg"
    TmdbId = 1396
    TmdbRating = Some 8.9
    EpisodeRuntime = Some 47
    Seasons = [ mkSeason 1 3; mkSeason 2 3 ]
}

let private givenWhenThen (given: SeriesEvent list) (command: SeriesCommand) =
    let state = reconstitute given
    decide state command

let private applyEvents (events: SeriesEvent list) =
    reconstitute events

[<Tests>]
let seriesTests =
    testList "Series" [

        testCase "Adding a series creates it with correct state" <| fun _ ->
            let result = givenWhenThen [] (Add_series_to_library sampleSeriesData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents events
                match state with
                | Active series ->
                    Expect.equal series.Name "Breaking Bad" "Name should match"
                    Expect.equal series.Year 2008 "Year should match"
                    Expect.equal series.Overview "A chemistry teacher turned drug lord" "Overview should match"
                    Expect.equal series.Genres [ "Drama"; "Crime" ] "Genres should match"
                    Expect.equal series.TmdbId 1396 "TmdbId should match"
                    Expect.isTrue (series.RewatchSessions |> Map.containsKey "default") "Should have a default rewatch session"
                    let defaultSession = series.RewatchSessions |> Map.find "default"
                    Expect.isTrue defaultSession.IsDefault "Default session should be marked as default"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot add a series that already exists" <| fun _ ->
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Add_series_to_library sampleSeriesData)
            match result with
            | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
            | Ok _ -> failtest "Expected error"

        testCase "Cannot add to a removed series" <| fun _ ->
            let result = givenWhenThen
                            [ Series_added_to_library sampleSeriesData; Series_removed_from_library ]
                            (Add_series_to_library sampleSeriesData)
            match result with
            | Error msg -> Expect.stringContains msg "removed" "Should say removed"
            | Ok _ -> failtest "Expected error"

        testCase "Removing a series" <| fun _ ->
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] Remove_series
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                Expect.equal events.[0] Series_removed_from_library "Should be Series_removed_from_library"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                Expect.equal state Removed "State should be Removed"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot remove a series that doesn't exist" <| fun _ ->
            let result = givenWhenThen [] Remove_series
            match result with
            | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
            | Ok _ -> failtest "Expected error"

        testCase "Setting personal rating" <| fun _ ->
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Set_series_personal_rating (Some 9))
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series -> Expect.equal series.PersonalRating (Some 9) "Personal rating should be 9"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Recommending a series" <| fun _ ->
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Recommend_series "marco")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series -> Expect.isTrue (series.RecommendedBy |> Set.contains "marco") "marco should be in RecommendedBy"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Duplicate recommendation is idempotent" <| fun _ ->
            let result = givenWhenThen
                            [ Series_added_to_library sampleSeriesData; Series_recommended_by "marco" ]
                            (Recommend_series "marco")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Want to watch with" <| fun _ ->
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Want_to_watch_series_with "sarah")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series -> Expect.isTrue (series.WantToWatchWith |> Set.contains "sarah") "sarah should be in WantToWatchWith"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Creating a rewatch session" <| fun _ ->
            let sessionData: RewatchSessionCreatedData = {
                RewatchId = "rewatch-1"
                Name = Some "Second Watch"
                FriendSlugs = [ "marco" ]
            }
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Create_rewatch_session sessionData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series ->
                    Expect.isTrue (series.RewatchSessions |> Map.containsKey "rewatch-1") "Should have rewatch-1 session"
                    let session = series.RewatchSessions |> Map.find "rewatch-1"
                    Expect.equal session.Name (Some "Second Watch") "Session name should match"
                    Expect.isTrue (session.Friends |> Set.contains "marco") "marco should be in session friends"
                    Expect.isFalse session.IsDefault "Non-default session should not be marked as default"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot create rewatch session on non-existent series" <| fun _ ->
            let sessionData: RewatchSessionCreatedData = {
                RewatchId = "rewatch-1"
                Name = None
                FriendSlugs = []
            }
            let result = givenWhenThen [] (Create_rewatch_session sessionData)
            match result with
            | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
            | Ok _ -> failtest "Expected error"

        testCase "Marking episode watched" <| fun _ ->
            let watchData: EpisodeWatchedData = {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 2
                Date = "2025-03-15"
            }
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Mark_episode_watched watchData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series ->
                    let session = series.RewatchSessions |> Map.find "default"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 2)) "S1E2 should be in WatchedEpisodes"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Marking episode unwatched" <| fun _ ->
            let watchData: EpisodeWatchedData = {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 2
                Date = "2025-03-15"
            }
            let unwatchData: EpisodeUnwatchedData = {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 2
            }
            let given = [
                Series_added_to_library sampleSeriesData
                Episode_watched watchData
            ]
            let result = givenWhenThen given (Mark_episode_unwatched unwatchData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents (given @ events)
                match state with
                | Active series ->
                    let session = series.RewatchSessions |> Map.find "default"
                    Expect.isFalse (session.WatchedEpisodes |> Set.contains (1, 2)) "S1E2 should be removed from WatchedEpisodes"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Marking season watched" <| fun _ ->
            let seasonData: SeasonMarkedWatchedData = {
                RewatchId = "default"
                SeasonNumber = 1
                Date = "2025-03-15"
            }
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Mark_season_watched seasonData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series ->
                    let session = series.RewatchSessions |> Map.find "default"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 1)) "S1E1 should be in WatchedEpisodes"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 2)) "S1E2 should be in WatchedEpisodes"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 3)) "S1E3 should be in WatchedEpisodes"
                    Expect.isFalse (session.WatchedEpisodes |> Set.contains (2, 1)) "S2E1 should NOT be in WatchedEpisodes"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Mark episodes watched up to" <| fun _ ->
            let upToData: EpisodesWatchedUpToData = {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 3
                Date = "2025-03-15"
            }
            let result = givenWhenThen [ Series_added_to_library sampleSeriesData ] (Mark_episodes_watched_up_to upToData)
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents ([ Series_added_to_library sampleSeriesData ] @ events)
                match state with
                | Active series ->
                    let session = series.RewatchSessions |> Map.find "default"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 1)) "S1E1 should be in WatchedEpisodes"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 2)) "S1E2 should be in WatchedEpisodes"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (1, 3)) "S1E3 should be in WatchedEpisodes"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Unmarking a season removes all watched episodes for that season" <| fun _ ->
            let given = [
                Series_added_to_library sampleSeriesData
                Season_marked_watched { RewatchId = "default"; SeasonNumber = 1; Date = "2025-03-15" }
                Episode_watched { RewatchId = "default"; SeasonNumber = 2; EpisodeNumber = 1; Date = "2025-03-15" }
            ]
            let result = givenWhenThen given (Mark_season_unwatched { RewatchId = "default"; SeasonNumber = 1 })
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                let state = applyEvents (given @ events)
                match state with
                | Active series ->
                    let session = series.RewatchSessions |> Map.find "default"
                    Expect.isFalse (session.WatchedEpisodes |> Set.contains (1, 1)) "S1E1 should be removed"
                    Expect.isFalse (session.WatchedEpisodes |> Set.contains (1, 2)) "S1E2 should be removed"
                    Expect.isFalse (session.WatchedEpisodes |> Set.contains (1, 3)) "S1E3 should be removed"
                    Expect.isTrue (session.WatchedEpisodes |> Set.contains (2, 1)) "S2E1 should still be watched"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Changing episode watched date on a watched episode succeeds" <| fun _ ->
            let given = [
                Series_added_to_library sampleSeriesData
                Episode_watched { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 2; Date = "2025-03-15" }
            ]
            let result = givenWhenThen given (Change_episode_watched_date { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 2; Date = "2025-04-01" })
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                match events.[0] with
                | Episode_watched_date_changed data ->
                    Expect.equal data.Date "2025-04-01" "Date should be updated"
                | _ -> failtest "Expected Episode_watched_date_changed event"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Changing date on an unwatched episode fails" <| fun _ ->
            let given = [ Series_added_to_library sampleSeriesData ]
            let result = givenWhenThen given (Change_episode_watched_date { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 2; Date = "2025-04-01" })
            match result with
            | Error msg -> Expect.stringContains msg "not watched" "Should say not watched"
            | Ok _ -> failtest "Expected error"

        testCase "Setting default to another session emits event and flips IsDefault flags" <| fun _ ->
            let sessionData: RewatchSessionCreatedData = {
                RewatchId = "rewatch-1"
                Name = Some "Second Watch"
                FriendSlugs = []
            }
            let given = [
                Series_added_to_library sampleSeriesData
                Rewatch_session_created sessionData
            ]
            let result = givenWhenThen given (Set_default_rewatch_session "rewatch-1")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                match events.[0] with
                | Default_rewatch_session_changed id -> Expect.equal id "rewatch-1" "Should change to rewatch-1"
                | _ -> failtest "Expected Default_rewatch_session_changed event"
                let state = applyEvents (given @ events)
                match state with
                | Active series ->
                    let oldDefault = series.RewatchSessions |> Map.find "default"
                    Expect.isFalse oldDefault.IsDefault "Old default should no longer be default"
                    let newDefault = series.RewatchSessions |> Map.find "rewatch-1"
                    Expect.isTrue newDefault.IsDefault "New session should be default"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting default to already-default session is no-op" <| fun _ ->
            let given = [ Series_added_to_library sampleSeriesData ]
            let result = givenWhenThen given (Set_default_rewatch_session "default")
            match result with
            | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Setting default to non-existent session returns error" <| fun _ ->
            let given = [ Series_added_to_library sampleSeriesData ]
            let result = givenWhenThen given (Set_default_rewatch_session "non-existent")
            match result with
            | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
            | Ok _ -> failtest "Expected error"

        testCase "Cannot delete the new default session" <| fun _ ->
            let sessionData: RewatchSessionCreatedData = {
                RewatchId = "rewatch-1"
                Name = Some "Second Watch"
                FriendSlugs = []
            }
            let given = [
                Series_added_to_library sampleSeriesData
                Rewatch_session_created sessionData
                Default_rewatch_session_changed "rewatch-1"
            ]
            let result = givenWhenThen given (Remove_rewatch_session "rewatch-1")
            match result with
            | Error msg -> Expect.stringContains msg "default" "Should say cannot remove default"
            | Ok _ -> failtest "Expected error"

        testCase "Can delete old default after changing default" <| fun _ ->
            let sessionData: RewatchSessionCreatedData = {
                RewatchId = "rewatch-1"
                Name = Some "Second Watch"
                FriendSlugs = []
            }
            let given = [
                Series_added_to_library sampleSeriesData
                Rewatch_session_created sessionData
                Default_rewatch_session_changed "rewatch-1"
            ]
            let result = givenWhenThen given (Remove_rewatch_session "default")
            match result with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                match events.[0] with
                | Rewatch_session_removed id -> Expect.equal id "default" "Should remove the old default"
                | _ -> failtest "Expected Rewatch_session_removed event"
            | Error e -> failtest $"Expected success but got: {e}"
    ]

[<Tests>]
let seriesSerializationTests =
    testList "Series Serialization" [

        testCase "Series_added_to_library round-trips" <| fun _ ->
            let event = Series_added_to_library sampleSeriesData
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Series_removed_from_library round-trips" <| fun _ ->
            let event = Series_removed_from_library
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Episode_watched round-trips" <| fun _ ->
            let event = Episode_watched {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 3
                Date = "2025-03-15"
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Season_marked_watched round-trips" <| fun _ ->
            let event = Season_marked_watched {
                RewatchId = "default"
                SeasonNumber = 2
                Date = "2025-03-15"
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Rewatch_session_created round-trips" <| fun _ ->
            let event = Rewatch_session_created {
                RewatchId = "rewatch-2"
                Name = Some "Watch with friends"
                FriendSlugs = [ "marco"; "sarah" ]
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Season_marked_unwatched round-trips" <| fun _ ->
            let event = Season_marked_unwatched {
                RewatchId = "default"
                SeasonNumber = 2
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "Episode_watched_date_changed round-trips" <| fun _ ->
            let event = Episode_watched_date_changed {
                RewatchId = "default"
                SeasonNumber = 1
                EpisodeNumber = 3
                Date = "2025-04-01"
            }
            let eventType, data = Serialization.serialize event
            let deserialized = Serialization.deserialize eventType data
            Expect.equal deserialized (Some event) "Should round-trip"

        testCase "All event types serialize and deserialize" <| fun _ ->
            let events: SeriesEvent list = [
                Series_added_to_library sampleSeriesData
                Series_removed_from_library
                Series_categorized [ "Drama"; "Thriller" ]
                Series_poster_replaced "posters/new.jpg"
                Series_backdrop_replaced "backdrops/new.jpg"
                Series_recommended_by "marco"
                Series_recommendation_removed "marco"
                Series_want_to_watch_with "sarah"
                Series_removed_want_to_watch_with "sarah"
                Series_personal_rating_set (Some 8)
                Rewatch_session_created { RewatchId = "r1"; Name = None; FriendSlugs = [] }
                Rewatch_session_removed "r1"
                Default_rewatch_session_changed "r1"
                Rewatch_session_friend_added { RewatchId = "r1"; FriendSlug = "marco" }
                Rewatch_session_friend_removed { RewatchId = "r1"; FriendSlug = "marco" }
                Episode_watched { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 1; Date = "2025-01-01" }
                Episode_unwatched { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 1 }
                Season_marked_watched { RewatchId = "default"; SeasonNumber = 1; Date = "2025-01-01" }
                Episodes_watched_up_to { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 3; Date = "2025-01-01" }
                Season_marked_unwatched { RewatchId = "default"; SeasonNumber = 1 }
                Episode_watched_date_changed { RewatchId = "default"; SeasonNumber = 1; EpisodeNumber = 1; Date = "2025-04-01" }
            ]
            for event in events do
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) $"Should round-trip: {eventType}"
    ]
