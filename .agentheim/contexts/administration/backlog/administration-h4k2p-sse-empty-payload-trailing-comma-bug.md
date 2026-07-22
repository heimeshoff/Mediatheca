---
id: administration-h4k2p
title: Fix trailing-comma malformed JSON in SSE "complete"/empty-payload events (breaks the Projections tab's Rebuild button completion handling)
status: backlog
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [sse, event-store, projections, bug]
related_adrs: [0024]
related_research: []
prior_art: []
---

## Why
Discovered incidentally while smoke-testing administration-vrc56's NDJSON
export/import SSE routes against a real running server (not caught by any
existing Expecto test, since those exercise `Projection.rebuildProjectionWithProgress`
directly, never the actual SSE wire bytes).

`Administration.projectionRebuildStreamHandler`'s `writeEvent` helper builds
each SSE frame as:

```fsharp
let line = sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType (json.TrimStart('{').TrimEnd('}'))
```

When the payload is the empty object `"{}"` (exactly what the "complete" event
sends: `do! writeEvent "complete" "{}"`), `TrimStart('{').TrimEnd('}')` reduces
`"{}"` to `""`, producing the line:

```
data: {"type":"complete",}
```

— a trailing comma, which is invalid JSON. Confirmed live against a running
server:

```
$ curl -N http://localhost:PORT/api/stream/rebuild-projection/FriendProjection
data: {"type":"progress","position":0,"head":0,"eventsProcessed":0}

data: {"type":"complete",}
```

Client-side, `AdminProjections/State.fs`'s `runRebuildStream` calls
`JS.JSON.parse dataLine` **before** dispatching on `eventType`. A trailing
comma makes `JSON.parse` throw (`SyntaxError: Unexpected token '}'...` /
`Expected double-quoted property name in JSON`), which is caught by the
outer `with ex -> dispatch (Rebuild_failed (projectionName, ex.Message))` —
meaning **every successful projection rebuild currently reports itself to the
UI as a failure** (a JSON-parse error message), never as `Rebuild_completed`.
The rebuild itself still runs to completion server-side (checkpoint is saved
correctly); only the UI's own completion signal is wrong.

## What
Fix `projectionRebuildStreamHandler`'s `writeEvent` helper (and check whether
`Api.steamFamilyImportHandler` has the same `TrimStart('{').TrimEnd('}')`
pattern reachable with an empty payload) so an empty-object payload produces
valid JSON — e.g. only insert the comma+fields when `json <> "{}"`, or build
the frame via a proper JSON encoder (Thoth.Json.Net, already a project
dependency) instead of string surgery.

administration-vrc56's own new `importEventsStreamHandler` avoids this
specific landmine by never sending an empty-payload SSE event (no "start"
event; every event type it sends carries at least one field) — see that
handler's doc comment. This backlog item is scoped to the pre-existing
`projectionRebuildStreamHandler` bug only.

## Acceptance criteria
- [ ] A live SSE response from `/api/stream/rebuild-projection/{name}` for a
      successful rebuild produces syntactically valid JSON on every frame,
      confirmed via `JSON.parse` (not just Expecto over the F# function).
- [ ] The Projections tab's "Rebuild" button correctly shows the completed
      state (not a false failure message) after a real rebuild.
- [ ] Existing Expecto tests (`ProjectionRebuildTests.fs`) stay green.

## Notes
Root-caused and reported by administration-vrc56 during its own SSE handler
smoke-testing; not fixed there to keep that task's diff scoped to export/import.
