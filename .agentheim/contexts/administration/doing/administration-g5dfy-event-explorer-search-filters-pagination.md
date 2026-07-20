---
id: administration-g5dfy
title: Event explorer — FTS payload search, time/position/BC filters, keyset pagination
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, search, fts5]
related_adrs: [0002, 0003]
related_research: []
prior_art: []
---

## Why
The event browser currently offers only two LIKE-dropdowns (stream, event type) and a fixed result limit — no way to find "every event mentioning blade-runner", narrow to a date range, or page through history. To navigate an append-only log that only grows, search and pagination are table stakes.

## What
- **Payload search:** SQLite FTS5 index over `events.data` (external-content table, populated by trigger or on append), exposed as a free-text search box. Migration added in `EventStore.initialize` (idempotent, backfills existing rows).
- **New filter axes:** timestamp range (from/to), global-position range, and bounded context (derived from stream-id prefix: `Movie-`, `Series-`, `Game-`, `Friend-`, `Catalog-`, `ContentBlocks-`).
- **Keyset pagination:** replace fixed limit/offset with cursor pagination on `global_position` (newest-first), prev/next controls, page size selector, total-match count.
- Filters compose (search + stream + type + time range together) and are reflected in the query sent over `IAdminApi`.

## Acceptance criteria
- [ ] Free-text search over event payloads returns matching events (verified with a term that appears only in `data`, not in stream/type).
- [ ] Time-range and BC filters narrow results correctly and compose with existing stream/type filters.
- [ ] Paging through more events than one page works forward and backward without skipping or duplicating rows (keyset, not offset).
- [ ] FTS index creation is idempotent across server restarts and covers pre-existing events.
- [ ] Expecto tests cover the new `EventStore` query paths (in-memory SQLite).

## Notes
`EventStore.queryEvents` (src/Server/EventStore.fs:108) is the seam to extend or replace. Keep raw LIKE fallback if FTS5 is unavailable? — SQLite bundled with Microsoft.Data.Sqlite includes FTS5, so no fallback needed; verify once in a test.
