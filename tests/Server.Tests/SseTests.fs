module Mediatheca.Tests.SseTests

open Expecto
open System.Text.Json
open Mediatheca.Server

/// `Sse.sseFrame` (administration-h4k2p): the single pure home for SSE
/// frame-building. Pins the empty-payload bug directly — before this
/// helper existed, every SSE handler built its frame inline via
/// `json.TrimStart('{').TrimEnd('}')`, which reduced the empty-object
/// payload `"{}"` to `""` and left an unconditional trailing comma:
/// `data: {"type":"complete",}` — invalid JSON that made the client's
/// `JSON.parse` throw, reporting every successful projection rebuild as a
/// false failure.

/// Extracts the JSON object out of an `sseFrame` line (`data: {...}\n\n`)
/// so tests can assert it actually round-trips through a JSON parser, not
/// just eyeball the string.
let private jsonBody (frame: string) : string =
    frame
        .Substring("data: ".Length)
        .TrimEnd('\n')

[<Tests>]
let tests =
    testList "Sse.sseFrame" [
        test "empty-object payload {} yields no trailing comma and parses" {
            let frame = Sse.sseFrame "complete" "{}"
            Expect.equal frame "data: {\"type\":\"complete\"}\n\n" "frame should have no trailing comma"
            Expect.stringContains frame "\n\n" "frame should be terminated by a blank SSE line"
            let parsed = JsonDocument.Parse(jsonBody frame)
            Expect.equal (parsed.RootElement.GetProperty("type").GetString()) "complete" "type field should round-trip"
        }

        test "empty-string payload behaves identically to {}" {
            let frame = Sse.sseFrame "complete" ""
            Expect.equal frame "data: {\"type\":\"complete\"}\n\n" "empty string should be treated like an empty object"
            JsonDocument.Parse(jsonBody frame) |> ignore
        }

        test "non-empty payload keeps its fields and parses" {
            let frame = Sse.sseFrame "progress" "{\"position\":0}"
            Expect.equal frame "data: {\"type\":\"progress\",\"position\":0}\n\n" "fields should be spliced in after type"
            let parsed = JsonDocument.Parse(jsonBody frame)
            Expect.equal (parsed.RootElement.GetProperty("type").GetString()) "progress" "type field should round-trip"
            Expect.equal (parsed.RootElement.GetProperty("position").GetInt32()) 0 "payload field should round-trip"
        }

        test "multi-field non-empty payload parses with no trailing comma" {
            let frame = Sse.sseFrame "progress" "{\"position\":3,\"head\":10}"
            Expect.isFalse (frame.Contains(",}")) "frame should never contain a trailing comma before the closing brace"
            let parsed = JsonDocument.Parse(jsonBody frame)
            Expect.equal (parsed.RootElement.GetProperty("position").GetInt32()) 3 "position field should round-trip"
            Expect.equal (parsed.RootElement.GetProperty("head").GetInt32()) 10 "head field should round-trip"
        }
    ]
