module Mediatheca.Tests.MoviesIntegrationTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    conn

let private sampleMovieData: Movies.MovieAddedData = {
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

[<Tests>]
let catalogIntegrationTests =
    testList "Movies Integration" [

        testCase "serialize event, store, read back, deserialize round-trip" <| fun _ ->
            let conn = createInMemoryConnection ()
            let event = Movies.Movie_added_to_library sampleMovieData
            let eventData = Movies.Serialization.toEventData event
            let streamId = Movies.streamId "the-matrix-1999"

            // Append
            let result = EventStore.appendToStream conn streamId -1L [ eventData ]
            match result with
            | EventStore.Success _ -> ()
            | EventStore.ConcurrencyConflict _ -> failtest "Expected success"

            // Read back
            let stored = EventStore.readStream conn streamId
            Expect.equal (List.length stored) 1 "Should have 1 event"

            // Deserialize
            let deserialized = Movies.Serialization.fromStoredEvent stored.[0]
            Expect.equal deserialized (Some event) "Should round-trip through event store"

        testCase "movie projection populates read model after event" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Movies.streamId "the-matrix-1999"

            // Initialize projection
            MovieProjection.handler.Init conn

            // Append movie added event
            let event = Movies.Movie_added_to_library sampleMovieData
            let eventData = Movies.Serialization.toEventData event
            EventStore.appendToStream conn streamId -1L [ eventData ] |> ignore

            // Run projection
            Projection.runProjection conn MovieProjection.handler

            // Query read model
            let movies = MovieProjection.getAll conn
            Expect.equal (List.length movies) 1 "Should have 1 movie in list"
            Expect.equal movies.[0].Name "The Matrix" "Name should match"
            Expect.equal movies.[0].Year 1999 "Year should match"
            Expect.equal movies.[0].Slug "the-matrix-1999" "Slug should match"

            let detail = MovieProjection.getBySlug conn "the-matrix-1999"
            Expect.isSome detail "Should find movie detail"
            let d = detail.Value
            Expect.equal d.Name "The Matrix" "Detail name should match"
            Expect.equal d.Genres [ "Action"; "Sci-Fi" ] "Genres should match"
            Expect.equal d.TmdbId 603 "TMDB ID should match"

        testCase "movie removal clears read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Movies.streamId "the-matrix-1999"

            MovieProjection.handler.Init conn

            // Add movie
            let addEvent = Movies.Serialization.toEventData (Movies.Movie_added_to_library sampleMovieData)
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            // Remove movie
            let removeEvent = Movies.Serialization.toEventData Movies.Movie_removed_from_library
            EventStore.appendToStream conn streamId 0L [ removeEvent ] |> ignore

            Projection.runProjection conn MovieProjection.handler

            let movies = MovieProjection.getAll conn
            Expect.equal (List.length movies) 0 "Should have no movies after removal"

            let detail = MovieProjection.getBySlug conn "the-matrix-1999"
            Expect.isNone detail "Should not find removed movie"

        testCase "categorize movie updates genres in read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Movies.streamId "the-matrix-1999"

            MovieProjection.handler.Init conn

            let addEvent = Movies.Serialization.toEventData (Movies.Movie_added_to_library sampleMovieData)
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            let catEvent = Movies.Serialization.toEventData (Movies.Movie_categorized [ "Drama"; "Mystery" ])
            EventStore.appendToStream conn streamId 0L [ catEvent ] |> ignore

            Projection.runProjection conn MovieProjection.handler

            let movies = MovieProjection.getAll conn
            Expect.equal movies.[0].Genres [ "Drama"; "Mystery" ] "Genres should be updated"

        testCase "recommend movie updates read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Movies.streamId "the-matrix-1999"

            MovieProjection.handler.Init conn

            let addEvent = Movies.Serialization.toEventData (Movies.Movie_added_to_library sampleMovieData)
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            let recEvent = Movies.Serialization.toEventData (Movies.Movie_recommended_by "marco")
            EventStore.appendToStream conn streamId 0L [ recEvent ] |> ignore

            Projection.runProjection conn MovieProjection.handler

            let detail = MovieProjection.getBySlug conn "the-matrix-1999"
            Expect.isSome detail "Should find movie detail"
            Expect.equal (detail.Value.RecommendedBy |> List.length) 1 "Should have 1 recommendation"
            Expect.equal detail.Value.RecommendedBy.[0].Slug "marco" "Should be recommended by marco"

        testCase "watch session recorded updates read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Movies.streamId "the-matrix-1999"

            MovieProjection.handler.Init conn

            let addEvent = Movies.Serialization.toEventData (Movies.Movie_added_to_library sampleMovieData)
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            let sessionId = System.Guid.NewGuid().ToString("N")
            let sessionData: Movies.WatchSessionRecordedData = {
                SessionId = sessionId
                Date = "2025-01-15"
                Duration = Some 136
                FriendSlugs = [ "marco"; "sarah" ]
            }
            let sessionEvent = Movies.Serialization.toEventData (Movies.Watch_session_recorded sessionData)
            EventStore.appendToStream conn streamId 0L [ sessionEvent ] |> ignore

            Projection.runProjection conn MovieProjection.handler

            let sessions = MovieProjection.getWatchSessions conn "the-matrix-1999"
            Expect.equal (List.length sessions) 1 "Should have 1 watch session"
            Expect.equal sessions.[0].SessionId sessionId "SessionId should match"
            Expect.equal sessions.[0].Date "2025-01-15" "Date should match"
            Expect.equal sessions.[0].Duration (Some 136) "Duration should match"
            Expect.equal (sessions.[0].Friends |> List.length) 2 "Should have 2 friends"
            Expect.equal sessions.[0].Friends.[0].Slug "marco" "First friend should be marco"
            Expect.equal sessions.[0].Friends.[1].Slug "sarah" "Second friend should be sarah"

            // Also verify getBySlug includes watch sessions
            let detail = MovieProjection.getBySlug conn "the-matrix-1999"
            Expect.isSome detail "Should find movie detail"
            Expect.equal (detail.Value.WatchSessions |> List.length) 1 "Detail should include 1 watch session"
    ]
