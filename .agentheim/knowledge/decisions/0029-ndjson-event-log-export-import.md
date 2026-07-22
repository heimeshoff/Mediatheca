---
id: 0029
title: Event log export/import as NDJSON — opaque-string payloads, explicit-rowid position preservation, leave-dirty-and-reuse-Rebuild-all, and a plain-stream/SSE transport split
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-vrc56]
related_research: []
---

# ADR 0029: Event log export/import as NDJSON

## Context

administration-vrc56 gives the event log its first portable form: export the
full log as NDJSON, and import an NDJSON export into a store that is
currently empty (import into a non-empty store is a separate, more
dangerous operation — administration-n8kqw). Several design questions came
up that needed a deliberate answer rather than a default:

1. How should `events.data`/`events.metadata` (already-JSON TEXT columns)
   be embedded in each NDJSON line's JSON object?
2. How should `global_position` — an `INTEGER PRIMARY KEY AUTOINCREMENT`
   rowid — be preserved exactly across export/import, given the existing
   `EventStore.appendToStream` recomputes stream position and timestamp and
   has no notion of "insert this exact value"?
3. Should import trigger a projection rebuild automatically, or leave that
   to the operator?
4. Should export and import use the same transport (both plain streams, or
   both SSE), or different ones?

## Decision

### `data`/`metadata` are opaque JSON-escaped string values, never re-nested as JSON objects

Each NDJSON line is a fixed-field-order JSON object:

```
{"globalPosition":<int64>,"streamId":<string>,"streamPosition":<int64>,"eventType":<string>,"data":<string>,"metadata":<string>,"timestamp":<string>}
```

`data`/`metadata` hold the literal `events.data`/`events.metadata` TEXT
content, JSON-string-escaped — not reparsed and re-embedded as a nested JSON
object. A JSON string escape/unescape is a lossless bijection: whatever
bytes went in come back out exactly, with no dependency on canonical-JSON
serialization agreeing between whatever originally wrote the payload and
whatever writes it back during re-export. Re-nesting as an object would
require parsing arbitrary historical payload shapes (which may not even be
uniform across a log spanning schema changes) and re-serializing them,
risking silent reformatting (key order, whitespace, number formatting) that
breaks the byte-stable round-trip the acceptance criteria require.

`globalPosition`/`streamPosition` are written as bare JSON numbers (built
via `sprintf "%d"`, not through Thoth's `Encode.int64` — Thoth deliberately
encodes `int64` as a JSON *string* to protect JS callers from IEEE-754
precision loss above 2^53; this schema has no JS consumer and the task's own
schema notation writes `<int64>` unquoted). The rest of each line's string
fields are still built through `Thoth.Json.Net.Encode.string`, so the
escaping correctness of a well-exercised library is reused rather than
hand-rolled string replacement.

### `global_position` preserved via an explicit-rowid INSERT, bypassing `appendToStream`

`EventStore.importNdjson` inserts each row with `global_position` set
explicitly from the NDJSON line, rather than going through
`EventStore.appendToStream` (which computes `stream_position` from the
current stream head and stamps `timestamp = DateTimeOffset.UtcNow`, with no
parameter for "use this exact position/timestamp instead"). Verified
directly against SQLite (`Microsoft.Data.Sqlite` 9.x) before implementing:
inserting an explicit `global_position` value into an `INTEGER PRIMARY KEY
AUTOINCREMENT` column updates SQLite's `sqlite_sequence` bookkeeping to that
value when it exceeds the current tracked maximum — so a subsequent ordinary
`appendToStream` call after import correctly continues from
`(imported max global_position) + 1`, with no separate "reset the sequence"
step needed.

### Import leaves projections dirty; the operator reuses the existing Rebuild-all control

