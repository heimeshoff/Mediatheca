module Mediatheca.Tests.EventStoreTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

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
let eventStoreTests =
    testList "EventStore" [

        testCase "append events to a stream and read them back" <| fun _ ->
            let conn = createInMemoryConnection ()
            let events = [
                makeEvent "BookAdded" """{"title":"Domain Modeling Made Functional"}"""
                makeEvent "BookAdded" """{"title":"Event Sourcing in Action"}"""
            ]

            let result = EventStore.appendToStream conn "books-1" -1L events
            match result with
            | EventStore.Success _ -> ()
            | EventStore.ConcurrencyConflict _ -> failtest "Expected success but got concurrency conflict"

            let stored = EventStore.readStream conn "books-1"
            Expect.equal (List.length stored) 2 "Should have 2 events"
            Expect.equal stored.[0].EventType "BookAdded" "First event type"
            Expect.equal stored.[0].StreamPosition 0L "Second event stream position"
            Expect.equal stored.[1].StreamPosition 1L "Second event stream position"

        testCase "stream position tracking" <| fun _ ->
            let conn = createInMemoryConnection ()

            let pos0 = EventStore.getStreamPosition conn "empty-stream"
            Expect.equal pos0 -1L "Empty stream position should be -1"

            let events = [ makeEvent "TestEvent" """{"value":1}""" ]
            EventStore.appendToStream conn "test-stream" -1L events |> ignore

            let pos1 = EventStore.getStreamPosition conn "test-stream"
            Expect.equal pos1 0L "After one event, position should be 0"

            EventStore.appendToStream conn "test-stream" 0L [ makeEvent "TestEvent" """{"value":2}""" ] |> ignore

            let pos2 = EventStore.getStreamPosition conn "test-stream"
            Expect.equal pos2 1L "After two events, position should be 1"

        testCase "optimistic concurrency conflict" <| fun _ ->
            let conn = createInMemoryConnection ()
            let events = [ makeEvent "TestEvent" """{"value":1}""" ]

            EventStore.appendToStream conn "stream-1" -1L events |> ignore

            let result = EventStore.appendToStream conn "stream-1" -1L [ makeEvent "TestEvent" """{"value":2}""" ]
            match result with
            | EventStore.ConcurrencyConflict (expected, actual) ->
                Expect.equal expected -1L "Expected position"
                Expect.equal actual 0L "Actual position"
            | EventStore.Success _ -> failtest "Expected concurrency conflict but got success"

        testCase "read all events forward with pagination" <| fun _ ->
            let conn = createInMemoryConnection ()

            // Append events to different streams
            for i in 1..5 do
                let streamId = $"stream-{i}"
                let events = [ makeEvent "TestEvent" $"""{{"index":{i}}}""" ]
                EventStore.appendToStream conn streamId -1L events |> ignore

            let batch1 = EventStore.readAllForward conn 0L 3
            Expect.equal (List.length batch1) 3 "First batch should have 3 events"

            let lastPos = (List.last batch1).GlobalPosition
            let batch2 = EventStore.readAllForward conn lastPos 3
            Expect.equal (List.length batch2) 2 "Second batch should have 2 events"

            let batch3 = EventStore.readAllForward conn (lastPos + 2L) 3
            Expect.equal (List.length batch3) 0 "Third batch should be empty"

        testCase "projection checkpoint save and load" <| fun _ ->
            let conn = createInMemoryConnection ()

            let checkpoint0 = Projection.getCheckpoint conn "test-projection"
            Expect.equal checkpoint0 0L "Initial checkpoint should be 0"

            Projection.saveCheckpoint conn "test-projection" 42L

            let checkpoint1 = Projection.getCheckpoint conn "test-projection"
            Expect.equal checkpoint1 42L "Checkpoint should be 42 after save"

            Projection.saveCheckpoint conn "test-projection" 100L

            let checkpoint2 = Projection.getCheckpoint conn "test-projection"
            Expect.equal checkpoint2 100L "Checkpoint should be 100 after update"

        testCase "projection replay processes events" <| fun _ ->
            let conn = createInMemoryConnection ()

            // Append some events
            for i in 1..3 do
                let events = [ makeEvent "CountEvent" $"""{{"count":{i}}}""" ]
                EventStore.appendToStream conn $"stream-{i}" -1L events |> ignore

            // Track processed events
            let processed = System.Collections.Generic.List<string>()

            let handler: Projection.ProjectionHandler = {
                Name = "test-counter"
                Handle = fun _ event -> processed.Add(event.EventType)
                Init = fun _ -> ()
                Drop = fun _ -> ()
            }

            Projection.runProjection conn handler

            Expect.equal processed.Count 3 "Should have processed 3 events"

            let checkpoint = Projection.getCheckpoint conn "test-counter"
            Expect.isGreaterThan checkpoint 0L "Checkpoint should be greater than 0"
    ]
