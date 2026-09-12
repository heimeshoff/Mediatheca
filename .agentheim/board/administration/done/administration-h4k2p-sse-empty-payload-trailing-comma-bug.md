---
id: administration-h4k2p
title: Fix trailing-comma malformed JSON in empty-payload SSE frames — extract one shared pure `sseFrame` helper the three SSE handlers call, so an empty-object payload can never emit `data: {"type":"complete",}`. Fixes the Projections-tab Rebuild button reporting every successful rebuild as a failure.
status: done
type: bug
context: administration
created: 2026-07-22
completed: 2026-07-22
depends_on: []
blocks: []
tags: [sse, event-store, projections, bug]
related_adrs: [0024, 0029]
related_research: []
prior_art: []
---

## Why
Discovered incidentally while smoke-testing administration-vrc56's NDJSON
export/import SSE routes against a real running server (not caught by any
existing Expecto test, since those exercise `Projection.rebuildProjectionWithProgress`
directly, never the actual SSE wire bytes — that gap is exactly why this shipped).

All three of the app's Server-Sent-Events handlers build each frame with the
same fragile string-surgery helper:

```fsharp
let writeEvent (eventType: string) (json: string) = task {
    let line = sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType (json.TrimStart('{').TrimEnd('}'))
    ...
```

When the payload is the empty object `"{}"`, `TrimStart('{').TrimEnd('}')`
reduces it to `""`, and the unconditional comma in the format string produces:

```
data: {"type":"complete",}
```

— a trailing comma, which is invalid JSON.

**Where it is actually live (source-grounded during refinement):** the only site
that passes an empty payload today is `Administration.projectionRebuildStreamHandler`
(`src/Server/Administration.fs:515`), which sends `do! writeEvent "complete" "{}"`
on a successful rebuild. Confirmed live:

```
$ curl -N http://localhost:PORT/api/stream/rebuild-projection/FriendProjection
data: {"type":"progress","position":0,"head":0,"eventsProcessed":0}

data: {"type":"complete",}
```

Client-side, `AdminProjections/State.fs`'s `runRebuildStream` calls
`JS.JSON.parse dataLine` **before** dispatching on `eventType`. The trailing
comma makes `JSON.parse` throw, which is caught by the outer
`with ex -> dispatch (Rebuild_failed (projectionName, ex.Message))` — so
**every successful projection rebuild currently reports itself to the UI as a
failure** (a JSON-parse error message), never as `Rebuild_completed`. The
rebuild itself still runs to completion server-side (the checkpoint is saved
correctly); only the UI's own completion signal is wrong.

**The other two SSE handlers share the identical helper but are not currently
reachable with an empty payload** (verified during refinement):
- `Api.steamFamilyImportHandler` (`src/Server/Api.fs:689`) — every `progress`/
  `complete`/`error` payload carries at least one field.
- `Administration.importEventsStreamHandler` (`src/Server/Administration.fs:452`,
  vrc56/ADR-0029) — deliberately dodges this landmine (no empty-payload event;
  see its doc comment). Its `complete` sends `{"eventsImported":N}`.

Both are latent copies of the same bug: one future `writeEvent "x" "{}"` call in
either handler reintroduces it. Rather than patch one call site, this task
removes the shared landmine.

## What
Extract the SSE frame-building into a **single pure helper** and route all three
handlers through it, so an empty-object payload can never produce a trailing
comma anywhere. The pure helper is the testability lever — it turns the wire
framing (previously inline and untestable without a live server) into a plain
function a plain Expecto test can pin.

Shape (builder-approved during refinement):

```fsharp
let sseFrame (eventType: string) (json: string) =
    let body = json.TrimStart('{').TrimEnd('}')
    if body = "" then sprintf "data: {\"type\":\"%s\"}\n\n" eventType
    else sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType body
```

Each handler's `writeEvent` becomes a thin task wrapper that calls `sseFrame`,
UTF-8 encodes, writes, and flushes — no handler keeps its own inline
`TrimStart('{').TrimEnd('}')` frame-building.

**Placement:** the helper needs a home both `Api.fs` and `Administration.fs` can
`open` (a small shared Server-side module — e.g. a new `Sse.fs`, or an existing
low-level module both already reference). Confirm the .fsproj compile order lets
both call sites see it; if not, place it earlier in the compile chain. This is a
worker implementation detail — no ADR needed (a bug-fix refactor, not a decision).

