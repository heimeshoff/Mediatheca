module Mediatheca.Tests.CatalogTests

open Expecto
open Mediatheca.Server.Catalog

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
    testList "Catalog" [

        testList "AddMovieToLibrary" [
            testCase "adding movie to empty library succeeds" <| fun _ ->
                let result = givenWhenThen [] (AddMovieToLibrary sampleMovieData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | MovieAddedToLibrary data ->
                        Expect.equal data.Name "The Matrix" "Name should match"
                        Expect.equal data.Year 1999 "Year should match"
                    | _ -> failtest "Expected MovieAddedToLibrary"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding movie that already exists fails" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (AddMovieToLibrary sampleMovieData)
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"
        ]

        testList "RemoveMovieFromLibrary" [
            testCase "removing active movie produces MovieRemovedFromLibrary" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] RemoveMovieFromLibrary
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] MovieRemovedFromLibrary "Should be MovieRemovedFromLibrary"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent movie fails" <| fun _ ->
                let result = givenWhenThen [] RemoveMovieFromLibrary
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "CategorizeMovie" [
            testCase "categorizing changes genres" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (CategorizeMovie [ "Drama"; "Thriller" ])
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | MovieCategorized genres -> Expect.equal genres [ "Drama"; "Thriller" ] "Genres should match"
                    | _ -> failtest "Expected MovieCategorized"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "categorizing with same genres produces no events (idempotent)" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (CategorizeMovie [ "Action"; "Sci-Fi" ])
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "ReplacePoster" [
            testCase "replacing poster produces event" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (ReplacePoster "posters/new-poster.jpg")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | MoviePosterReplaced ref -> Expect.equal ref "posters/new-poster.jpg" "Poster ref should match"
                    | _ -> failtest "Expected MoviePosterReplaced"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "ReplaceBackdrop" [
            testCase "replacing backdrop produces event" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (ReplaceBackdrop "backdrops/new-backdrop.jpg")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | MovieBackdropReplaced ref -> Expect.equal ref "backdrops/new-backdrop.jpg" "Backdrop ref should match"
                    | _ -> failtest "Expected MovieBackdropReplaced"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "RecommendBy" [
            testCase "recommending by friend adds to set" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (RecommendBy "marco")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | MovieRecommendedBy slug -> Expect.equal slug "marco" "Friend slug should match"
                    | _ -> failtest "Expected MovieRecommendedBy"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "recommending same friend twice is idempotent" <| fun _ ->
                let result = givenWhenThen
                                [ MovieAddedToLibrary sampleMovieData; MovieRecommendedBy "marco" ]
                                (RecommendBy "marco")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "RemoveRecommendation" [
            testCase "removing existing recommendation produces event" <| fun _ ->
                let result = givenWhenThen
                                [ MovieAddedToLibrary sampleMovieData; MovieRecommendedBy "marco" ]
                                (RemoveRecommendation "marco")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | RecommendationRemoved slug -> Expect.equal slug "marco" "Friend slug should match"
                    | _ -> failtest "Expected RecommendationRemoved"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existing recommendation produces no events" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (RemoveRecommendation "marco")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "WantToWatchWith" [
            testCase "adding want-to-watch-with produces event" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (AddWantToWatchWith "sarah")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | WantToWatchWith slug -> Expect.equal slug "sarah" "Friend slug should match"
                    | _ -> failtest "Expected WantToWatchWith"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding same friend twice is idempotent" <| fun _ ->
                let result = givenWhenThen
                                [ MovieAddedToLibrary sampleMovieData; WantToWatchWith "sarah" ]
                                (AddWantToWatchWith "sarah")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing want-to-watch-with produces event" <| fun _ ->
                let result = givenWhenThen
                                [ MovieAddedToLibrary sampleMovieData; WantToWatchWith "sarah" ]
                                (RemoveFromWantToWatchWith "sarah")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | RemovedWantToWatchWith slug -> Expect.equal slug "sarah" "Friend slug should match"
                    | _ -> failtest "Expected RemovedWantToWatchWith"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existing want-to-watch-with produces no events" <| fun _ ->
                let result = givenWhenThen [ MovieAddedToLibrary sampleMovieData ] (RemoveFromWantToWatchWith "sarah")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Removed movie" [
            testCase "commands on removed movie fail" <| fun _ ->
                let removedEvents = [ MovieAddedToLibrary sampleMovieData; MovieRemovedFromLibrary ]
                let commands = [
                    AddMovieToLibrary sampleMovieData
                    RemoveMovieFromLibrary
                    CategorizeMovie [ "Drama" ]
                    ReplacePoster "x"
                    ReplaceBackdrop "x"
                    RecommendBy "marco"
                    RemoveRecommendation "marco"
                    AddWantToWatchWith "sarah"
                    RemoveFromWantToWatchWith "sarah"
                ]
                for cmd in commands do
                    let result = givenWhenThen removedEvents cmd
                    match result with
                    | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                    | Ok _ -> failtest $"Expected error for command on removed movie: {cmd}"
        ]

        testList "Serialization" [
            testCase "MovieAddedToLibrary round-trip" <| fun _ ->
                let event = MovieAddedToLibrary sampleMovieData
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "MovieRemovedFromLibrary round-trip" <| fun _ ->
                let event = MovieRemovedFromLibrary
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "MovieCategorized round-trip" <| fun _ ->
                let event = MovieCategorized [ "Drama"; "Comedy" ]
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "MovieRecommendedBy round-trip" <| fun _ ->
                let event = MovieRecommendedBy "marco"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "WantToWatchWith round-trip" <| fun _ ->
                let event = WantToWatchWith "sarah"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
