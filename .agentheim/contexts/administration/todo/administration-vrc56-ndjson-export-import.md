---
id: administration-vrc56
title: Event log export/import as NDJSON — stream out/in via plain Giraffe routes, preserving exact global_position, into an empty store only
status: todo
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: [administration-n8kqw]
tags: [admin-console, event-store, backup, export, import, ndjson, streaming]
related_adrs: [0002, 0003, 0024, 0025]
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
- [ ] Export of a seeded store produces one NDJSON line per event, in `global_position` ascending order, with the fixed field order and `data`/`metadata` as JSON-escaped strings (not nested objects).
- [ ] Export walks `EventStore.readAllForward`'s existing batching and never builds the full log as one in-memory collection/string.
- [ ] Round-trip test: seed store A (varied streams/types, payloads containing quotes/unicode/newlines to stress escaping), export to a string, import into fresh store B, export B — the two NDJSON strings are exactly string-equal.
- [ ] Import into a fresh store, followed by an explicit rebuild via the existing Rebuild-all control, yields projections identical to store A's.
- [ ] Import into a non-empty store is refused before any row is written or the body fully consumed.
- [ ] `global_position` is preserved exactly on import; appending a new event afterward continues from `(imported max global_position) + 1`.
- [ ] `events_fts` is searchable immediately after import (a distinctive substring of an imported event's `data` is found via `queryEventPage`'s `Search` filter) with no separate manual FTS rebuild step.
- [ ] A malformed line partway through an uploaded import rolls back the whole transaction; the target store is left empty.
- [ ] Admin UI offers working Export/Import controls and surfaces the refusal message for a non-empty-store import attempt.

## Notes
Concurrency guard scope is open: import mutates the whole `events` table over the shared `SqliteConnection` and should likely take a `rebuildingProjections`-style module-level guard, and should probably mutually exclude with an in-progress projection rebuild (both hit the same connection). Decide during implementation whether this guard is local to this task or shared with administration-wwc36's surgery guardrails. The app-wide version of this same shared-connection concurrency question is being audited under administration-cx92m — if that audit lands a global gate or per-operation-connection model first, this import path should conform to it rather than growing a bespoke guard.

Consider drafting a new ADR (next available: 0028, confirm at write time) covering: opaque-string payload embedding, the `appendToStream` bypass with explicit `global_position`, the "leave dirty, reuse Rebuild-all" choice, and the export/import transport asymmetry (plain stream vs. SSE).

Scope split: this task covers export + import-into-empty-store only. Import that overwrites a non-empty store is administration-n8kqw, gated behind administration-wwc36's auto-backup guardrail.