`importNdjson` does not touch `projection_checkpoints`. Checkpoints stay at
whatever they were before import (typically all zero, on the common "import
into a truly fresh store" path), so the store immediately reads as dirty via
the existing ADR-0025-style lag detection (checkpoint vs.
`EventStore.getMaxGlobalPosition`), and the operator runs the existing
Rebuild-all control (administration-qjcp4, ADR-0024) to bring every
projection up to date. Self-triggering a rebuild from inside import was
considered and rejected: it would duplicate rebuild orchestration
(concurrency guard, per-projection sequencing, progress reporting) that
`Administration.projectionRebuildStreamHandler` and
`AdminProjections.State`'s `PendingRebuildAllQueue` already implement and
that ADR-0024 already reasoned through carefully. Reusing verified machinery
is simpler and safer than a second, import-specific rebuild path — and the
AdminProjections UI's Backup section makes the "import, then Rebuild all"
sequence a two-click adjacency on the same tab.

### Export/import logic is Giraffe-decoupled: `TextWriter`/`TextReader`, not `Stream`/`HttpContext`

`EventStore.exportNdjson (conn) (writer: TextWriter) : unit` and
`EventStore.importNdjson (conn) (reader: TextReader) : Result<ImportOutcome, ImportFailure>`
take plain BCL text-stream types, not Giraffe/ASP.NET Core types. The
Giraffe routes (`Administration.exportEventsStreamHandler`/
`importEventsStreamHandler`) are thin wrappers that adapt
`ctx.Response.Body`/`ctx.Request.Body` into a `StreamWriter`/`StreamReader`.
This is what makes the round-trip test (`EventStoreNdjsonTests.fs`) a plain
Expecto test against `StringWriter`/`StringReader` — no HTTP pipeline, no
Kestrel, no test server — while the actual route wiring was still verified
live (see Consequences).

### Export is a plain streamed download; import is SSE progress — deliberately different transports

Export (`GET /api/stream/export-events`) is a plain streamed HTTP response
body (`Content-Type: application/x-ndjson`, `Content-Disposition: attachment`),
not SSE. SSE's `data: {...}\n\n` framing exists for *progress* reporting
(as `Api.steamFamilyImportHandler` and
`Administration.projectionRebuildStreamHandler` already use it); wrapping
NDJSON — itself already one JSON object per line — inside SSE's own framing
would force a second, redundant layer of escaping onto every line for no
benefit, since export has no meaningful intermediate progress to report
beyond "still streaming." Import (`POST /api/stream/import-events`) *is* SSE,
matching those two precedents, because it needs to report an outcome
(`complete`/`rejected`/`error`) after a request whose total line count is
unknown up front — there is no percentage bar, just an outcome event. Unlike
those two precedents, import here sends **no intermediate "start" event**:
see Consequences for why an empty-payload SSE event is actively harmful with
this codebase's existing `writeEvent` helper pattern, and import's own
work is one atomic transaction with no natural point to report partial
progress from anyway.

## Consequences

### Positive
- The round-trip (export A → import into empty B → export B) is exactly
  string-equal, verified both in Expecto (varied streams/types, payloads
  with quotes/unicode/newlines) and live over a real HTTP round trip during
  implementation (curl export → curl import → curl re-export, byte-diffed).
- `global_position` continuity after import needs no special-cased "next
  position" bookkeeping — SQLite's own `AUTOINCREMENT` semantics handle it.
- A malformed line anywhere in the upload rolls back the *entire* import in
  one transaction — the target store is never left partially populated.

### Negative / things discovered during implementation
- **Kestrel disallows synchronous I/O on the request/response body by
  default.** `EventStore.exportNdjson`/`importNdjson`'s synchronous
  `TextWriter`/`TextReader` interface — chosen specifically so the
  round-trip logic is plain-Expecto testable — collides with this once a
  real Kestrel response/request stream is involved: `StreamWriter.WriteLine`
  (once its internal buffer fills) or `StreamReader.ReadLine` throws
  `InvalidOperationException: Synchronous operations are disallowed`. This
  was **not** caught by the Expecto suite (which only ever supplies a
  `StringWriter`/`StringReader`) — it surfaced only when this task
  smoke-tested the real routes with a store large enough to overflow
  `StreamWriter`'s ~1KB internal buffer (reproduced with 30 seeded events;
  invisible with 2). Fixed by opting the request into Kestrel's own escape
  hatch (`IHttpBodyControlFeature.AllowSynchronousIO <- true`) inside each
  Giraffe handler, rather than making the storage-layer functions async —
  this still streams (batched reads on export, line-by-line on import); it
  only relaxes Kestrel's thread-starvation guard against a blocking sync
  call, it does not force any in-memory buffering of the whole body.
