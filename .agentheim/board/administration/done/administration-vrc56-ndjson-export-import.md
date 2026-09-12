---
id: administration-vrc56
title: Event log export/import as NDJSON — stream out/in via plain Giraffe routes, preserving exact global_position, into an empty store only
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-22
depends_on: [administration-p0jka, design-system-001]
blocks: [administration-n8kqw]
tags: [admin-console, event-store, backup, export, import, ndjson, streaming]
related_adrs: [0002, 0003, 0024, 0025, 0029]
related_research: []
prior_art: []
---

## Why
The event log is the system of record, and it currently has no portable form: no backup format besides copying the db file, no way to move history between environments (dev ↔ prod), and no substrate for copy-on-write log transformations (rewrite the log through a script into a fresh store, then swap). This task covers the non-destructive half: export, and import into an *empty* store only. Import that overwrites an existing store is a separate, more dangerous operation — see administration-n8kqw.

## What
- **Export** (`GET /api/stream/export-events`): streams the full event log as NDJSON, one line per event in `global_position` ascending order, `Content-Type: application/x-ndjson`, `Content-Disposition: attachment`. A plain streamed body, not SSE — SSE's `data: {...}` framing exists for *progress* reporting (steamFamilyImportHandler's precedent), and would force a second layer of escaping onto every NDJSON line. Built on `EventStore.readAllForward`'s existing batched reads; never materializes the whole log in memory.
- **NDJSON line schema**, fixed field order (mirrors `StoredEvent`/the `events` table column order):
  `{"globalPosition":<int64>,"streamId":<string>,"streamPosition":<int64>,"eventType":<string>,"data":<string>,"metadata":<string>,"timestamp":<string ISO-8601>}`
  `data`/`metadata` are embedded as **JSON-escaped string values holding the literal `events.data`/`events.metadata` TEXT content** — not reparsed and re-nested as JSON objects. This is deliberate: JSON string escape/unescape is a lossless bijection, so it guarantees the round-trip is byte-stable without depending on canonical-JSON-serialization matching between whatever wrote the original payload and whatever writes it back.
