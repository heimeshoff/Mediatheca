module Mediatheca.Tests.FriendIntegrationTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    CastStore.initialize conn
    conn

[<Tests>]
let friendIntegrationTests =
    testList "Friend Integration" [

        testCase "serialize event, store, read back, deserialize round-trip" <| fun _ ->
            let conn = createInMemoryConnection ()
            let event = Friends.Friend_added { Name = "Marco"; ImageRef = Some "friends/marco.jpg" }
            let eventData = Friends.Serialization.toEventData event
            let streamId = Friends.streamId "marco"

            let result = EventStore.appendToStream conn streamId -1L [ eventData ]
            match result with
            | EventStore.Success _ -> ()
            | EventStore.ConcurrencyConflict _ -> failtest "Expected success"

            let stored = EventStore.readStream conn streamId
            Expect.equal (List.length stored) 1 "Should have 1 event"

            let deserialized = Friends.Serialization.fromStoredEvent stored.[0]
            Expect.equal deserialized (Some event) "Should round-trip through event store"

        testCase "friend projection populates read model after event" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Friends.streamId "marco"

            FriendProjection.handler.Init conn

            let event = Friends.Friend_added { Name = "Marco"; ImageRef = None }
            let eventData = Friends.Serialization.toEventData event
            EventStore.appendToStream conn streamId -1L [ eventData ] |> ignore

            Projection.runProjection conn FriendProjection.handler

            let friends = FriendProjection.getAll conn
            Expect.equal (List.length friends) 1 "Should have 1 friend in list"
            Expect.equal friends.[0].Name "Marco" "Name should match"
            Expect.equal friends.[0].Slug "marco" "Slug should match"

            let detail = FriendProjection.getBySlug conn "marco"
            Expect.isSome detail "Should find friend detail"
            Expect.equal detail.Value.Name "Marco" "Detail name should match"

        testCase "friend update changes read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Friends.streamId "marco"

            FriendProjection.handler.Init conn

            let addEvent = Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None })
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            let updateEvent = Friends.Serialization.toEventData (Friends.Friend_updated { Name = "Marco H."; ImageRef = Some "friends/marco.jpg" })
            EventStore.appendToStream conn streamId 0L [ updateEvent ] |> ignore

            Projection.runProjection conn FriendProjection.handler

            let detail = FriendProjection.getBySlug conn "marco"
            Expect.isSome detail "Should find friend detail"
            Expect.equal detail.Value.Name "Marco H." "Name should be updated"
            Expect.equal detail.Value.ImageRef (Some "friends/marco.jpg") "ImageRef should be updated"

        testCase "friend removal clears read model" <| fun _ ->
            let conn = createInMemoryConnection ()
            let streamId = Friends.streamId "marco"

            FriendProjection.handler.Init conn

            let addEvent = Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None })
            EventStore.appendToStream conn streamId -1L [ addEvent ] |> ignore

            let removeEvent = Friends.Serialization.toEventData Friends.Friend_removed
            EventStore.appendToStream conn streamId 0L [ removeEvent ] |> ignore

            Projection.runProjection conn FriendProjection.handler

            let friends = FriendProjection.getAll conn
            Expect.equal (List.length friends) 0 "Should have no friends after removal"

            let detail = FriendProjection.getBySlug conn "marco"
            Expect.isNone detail "Should not find removed friend"

        testCase "friend removal cascades to movie projections" <| fun _ ->
            let conn = createInMemoryConnection ()
            let movieStreamId = Movies.streamId "the-matrix-1999"
            let friendStreamId = Friends.streamId "marco"

            MovieProjection.handler.Init conn
            FriendProjection.handler.Init conn

            // Add a movie
            let movieData: Movies.MovieAddedData = {
                Name = "The Matrix"; Year = 1999; Runtime = Some 136
                Overview = "A computer hacker learns about the true nature of reality"
                Genres = [ "Action"; "Sci-Fi" ]; PosterRef = None; BackdropRef = None
                TmdbId = 603; TmdbRating = None
            }
            let addMovie = Movies.Serialization.toEventData (Movies.Movie_added_to_library movieData)
            EventStore.appendToStream conn movieStreamId -1L [ addMovie ] |> ignore

            // Recommend and want-to-watch-with "marco"
            let recEvent = Movies.Serialization.toEventData (Movies.Movie_recommended_by "marco")
            EventStore.appendToStream conn movieStreamId 0L [ recEvent ] |> ignore
            let wtwEvent = Movies.Serialization.toEventData (Movies.Want_to_watch_with "marco")
            EventStore.appendToStream conn movieStreamId 1L [ wtwEvent ] |> ignore

            // Add then remove friend "marco"
            let addFriend = Friends.Serialization.toEventData (Friends.Friend_added { Name = "Marco"; ImageRef = None })
            EventStore.appendToStream conn friendStreamId -1L [ addFriend ] |> ignore
            let removeFriend = Friends.Serialization.toEventData Friends.Friend_removed
            EventStore.appendToStream conn friendStreamId 0L [ removeFriend ] |> ignore

            // Run both projections
            Projection.runProjection conn MovieProjection.handler
            Projection.runProjection conn FriendProjection.handler

            // Movie detail should have empty friend lists
            let detail = MovieProjection.getBySlug conn "the-matrix-1999"
            Expect.isSome detail "Should find movie detail"
            Expect.equal detail.Value.RecommendedBy [] "recommended_by should be empty after friend removal"
            Expect.equal detail.Value.WantToWatchWith [] "want_to_watch_with should be empty after friend removal"
    ]
