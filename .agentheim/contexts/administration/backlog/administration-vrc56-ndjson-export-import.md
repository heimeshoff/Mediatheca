---
id: administration-vrc56
title: Event log export/import as NDJSON
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, backup, export]
related_adrs: [0002, 0003]
related_research: []
prior_art: []
---

## Why
The event log is the system of record, and it currently has no portable form: no backup format besides copying the db file, no way to move history between environments (dev ↔ prod), and no substrate for copy-on-write log transformations (rewrite the log through a script into a fresh store, then swap).

## What
- **Export:** download the full event log as NDJSON (one event per line: global position, stream id, stream position, type, data, metadata, timestamp). Streamed response, not built in memory.
- **Import:** upload an NDJSON file into an *empty* store (refuse if events exist, or offer explicit wipe-first with the surgery-grade backup guardrail), preserving stream ids, positions, types, payloads, and timestamps; then rebuild all projections.
- Round-trip fidelity: export → import into fresh store → identical `events` table content (modulo `global_position` autoincrement values — decide whether to preserve them; preserving is better for references).

## Acceptance criteria
- [ ] Export of a seeded store produces valid NDJSON with one line per event.
- [ ] Import into a fresh store followed by projection rebuild yields projections identical to the source system.
- [ ] Import into a non-empty store is refused (or gated behind explicit wipe + backup).
- [ ] Round-trip test (export → import → export) is byte-stable.

## Notes
Needs refinement: whether to preserve `global_position` on import (INSERT with explicit rowid preserves it — preferred) and how the upload travels (Fable.Remoting handles byte arrays, but a plain Giraffe route with multipart streaming may be saner for large files — the `/api/stream/import-steam-family` route shows the non-Remoting precedent). Also the natural home for a scheduled auto-export later.
