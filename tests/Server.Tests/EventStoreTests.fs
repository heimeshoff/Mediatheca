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

        testCase "queryEventPage full-text search finds events by payload only" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "stream-a" -1L [ makeEvent "Noted" """{"note":"a rare mention of marginalia here"}""" ] |> ignore
            EventStore.appendToStream conn "stream-b" -1L [ makeEvent "Noted" """{"note":"nothing special"}""" ] |> ignore

            let filter = { EventStore.emptyQueryFilter with Search = Some "marginalia" }
            let results, hasMore, total = EventStore.queryEventPage conn filter None 10

            Expect.equal total 1 "Only one event's payload contains the search term"
            Expect.equal (List.length results) 1 "Should return exactly the matching event"
            Expect.equal results.[0].StreamId "stream-a" "Should match the event whose data contains the term"
            Expect.isFalse hasMore "Only one match, no more pages"

        testCase "queryEventPage filters by bounded-context stream prefix" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" "{}" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" "{}" ] |> ignore

            let filter = { EventStore.emptyQueryFilter with StreamPrefix = Some "Movie-" }
            let results, _, total = EventStore.queryEventPage conn filter None 10

            Expect.equal total 1 "Only the Movie- prefixed stream should match"
            Expect.equal results.[0].StreamId "Movie-dune" "Should match the movie stream"

        testCase "queryEventPage filters by timestamp range" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "stream-a" -1L [ makeEvent "First" "{}" ] |> ignore
            let firstTimestamp = (EventStore.readStream conn "stream-a").[0].Timestamp
            System.Threading.Thread.Sleep 10
            EventStore.appendToStream conn "stream-b" -1L [ makeEvent "Second" "{}" ] |> ignore

            let filter = { EventStore.emptyQueryFilter with TimestampTo = Some (firstTimestamp.ToString("o")) }
            let results, _, total = EventStore.queryEventPage conn filter None 10

            Expect.equal total 1 "Only the first event should be at or before the cutoff"
            Expect.equal results.[0].EventType "First" "Should be the first event"

        testCase "queryEventPage composes search, stream, event-type and BC filters" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "Movie-dune" -1L [ makeEvent "MovieAdded" """{"note":"quicksilver detail"}""" ] |> ignore
            EventStore.appendToStream conn "Movie-dune" 0L [ makeEvent "MovieRated" """{"note":"quicksilver rating"}""" ] |> ignore
            EventStore.appendToStream conn "Friend-alice" -1L [ makeEvent "FriendAdded" """{"note":"quicksilver friend"}""" ] |> ignore

            let filter = {
                EventStore.emptyQueryFilter with
                    Search = Some "quicksilver"
                    StreamPrefix = Some "Movie-"
                    EventTypeFilter = Some "Rated"
            }
            let results, _, total = EventStore.queryEventPage conn filter None 10

            Expect.equal total 1 "Only MovieRated on the Movie- stream matching the search term should remain"
            Expect.equal results.[0].EventType "MovieRated" "Should be the rated event"

        testCase "queryEventPage keyset pagination pages forward without skipping or duplicating" <| fun _ ->
            let conn = createInMemoryConnection ()
            for i in 1..5 do
                EventStore.appendToStream conn $"stream-{i}" -1L [ makeEvent "TestEvent" $"""{{"i":{i}}}""" ] |> ignore

            let filter = EventStore.emptyQueryFilter
            let page1, hasMore1, total1 = EventStore.queryEventPage conn filter None 2
            Expect.equal (List.length page1) 2 "First page should have 2 events"
            Expect.isTrue hasMore1 "Should have more pages after the first"
            Expect.equal total1 5 "Total matches should count all 5 events"

            let cursor1 = (List.last page1).GlobalPosition
            let page2, hasMore2, _ = EventStore.queryEventPage conn filter (Some cursor1) 2
            Expect.equal (List.length page2) 2 "Second page should have 2 events"
            Expect.isTrue hasMore2 "Should have one more page after the second"

            let cursor2 = (List.last page2).GlobalPosition
            let page3, hasMore3, _ = EventStore.queryEventPage conn filter (Some cursor2) 2
            Expect.equal (List.length page3) 1 "Third page should have the last remaining event"
            Expect.isFalse hasMore3 "No more pages after the third"

            let allPositions = (page1 @ page2 @ page3) |> List.map (fun e -> e.GlobalPosition)
            Expect.equal (List.length allPositions) (List.length (List.distinct allPositions)) "No event should appear on more than one page"
            Expect.equal (List.length allPositions) 5 "All 5 events should be covered across the pages"

        testCase "FTS backfill is idempotent and covers pre-existing events after restart" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "movies-1" -1L [ makeEvent "MovieAdded" """{"title":"Blade Runner marginalia"}""" ] |> ignore

            // Simulate a database that predates the FTS migration: the row in
            // `events` exists, but the FTS index/trigger do not.
            use dropCmd = conn.CreateCommand()
            dropCmd.CommandText <- "DROP TRIGGER events_fts_ai; DROP TABLE events_fts;"
            dropCmd.ExecuteNonQuery() |> ignore

            // Re-running initialize, as happens on every server restart, must
            // recreate the index and backfill the pre-existing row.
            EventStore.initialize conn
            // Running it again immediately after must be a no-op, not an error
            // or a duplicate-indexing attempt.
            EventStore.initialize conn

            let filter = { EventStore.emptyQueryFilter with Search = Some "marginalia" }
            let results, _, total = EventStore.queryEventPage conn filter None 10

            Expect.equal total 1 "Backfilled event should be found via FTS search"
            Expect.equal results.[0].StreamId "movies-1" "Should be the pre-existing event"

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
