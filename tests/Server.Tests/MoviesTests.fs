module Mediatheca.Tests.MoviesTests

open Expecto
open Mediatheca.Server.Movies

let private sampleMovieData: MovieAddedData = {
    Name = "The Matrix"
    Year = 1999
    Runtime = Some 136
    Overview = "A computer hacker learns about the true nature of reality"
    Genres = [ "Action"; "Sci-Fi" ]
    PosterRef = Some "posters/the-matrix-1999.jpg"
    BackdropRef = Some "backdrops/the-matrix-1999.jpg"
    TmdbId = 603
    TmdbRating = Some 8.7
}

let private givenWhenThen (given: MovieEvent list) (command: MovieCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let catalogTests =
    testList "Movies" [

        testList "Add_movie_to_library" [
            testCase "adding movie to empty library succeeds" <| fun _ ->
                let result = givenWhenThen [] (Add_movie_to_library sampleMovieData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Movie_added_to_library data ->
                        Expect.equal data.Name "The Matrix" "Name should match"
                        Expect.equal data.Year 1999 "Year should match"
                    | _ -> failtest "Expected Movie_added_to_library"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding movie that already exists fails" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Add_movie_to_library sampleMovieData)
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Remove_movie_from_library" [
            testCase "removing active movie produces Movie_removed_from_library" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] Remove_movie_from_library
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] Movie_removed_from_library "Should be Movie_removed_from_library"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent movie fails" <| fun _ ->
                let result = givenWhenThen [] Remove_movie_from_library
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Categorize_movie" [
            testCase "categorizing changes genres" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Categorize_movie [ "Drama"; "Thriller" ])
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Movie_categorized genres -> Expect.equal genres [ "Drama"; "Thriller" ] "Genres should match"
                    | _ -> failtest "Expected Movie_categorized"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "categorizing with same genres produces no events (idempotent)" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Categorize_movie [ "Action"; "Sci-Fi" ])
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Replace_poster" [
            testCase "replacing poster produces event" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Replace_poster "posters/new-poster.jpg")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Movie_poster_replaced ref -> Expect.equal ref "posters/new-poster.jpg" "Poster ref should match"
                    | _ -> failtest "Expected Movie_poster_replaced"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Replace_backdrop" [
            testCase "replacing backdrop produces event" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Replace_backdrop "backdrops/new-backdrop.jpg")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Movie_backdrop_replaced ref -> Expect.equal ref "backdrops/new-backdrop.jpg" "Backdrop ref should match"
                    | _ -> failtest "Expected Movie_backdrop_replaced"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Recommend_by" [
            testCase "recommending by friend adds to set" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Recommend_by "marco")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Movie_recommended_by slug -> Expect.equal slug "marco" "Friend slug should match"
                    | _ -> failtest "Expected Movie_recommended_by"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recommending same friend twice is idempotent" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Movie_recommended_by "marco" ]
                                (Recommend_by "marco")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Remove_recommendation" [
            testCase "removing existing recommendation produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Movie_recommended_by "marco" ]
                                (Remove_recommendation "marco")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Recommendation_removed slug -> Expect.equal slug "marco" "Friend slug should match"
                    | _ -> failtest "Expected Recommendation_removed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existing recommendation produces no events" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Remove_recommendation "marco")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Want_to_watch_with" [
            testCase "adding want-to-watch-with produces event" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Add_want_to_watch_with "sarah")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Want_to_watch_with slug -> Expect.equal slug "sarah" "Friend slug should match"
                    | _ -> failtest "Expected Want_to_watch_with"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding same friend twice is idempotent" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Want_to_watch_with "sarah" ]
                                (Add_want_to_watch_with "sarah")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing want-to-watch-with produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Want_to_watch_with "sarah" ]
                                (Remove_from_want_to_watch_with "sarah")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Removed_want_to_watch_with slug -> Expect.equal slug "sarah" "Friend slug should match"
                    | _ -> failtest "Expected Removed_want_to_watch_with"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existing want-to-watch-with produces no events" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Remove_from_want_to_watch_with "sarah")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Watch sessions" [
            testCase "recording watch session produces event" <| fun _ ->
                let sessionData: WatchSessionRecordedData = {
                    SessionId = System.Guid.NewGuid().ToString("N")
                    Date = "2025-01-15"
                    Duration = Some 136
                    FriendSlugs = [ "marco"; "sarah" ]
                }
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Record_watch_session sessionData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Watch_session_recorded data ->
                        Expect.equal data.SessionId sessionData.SessionId "SessionId should match"
                        Expect.equal data.Date "2025-01-15" "Date should match"
                        Expect.equal data.Duration (Some 136) "Duration should match"
                        Expect.equal data.FriendSlugs [ "marco"; "sarah" ] "FriendSlugs should match"
                    | _ -> failtest "Expected Watch_session_recorded"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recording watch session auto-removes friends from want-to-watch-with" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = [ "sarah" ]
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Want_to_watch_with "sarah"
                    Want_to_watch_with "marco"
                ]
                let result = givenWhenThen given (Record_watch_session sessionData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    // Apply events and check state
                    let state = reconstitute (given @ events)
                    match state with
                    | Active movie ->
                        Expect.isFalse (movie.Want_to_watch_with |> Set.contains "sarah") "Sarah should be removed from want-to-watch-with"
                        Expect.isTrue (movie.Want_to_watch_with |> Set.contains "marco") "Marco should still be in want-to-watch-with"
                    | _ -> failtest "Expected Active state"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recording watch session with duplicate sessionId fails" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = []
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Watch_session_recorded sessionData
                ]
                let result = givenWhenThen given (Record_watch_session sessionData)
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"

            testCase "changing watch session date produces event" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = []
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Watch_session_recorded sessionData
                ]
                let result = givenWhenThen given (Change_watch_session_date (sessionId, "2025-02-20"))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Watch_session_date_changed (sid, date) ->
                        Expect.equal sid sessionId "SessionId should match"
                        Expect.equal date "2025-02-20" "Date should match"
                    | _ -> failtest "Expected Watch_session_date_changed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding friend to watch session is idempotent" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = [ "marco" ]
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Watch_session_recorded sessionData
                ]
                // First add produces event
                let result1 = givenWhenThen given (Add_friend_to_watch_session (sessionId, "sarah"))
                match result1 with
                | Ok events -> Expect.equal (List.length events) 1 "Should produce one event"
                | Error e -> failtest $"Expected success but got: {e}"
                // Adding same friend again is idempotent
                let givenWithSarah = given @ [ Friend_added_to_watch_session (sessionId, "sarah") ]
                let result2 = givenWhenThen givenWithSarah (Add_friend_to_watch_session (sessionId, "sarah"))
                match result2 with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events (idempotent)"
                | Error e -> failtest $"Expected success but got: {e}"
                // Adding marco who was already in original session is idempotent
                let result3 = givenWhenThen given (Add_friend_to_watch_session (sessionId, "marco"))
                match result3 with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events for existing friend"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing friend from watch session produces event" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = [ "marco" ]
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Watch_session_recorded sessionData
                ]
                let result = givenWhenThen given (Remove_friend_from_watch_session (sessionId, "marco"))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Friend_removed_from_watch_session (sid, slug) ->
                        Expect.equal sid sessionId "SessionId should match"
                        Expect.equal slug "marco" "Friend slug should match"
                    | _ -> failtest "Expected Friend_removed_from_watch_session"
                | Error e -> failtest $"Expected success but got: {e}"
                // Removing non-existing friend produces no events
                let result2 = givenWhenThen given (Remove_friend_from_watch_session (sessionId, "nobody"))
                match result2 with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events for non-existing friend"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "watch session commands on non-existent movie fail" <| fun _ ->
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = []
                }
                let commands: MovieCommand list = [
                    Record_watch_session sessionData
                    Change_watch_session_date (sessionId, "2025-02-20")
                    Add_friend_to_watch_session (sessionId, "marco")
                    Remove_friend_from_watch_session (sessionId, "marco")
                ]
                for cmd in commands do
                    let result = givenWhenThen [] cmd
                    match result with
                    | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                    | Ok _ -> failtest $"Expected error for command on non-existent movie: {cmd}"
        ]

        testList "In Focus" [
            testCase "setting in focus on a movie produces Movie_in_focus_set" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] Set_movie_in_focus
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] Movie_in_focus_set "Should be Movie_in_focus_set"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "setting in focus when already in focus is idempotent" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Movie_in_focus_set ]
                                Set_movie_in_focus
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "clearing in focus produces Movie_in_focus_cleared" <| fun _ ->
                let result = givenWhenThen
                                [ Movie_added_to_library sampleMovieData; Movie_in_focus_set ]
                                Clear_movie_in_focus
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] Movie_in_focus_cleared "Should be Movie_in_focus_cleared"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "clearing in focus when not in focus is idempotent" <| fun _ ->
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] Clear_movie_in_focus
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recording watch session auto-clears in focus" <| fun _ ->
                let sessionData: WatchSessionRecordedData = {
                    SessionId = System.Guid.NewGuid().ToString("N")
                    Date = "2025-01-15"
                    Duration = Some 136
                    FriendSlugs = []
                }
                let given = [
                    Movie_added_to_library sampleMovieData
                    Movie_in_focus_set
                ]
                let result = givenWhenThen given (Record_watch_session sessionData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 2 "Should produce two events"
                    match events.[0] with
                    | Watch_session_recorded _ -> ()
                    | _ -> failtest "First event should be Watch_session_recorded"
                    Expect.equal events.[1] Movie_in_focus_cleared "Second event should be Movie_in_focus_cleared"
                    // Verify state
                    let state = reconstitute (given @ events)
                    match state with
                    | Active movie ->
                        Expect.isFalse movie.InFocus "InFocus should be false after watch session"
                    | _ -> failtest "Expected Active state"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recording watch session when not in focus does not emit clear" <| fun _ ->
                let sessionData: WatchSessionRecordedData = {
                    SessionId = System.Guid.NewGuid().ToString("N")
                    Date = "2025-01-15"
                    Duration = Some 136
                    FriendSlugs = []
                }
                let result = givenWhenThen [ Movie_added_to_library sampleMovieData ] (Record_watch_session sessionData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event (no auto-clear)"
                    match events.[0] with
                    | Watch_session_recorded _ -> ()
                    | _ -> failtest "Should be Watch_session_recorded"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "InFocus defaults to false on new movie" <| fun _ ->
                let state = reconstitute [ Movie_added_to_library sampleMovieData ]
                match state with
                | Active movie -> Expect.isFalse movie.InFocus "InFocus should default to false"
                | _ -> failtest "Expected Active state"
        ]

        testList "Removed movie" [
            testCase "commands on removed movie fail" <| fun _ ->
                let removedEvents = [ Movie_added_to_library sampleMovieData; Movie_removed_from_library ]
                let sessionData: WatchSessionRecordedData = {
                    SessionId = "test-session"
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = []
                }
                let commands = [
                    Add_movie_to_library sampleMovieData
                    Remove_movie_from_library
                    Categorize_movie [ "Drama" ]
                    Replace_poster "x"
                    Replace_backdrop "x"
                    Recommend_by "marco"
                    Remove_recommendation "marco"
                    Add_want_to_watch_with "sarah"
                    Remove_from_want_to_watch_with "sarah"
                    Record_watch_session sessionData
                    Change_watch_session_date ("test-session", "2025-02-20")
                    Add_friend_to_watch_session ("test-session", "marco")
                    Remove_friend_from_watch_session ("test-session", "marco")
                    Set_movie_in_focus
                    Clear_movie_in_focus
                ]
                for cmd in commands do
                    let result = givenWhenThen removedEvents cmd
                    match result with
                    | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                    | Ok _ -> failtest $"Expected error for command on removed movie: {cmd}"
        ]

        testList "Serialization" [
            testCase "Movie_added_to_library round-trip" <| fun _ ->
                let event = Movie_added_to_library sampleMovieData
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Movie_removed_from_library round-trip" <| fun _ ->
                let event = Movie_removed_from_library
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Movie_categorized round-trip" <| fun _ ->
                let event = Movie_categorized [ "Drama"; "Comedy" ]
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Movie_recommended_by round-trip" <| fun _ ->
                let event = Movie_recommended_by "marco"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Want_to_watch_with round-trip" <| fun _ ->
                let event = Want_to_watch_with "sarah"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Watch_session_recorded round-trip" <| fun _ ->
                let event = Watch_session_recorded {
                    SessionId = "abc123"
                    Date = "2025-01-15"
                    Duration = Some 136
                    FriendSlugs = [ "marco"; "sarah" ]
                }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Watch_session_recorded with no duration round-trip" <| fun _ ->
                let event = Watch_session_recorded {
                    SessionId = "abc123"
                    Date = "2025-01-15"
                    Duration = None
                    FriendSlugs = []
                }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Watch_session_date_changed round-trip" <| fun _ ->
                let event = Watch_session_date_changed ("session1", "2025-02-20")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Friend_added_to_watch_session round-trip" <| fun _ ->
                let event = Friend_added_to_watch_session ("session1", "marco")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Friend_removed_from_watch_session round-trip" <| fun _ ->
                let event = Friend_removed_from_watch_session ("session1", "marco")
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Movie_in_focus_set round-trip" <| fun _ ->
                let event = Movie_in_focus_set
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Movie_in_focus_cleared round-trip" <| fun _ ->
                let event = Movie_in_focus_cleared
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