- **An empty-payload SSE event (`"{}"`) produces invalid JSON with this
  codebase's existing `writeEvent` helper.** `sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType (json.TrimStart('{').TrimEnd('}'))`
  reduces `"{}"` to an empty string, yielding `{"type":"...",}"` — a
  trailing comma that `JSON.parse` rejects. Discovered live (curl against
  `/api/stream/rebuild-projection/{name}`) while smoke-testing this task's
  own SSE route, which briefly used the same pattern for a "start" event.
  This is a **pre-existing bug in `projectionRebuildStreamHandler`'s
  `"complete" "{}"` call** (predating this task, part of administration-qjcp4/
  ADR-0024) — it means the Projections tab's Rebuild button currently always
  reports a successful rebuild as a client-side failure (a JSON-parse
  exception), even though the rebuild itself completes correctly
  server-side. Filed as `administration-h4k2p` rather than fixed here, to
  keep this task's diff scoped to export/import; this task's own import
  handler avoids the landmine by sending no empty-payload event at all
  (dropped the "start" event, since nothing meaningfully consumed it and
  import has no intermediate progress to report before its one all-or-
  nothing transaction resolves).
- Import leaving projections dirty means a forgotten Rebuild-all after
  import silently leaves stale read models — mitigated by the existing lag
  indicator on the Projections tab (ADR-0025's dirty-detection pattern) and
  by the Backup section's own success message explicitly telling the
  operator to run Rebuild-all next.

### Neutral
- `globalPosition`/`streamPosition` as bare JSON numbers (not
  Thoth-string-encoded int64) is a schema choice specific to this
  file-format's own consumer (this codebase's own `importNdjson`) — a future
  export consumer expecting all-numeric-fields-as-strings (the more common
  JS-safe convention) would need to know this schema deviates from that
  default.

## Alternatives considered

- **Re-parse `data`/`metadata` and re-embed as nested JSON objects.**
  Rejected: risks silent reformatting during re-serialization (breaks
  byte-stable round-trip) and requires the export/import code to understand
  every historical payload shape across schema evolution, which the opaque
  string approach never needs to.
- **Self-trigger a projection rebuild at the end of import.** Rejected:
  duplicates rebuild orchestration ADR-0024 already solved (guard,
  sequencing, progress); reusing the existing Rebuild-all control is less
  code and one less place bugs can diverge between "rebuild after import"
  and "rebuild on demand."
- **SSE for export too (uniform transport with import).** Rejected: would
  add a redundant escaping layer around already-JSON NDJSON lines for no
  benefit, since export has no meaningful partial-progress signal to report.
- **Make `EventStore.exportNdjson`/`importNdjson` async (`Stream`-based)
  instead of fixing Kestrel's sync-IO restriction at the handler layer.**
  Rejected: the task's own architecture requirement pins these functions to
  a plain synchronous `TextWriter`/`TextReader` signature specifically so the
  round-trip test stays a plain Expecto test with no async/HTTP-pipeline
  ceremony; `AllowSynchronousIO` is a smaller, well-precedented fix that
  keeps that testability intact.

## References

- `src/Server/EventStore.fs` — `exportNdjson`, `importNdjson`,
  `ImportOutcome`, `ImportFailure`.
- `src/Server/Administration.fs` — `exportEventsStreamHandler`,
  `importEventsStreamHandler`, `allowSynchronousIO`.
- `src/Server/Composition.fs` — route wiring
  (`/api/stream/export-events`, `/api/stream/import-events`).
- `src/Client/Pages/AdminProjections/` — the Backup section
  (`Views.backupSection`), `State.runImportStream`.
- `tests/Server.Tests/EventStoreNdjsonTests.fs` — round-trip, batching,
  refusal, malformed-line-rollback, FTS-searchable-after-import, and
  continue-from-max-position cases.
- `administration-h4k2p` — the pre-existing SSE trailing-comma bug in
  `projectionRebuildStreamHandler`, discovered and filed (not fixed) here.
- ADR-0002 — event sourcing + CQRS, why projections are disposable/
  rebuildable, the premise "leave dirty, reuse Rebuild-all" leans on.
- ADR-0003 — SQLite/WAL baseline.
- ADR-0024 — the Rebuild-all control this task reuses instead of
  self-triggering a rebuild.
- ADR-0025 — the admin-owned dirty/lag-detection pattern this task's
  "leave dirty" choice relies on to surface staleness to the operator.
