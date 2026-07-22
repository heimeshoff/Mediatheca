module Mediatheca.Tests.EventStoreNdjsonTests

open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

/// Export/import as NDJSON (administration-vrc56, ADR-0029): the event log's
/// portable form. These tests exercise `EventStore.exportNdjson` /
/// `EventStore.importNdjson` directly — plain Expecto, no HTTP pipeline —
/// since the Giraffe routes are thin wrappers over `ctx.Response.Body` /
/// `ctx.Request.Body`.
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

let private exportToString (conn: SqliteConnection) : string =
    use writer = new StringWriter()
    EventStore.exportNdjson conn writer
    writer.ToString()

/// A TextReader that records whether anything was ever read from it, so
/// tests can assert the non-empty-store refusal happens before the upload
/// body is touched at all.
type private TrackingReader(inner: TextReader) =
    inherit TextReader()
    let mutable touched = false
    member _.Touched = touched
    override _.ReadLine() =
        touched <- true
        inner.ReadLine()
    override _.Peek() =
        touched <- true
        inner.Peek()
    override _.Read() =
        touched <- true
        inner.Read()

[<Tests>]
let eventStoreNdjsonTests =
    testList "EventStore NDJSON export/import" [

        testCase "export produces one line per event, ascending global_position, fixed field order, data/metadata as escaped strings" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "stream-a" -1L [ makeEvent "FirstEvent" """{"note":"line one"}""" ] |> ignore
            EventStore.appendToStream conn "stream-b" -1L [ makeEvent "SecondEvent" """{"note":"line two, with \"quotes\" and unicode 日本語"}""" ] |> ignore

            let ndjson = exportToString conn
            let lines = ndjson.Split('\n') |> Array.filter (fun l -> l.Trim() <> "")
            Expect.equal lines.Length 2 "One line per event"

            for line in lines do
                let fieldOrder =
                    [ "\"globalPosition\""; "\"streamId\""; "\"streamPosition\""; "\"eventType\""; "\"data\""; "\"metadata\""; "\"timestamp\"" ]
                    |> List.map line.IndexOf
                Expect.isTrue (fieldOrder = List.sort fieldOrder) "Fields must appear in the fixed schema order"
                Expect.stringContains line "\"data\":\"" "data must be embedded as a JSON string value, not a nested object"
                Expect.isFalse (line.Contains "\"data\":{") "data must not be re-nested as a JSON object"

            Expect.isTrue (lines.[0].IndexOf "\"globalPosition\":1" >= 0) "First line should carry global_position 1"
            Expect.isTrue (lines.[1].Contains "日本語") "Unicode payload content must survive escaping"

        testCase "export across a batch boundary yields every event exactly once, in order" <| fun _ ->
            let conn = createInMemoryConnection ()
            // More than the internal batch size (500) to force readAllForward
            // to be walked across multiple batches.
            for i in 1..600 do
                EventStore.appendToStream conn $"stream-{i}" -1L [ makeEvent "TestEvent" $"""{{"i":{i}}}""" ] |> ignore

            let ndjson = exportToString conn
            let lines = ndjson.Split('\n') |> Array.filter (fun l -> l.Trim() <> "")
            Expect.equal lines.Length 600 "Every event across multiple batches must be exported exactly once"

            let positions =
                lines
                |> Array.map (fun l ->
                    let start = l.IndexOf "\"globalPosition\":" + "\"globalPosition\":".Length
                    let stop = l.IndexOf(',', start)
                    l.Substring(start, stop - start) |> int64)
            Expect.equal (Array.toList positions) (positions |> Array.sort |> Array.toList) "Positions must be strictly ascending"
            Expect.equal (Array.distinct positions |> Array.length) 600 "No position should repeat"

        testCase "round-trip: export A, import into empty B, export B — byte-identical NDJSON" <| fun _ ->
            let connA = createInMemoryConnection ()
            EventStore.appendToStream connA "stream-a" -1L [
                makeEvent "Noted" """{"quote":"she said \"hello\"","newline":"line1\nline2","unicode":"héllo 世界"}"""
            ] |> ignore
            EventStore.appendToStream connA "stream-b" -1L [
                makeEvent "OtherEvent" """{"value":42}"""
                makeEvent "OtherEvent" """{"value":43}"""
            ] |> ignore
            EventStore.appendToStream connA "stream-a" 0L [ makeEvent "Noted" """{"more":"data"}""" ] |> ignore

            let exportedA = exportToString connA

            let connB = createInMemoryConnection ()
            use readerB = new StringReader(exportedA)
            match EventStore.importNdjson connB readerB with
            | Error _ -> failtest "Import into an empty store should succeed"
            | Ok outcome -> Expect.equal outcome.EventsImported 4 "All 4 events should be imported"

            let exportedB = exportToString connB
            Expect.equal exportedB exportedA "Re-exported store B must be byte-identical to the original export of A"

        testCase "import into a fresh store, then rebuild, yields projections identical to the source store's" <| fun _ ->
            let connA = createInMemoryConnection ()
            FriendProjection.handler.Init connA
            let addFriend (conn: SqliteConnection) (name: string) =
                let eventData = Friends.Serialization.toEventData (Friends.Friend_added { Name = name; ImageRef = None })
                EventStore.appendToStream conn (Friends.streamId (name.ToLowerInvariant())) -1L [ eventData ] |> ignore
            addFriend connA "Marco"
            addFriend connA "Alice"
            Projection.runProjection connA FriendProjection.handler
            let expectedRows =
                FriendProjection.getAll connA
                |> List.map (fun f -> f.Slug, f.Name)
                |> List.sort

            let exportedA = exportToString connA

            let connB = createInMemoryConnection ()
            FriendProjection.handler.Init connB
            use readerB = new StringReader(exportedA)
            match EventStore.importNdjson connB readerB with
            | Error _ -> failtest "Import into an empty store should succeed"
            | Ok _ -> ()

            // Projections stay untouched by import (checkpoints left alone) —
            // the operator explicitly rebuilds, reusing the existing
            // Rebuild-all machinery (administration-qjcp4).
            Projection.rebuildProjectionWithProgress connB FriendProjection.handler (fun _ -> ())

            let actualRows =
                FriendProjection.getAll connB
                |> List.map (fun f -> f.Slug, f.Name)
                |> List.sort

            Expect.equal actualRows expectedRows "Projections rebuilt from the imported store must match the source store's"

        testCase "import into a non-empty store is refused before the body is touched" <| fun _ ->
            let conn = createInMemoryConnection ()
            EventStore.appendToStream conn "existing-stream" -1L [ makeEvent "AlreadyThere" "{}" ] |> ignore

            let ndjson = """{"globalPosition":1,"streamId":"s","streamPosition":0,"eventType":"E","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
            use tracking = new TrackingReader(new StringReader(ndjson))

            match EventStore.importNdjson conn tracking with
            | Ok _ -> failtest "Import into a non-empty store must be refused"
            | Error EventStore.StoreNotEmpty ->
                Expect.isFalse tracking.Touched "The uploaded body must not be read at all before the refusal"
            | Error other -> failtest $"Expected StoreNotEmpty, got {other}"

            Expect.equal (EventStore.getTotalEventCount conn) 1 "The existing store must be untouched"

        testCase "global_position is preserved exactly; a subsequent append continues from imported max + 1" <| fun _ ->
            let connA = createInMemoryConnection ()
            for i in 1..5 do
                EventStore.appendToStream connA $"stream-{i}" -1L [ makeEvent "TestEvent" $"""{{"i":{i}}}""" ] |> ignore
            let maxA = EventStore.getMaxGlobalPosition connA
            Expect.equal maxA 5L "Five events appended, one per stream"

            let exportedA = exportToString connA

            let connB = createInMemoryConnection ()
            use readerB = new StringReader(exportedA)
            EventStore.importNdjson connB readerB |> ignore

            Expect.equal (EventStore.getMaxGlobalPosition connB) maxA "Imported store's head must match the source store's exactly"

            let appendResult = EventStore.appendToStream connB "stream-new" -1L [ makeEvent "NewEvent" "{}" ]
            match appendResult with
            | EventStore.Success newPos -> Expect.equal newPos (maxA + 1L) "A new append after import must continue from (imported max) + 1"
            | EventStore.ConcurrencyConflict _ -> failtest "Unexpected concurrency conflict"

        testCase "events_fts is searchable immediately after import, with no separate manual FTS rebuild step" <| fun _ ->
            let connA = createInMemoryConnection ()
            EventStore.appendToStream connA "stream-a" -1L [ makeEvent "Noted" """{"note":"a rare mention of marginalia here"}""" ] |> ignore
            EventStore.appendToStream connA "stream-b" -1L [ makeEvent "Noted" """{"note":"nothing special"}""" ] |> ignore

            let exportedA = exportToString connA

            let connB = createInMemoryConnection ()
            use readerB = new StringReader(exportedA)
            match EventStore.importNdjson connB readerB with
            | Error _ -> failtest "Import into an empty store should succeed"
            | Ok _ -> ()

            let filter = { EventStore.emptyQueryFilter with Search = Some "marginalia" }
            let results, _, total = EventStore.queryEventPage connB filter None 10
            Expect.equal total 1 "The imported event's payload should be found via FTS search"
            Expect.equal results.[0].StreamId "stream-a" "Should match the event whose data contains the term"

        testCase "a malformed line partway through rolls back the whole import; target store is left empty" <| fun _ ->
            let conn = createInMemoryConnection ()
            let validLine1 = """{"globalPosition":1,"streamId":"stream-a","streamPosition":0,"eventType":"E1","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:00.0000000+00:00"}"""
            let validLine2 = """{"globalPosition":2,"streamId":"stream-b","streamPosition":0,"eventType":"E2","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:01.0000000+00:00"}"""
            let malformedLine = "this is not valid json"
            let validLine3 = """{"globalPosition":3,"streamId":"stream-c","streamPosition":0,"eventType":"E3","data":"{}","metadata":"{}","timestamp":"2026-01-01T00:00:02.0000000+00:00"}"""

            let ndjson = System.String.Join("\n", [ validLine1; validLine2; malformedLine; validLine3 ])
            use reader = new StringReader(ndjson)

            match EventStore.importNdjson conn reader with
            | Ok _ -> failtest "Import with a malformed line must fail"
            | Error (EventStore.MalformedLine(lineNumber, _)) ->
                Expect.equal lineNumber 3 "The malformed line is the 3rd non-blank line"
            | Error other -> failtest $"Expected MalformedLine, got {other}"

            Expect.equal (EventStore.getTotalEventCount conn) 0 "The target store must be left empty after a rolled-back import"
    ]