Scope is confined to SSE frame *framing*. Do not rework what each handler emits,
its event types, or its progress cadence.

## Acceptance criteria
- [ ] SSE frame-building is a single pure function (`sseFrame` or equivalent),
      and none of the three handlers retains its own inline
      `TrimStart('{').TrimEnd('}')` frame-building (grep-checkable: the string
      `TrimStart('{')` appears at most once in `src/Server/`, inside the helper).
- [ ] Expecto covers the pure helper directly: an empty-object payload `"{}"`
      (and `""`) yields `data: {"type":"complete"}\n\n` with **no** trailing
      comma; a non-empty payload keeps its fields
      (`sseFrame "progress" "{\"position\":0}"` → `data: {"type":"progress","position":0}\n\n`).
      Each asserted frame's JSON object round-trips through a JSON parse.
- [ ] A live `/api/stream/rebuild-projection/{name}` rebuild's `complete` frame
      parses via `JSON.parse` (satisfiable by the pure-helper Expecto test above;
      a Playwright spec on the ADR-0027 harness driving a real rebuild is the
      optional machine-checked upgrade).
- [ ] The Projections tab's "Rebuild" button shows the completed state (not a
      false failure message) after a real rebuild. [human-eye]
- [ ] Existing Expecto tests (`ProjectionRebuildTests.fs`) and the NDJSON
      round-trip tests (`EventStoreNdjsonTests.fs`) stay green.

## Notes
Root-caused and reported by administration-vrc56 during its own SSE handler
smoke-testing; not fixed there to keep that task's diff scoped to export/import.

Refinement (2026-07-22) source-grounded all three `writeEvent` sites and widened
scope from "the one projectionRebuildStreamHandler bug" to "extract a shared pure
`sseFrame` helper, all three handlers call it" (builder decision). The extra two
handlers are not currently reachable with an empty payload, so this is
defense-in-depth against the identical latent pattern, at a near-free cost.

All acceptance criteria are machine-checkable except the final Rebuild-button
observation, which is genuinely perceptual (`[human-eye]`, ADR-0061) — the pure
`sseFrame` Expecto test carries the actual correctness weight; the button check
is the operator confirming the fix lands end-to-end in the UI.

## Outcome
Extracted `Sse.sseFrame` (`src/Server/Sse.fs`, compiled just before `Api.fs` in
`Server.fsproj`) as the single pure SSE frame-building function: it branches on
whether the trimmed payload body is empty, so `sseFrame "complete" "{}"` now
yields `data: {"type":"complete"}\n\n` with no trailing comma, while a
non-empty payload keeps splicing its fields in exactly as before. All three
`writeEvent` call sites (`Api.steamFamilyImportHandler`,
`Administration.importEventsStreamHandler`,
`Administration.projectionRebuildStreamHandler`) now delegate to it instead of
each doing its own inline `TrimStart('{').TrimEnd('}')` string surgery — that
literal string now appears exactly once in `src/Server/` (inside the helper
itself), grep-verified.

Added `tests/Server.Tests/SseTests.fs` (4 Expecto tests, wired into
`Server.Tests.fsproj`): empty-object and empty-string payloads both parse via
`System.Text.Json.JsonDocument.Parse` with no trailing comma, a single-field
and a multi-field non-empty payload both keep their fields and round-trip
through the same parser. Confirmed red/green: temporarily reverted
`sseFrame` to the old unconditional-comma implementation, saw exactly the
2 empty-payload tests fail with the documented `data: {"type":"complete",}`
artifact, then restored the fix — all 4 new tests plus the full 372-test
Expecto suite (including `ProjectionRebuildTests.fs` and
`EventStoreNdjsonTests.fs`) pass.

No client-side change was needed: the client already does
`JS.JSON.parse dataLine` before dispatching, so a now-valid JSON `complete`
frame lets `AdminProjections/State.fs`'s `runRebuildStream` reach
`Rebuild_completed` instead of the JSON-parse-error catch branch. No ADR
written (per this task's own Notes: a bug-fix refactor, not a decision). No
BC README change (pure internal implementation detail — no ubiquitous
language, aggregate, or event changed).

Key files: `src/Server/Sse.fs`, `src/Server/Api.fs`,
`src/Server/Administration.fs`, `src/Server/Server.fsproj`,
`tests/Server.Tests/SseTests.fs`, `tests/Server.Tests/Server.Tests.fsproj`.
