module Mediatheca.Tests.AdministrationTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    conn

let private makeEvent eventType data : EventStore.EventData = {
    EventType = eventType
    Data = data
    Metadata = "{}"
}

[<Tests>]
let administrationTests =
    testList "Administration" [

        testCase "getEventPage returns events served through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" """{"name":"Dune"}""" ] |> ignore
            let api = Administration.create conn

            let query: EventPageQuery = { Filter = EventFilter.empty; Before = None; PageSize = 100 }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal (List.length page.Events) 1 "Should return the one appended event"
            Expect.equal page.Events.[0].StreamId "movies-dune-2021" "Stream id should match"
            Expect.equal page.Events.[0].EventType "MovieAdded" "Event type should match"
            Expect.equal page.TotalMatches 1 "Total matches should count the one event"
            Expect.isFalse page.HasMore "Single event should not have more pages"

        testCase "getEventPage resolves BoundedContext filter to a stream_id prefix" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = Administration.create conn

            let query: EventPageQuery = {
                Filter = { EventFilter.empty with BoundedContext = Some "Movies" }
                Before = None
                PageSize = 100
            }
            let page = api.getEventPage query |> Async.RunSynchronously

            Expect.equal page.TotalMatches 1 "Only the Movie- stream event should match"
            Expect.equal page.Events.[0].StreamId "Movie-dune" "Should be the movie event"

        testCase "getBoundedContexts returns the known bounded context names" <| fun _ ->
            let conn = createInMemoryConnection ()
            let api = Administration.create conn

            let contexts = api.getBoundedContexts () |> Async.RunSynchronously

            Expect.contains contexts "Movies" "Should include Movies"
            Expect.contains contexts "Friends" "Should include Friends"

        testCase "getEventStreams returns distinct stream ids through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "friends-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore
            let api = Administration.create conn

            let streams = api.getEventStreams () |> Async.RunSynchronously

            Expect.contains streams "movies-dune-2021" "Should include movies stream"
            Expect.contains streams "friends-alice" "Should include friends stream"

        testCase "getEventTypes returns distinct event types through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "movies-dune-2021" 0L [ makeEvent "MovieRated" "{}" ] |> ignore
            let api = Administration.create conn

            let types = api.getEventTypes () |> Async.RunSynchronously

            Expect.contains types "MovieAdded" "Should include MovieAdded"
            Expect.contains types "MovieRated" "Should include MovieRated"
    ]
