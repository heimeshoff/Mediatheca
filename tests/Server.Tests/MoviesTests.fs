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

        testList "Removed movie" [
            testCase "commands on removed movie fail" <| fun _ ->
                let removedEvents = [ Movie_added_to_library sampleMovieData; Movie_removed_from_library ]
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
        ]
    ]