- **Import** (`POST /api/stream/import-events`): request body *is* the NDJSON (no multipart wrapper — one file, no companion fields), read line-by-line so the file is never buffered whole. Response is SSE progress (steamFamilyImportHandler's envelope), since total line count is unknown up front — progress is a running count, not a percentage bar.
  - **Refuses immediately** if the target store already has events (`EventStore.getTotalEventCount conn > 0`), before consuming the uploaded body.
  - Inserts explicit `global_position` values, bypassing `EventStore.appendToStream` (which recomputes stream_position/timestamp and has no notion of "preserve this exact position") via a new `EventStore.importEvents`/`importNdjson`. Whole import runs in **one transaction** — malformed input rolls back everything, target store is left empty (never partially populated).
  - Does **not** self-trigger a projection rebuild. Checkpoints stay untouched, so the store reads as dirty via the existing ADR-0025 lag-detection; the operator runs the existing Rebuild-all control (administration-qjcp4) afterward. Reuses verified machinery instead of a second rebuild-orchestration implementation.
- **Export/import logic is Giraffe-decoupled**: `EventStore.exportNdjson (conn) (writer: TextWriter)` / `EventStore.importNdjson (conn) (reader: TextReader) : Result<...>`, with the Giraffe handlers as thin wrappers over `ctx.Response.Body`/`ctx.Request.Body`. This is what makes the round-trip test a plain Expecto test, no HTTP pipeline needed.
- Admin UI: an Export download control and an Import upload control on the admin console (paper-overlay conventions per design-system-001), surfacing the non-empty-store refusal as a visible message rather than a raw error.

## Acceptance criteria
- [x] Export of a seeded store produces one NDJSON line per event, in `global_position` ascending order, with the fixed field order and `data`/`metadata` as JSON-escaped strings (not nested objects).
- [x] Export walks `EventStore.readAllForward`'s existing batching and never builds the full log as one in-memory collection/string.
- [x] Round-trip test: seed store A (varied streams/types, payloads containing quotes/unicode/newlines to stress escaping), export to a string, import into fresh store B, export B — the two NDJSON strings are exactly string-equal.
- [x] Import into a fresh store, followed by an explicit rebuild via the existing Rebuild-all control, yields projections identical to store A's.
- [x] Import into a non-empty store is refused before any row is written or the body fully consumed.
- [x] `global_position` is preserved exactly on import; appending a new event afterward continues from `(imported max global_position) + 1`.
- [x] `events_fts` is searchable immediately after import (a distinctive substring of an imported event's `data` is found via `queryEventPage`'s `Search` filter) with no separate manual FTS rebuild step.
- [x] A malformed line partway through an uploaded import rolls back the whole transaction; the target store is left empty.
- [x] Admin UI offers working Export/Import controls and surfaces the refusal message for a non-empty-store import attempt.

## Notes
Concurrency guard scope: decided **not** to add a bespoke module-level guard (e.g. a `rebuildingProjections`-style lock) for import in this task. None of the acceptance criteria require it, and the Notes' own guidance was to defer to administration-cx92m (the app-wide shared-connection concurrency audit) rather than grow a one-off guard ahead of that audit landing a global answer. If administration-cx92m lands a global gate or per-operation-connection model, this import path should conform to it then.

ADR-0029 (`.agentheim/knowledge/decisions/0029-ndjson-event-log-export-import.md`) covers: opaque-string payload embedding, the `appendToStream` bypass with explicit `global_position`, the "leave dirty, reuse Rebuild-all" choice, the export/import transport asymmetry (plain stream vs. SSE), and two bugs found via live-server smoke testing (Kestrel's synchronous-IO restriction, and a pre-existing SSE trailing-comma bug filed as administration-h4k2p).

Scope split: this task covers export + import-into-empty-store only. Import that overwrites a non-empty store is administration-n8kqw, gated behind administration-wwc36's auto-backup guardrail.

## Outcome
Added `EventStore.exportNdjson`/`importNdjson` (plain `TextWriter`/`TextReader` interface, Giraffe-decoupled), wired behind two new raw Giraffe routes (`GET /api/stream/export-events`, `POST /api/stream/import-events`) in `Administration.fs`/`Composition.fs`, plus a "Backup" section on the AdminProjections tab UI (export download link, import file upload with SSE-consumed outcome messages, adjacent to the existing Rebuild-all button). 8 new Expecto tests (`EventStoreNdjsonTests.fs`) cover every acceptance criterion, all passing; full suite (366 tests) and `npm run build` stay green.

Went beyond Expecto coverage to smoke-test the real HTTP routes against a live server (two temp-`DATA_DIR` instances), which surfaced two real bugs invisible to the in-process tests: (1) Kestrel's default ban on synchronous request/response-body I/O, which the pinned synchronous `TextWriter`/`TextReader` interface collides with once the store is large enough to overflow `StreamWriter`'s internal buffer — fixed via `IHttpBodyControlFeature.AllowSynchronousIO` in both Giraffe handlers; (2) a pre-existing trailing-comma JSON bug in `projectionRebuildStreamHandler`'s empty-payload `"complete" "{}"` SSE event (predates this task, part of administration-qjcp4) that makes the Projections tab's Rebuild button always report success as a client-side failure — avoided in this task's own import handler (no empty-payload event sent) and filed separately as `administration-h4k2p` rather than fixed here, to keep this task's diff scoped to export/import. Verified live: full round-trip (export unicode-bearing store → import into empty store → re-export) byte-identical over real HTTP, and the non-empty-store refusal via real HTTP.

Key files: `src/Server/EventStore.fs` (`exportNdjson`, `importNdjson`, `ImportOutcome`, `ImportFailure`), `src/Server/Administration.fs` (`exportEventsStreamHandler`, `importEventsStreamHandler`, `allowSynchronousIO`), `src/Server/Composition.fs` (route wiring), `src/Client/Pages/AdminProjections/{Types,State,Views}.fs` (Backup section), `tests/Server.Tests/EventStoreNdjsonTests.fs`, `.agentheim/knowledge/decisions/0029-ndjson-event-log-export-import.md`, `.agentheim/contexts/administration/backlog/administration-h4k2p-sse-empty-payload-trailing-comma-bug.md`.
