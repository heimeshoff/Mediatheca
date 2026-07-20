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

        testCase "getEvents returns events served through IAdminApi" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-dune-2021" -1L [ makeEvent "MovieAdded" """{"name":"Dune"}""" ] |> ignore
            let api = Administration.create conn

            let query: EventQuery = { StreamFilter = None; EventTypeFilter = None; Limit = 100; Offset = 0 }
            let events = api.getEvents query |> Async.RunSynchronously

            Expect.equal (List.length events) 1 "Should return the one appended event"
            Expect.equal events.[0].StreamId "movies-dune-2021" "Stream id should match"
            Expect.equal events.[0].EventType "MovieAdded" "Event type should match"

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
