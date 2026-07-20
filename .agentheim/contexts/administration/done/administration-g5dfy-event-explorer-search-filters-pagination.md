---
id: administration-g5dfy
title: Event explorer — FTS payload search, time/position/BC filters, keyset pagination
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-20
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, search, fts5]
related_adrs: [0002, 0003, 0020]
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
- [x] Free-text search over event payloads returns matching events (verified with a term that appears only in `data`, not in stream/type).
- [x] Time-range and BC filters narrow results correctly and compose with existing stream/type filters.
- [x] Paging through more events than one page works forward and backward without skipping or duplicating rows (keyset, not offset).
- [x] FTS index creation is idempotent across server restarts and covers pre-existing events.
- [x] Expecto tests cover the new `EventStore` query paths (in-memory SQLite).

## Notes
`EventStore.queryEvents` (src/Server/EventStore.fs:108) is the seam to extend or replace. Keep raw LIKE fallback if FTS5 is unavailable? — SQLite bundled with Microsoft.Data.Sqlite includes FTS5, so no fallback needed; verify once in a test.

See ADR-0020 for the FTS5 external-content index design (including a real gotcha in detecting whether backfill is needed), and the keyset/client-cursor-stack pagination design.

## Outcome
Replaced the fixed LIKE/limit-offset event query with a composable, keyset-paginated query engine:

- **FTS5 search:** `events_fts` (external-content, `content='events'`) with an `AFTER INSERT` trigger keeps it in sync going forward; `EventStore.createFtsIndex` backfills pre-existing rows via FTS5's `('rebuild')` command, gated on a `sqlite_master` existence check (not a row-count comparison — see ADR-0020 for why that's wrong for external-content tables).
- **Filters:** `EventFilter` (search, stream, event type, bounded context, timestamp range) — all compose via a single SQL WHERE clause in `EventStore.queryEventPage`. Bounded-context names resolve to `stream_id` prefixes in `Administration.boundedContextPrefixes`, keeping `EventStore.fs` decoupled from domain BC naming.
- **Pagination:** keyset on `global_position`, newest-first. Server exposes only a "before" (older) direction; the client (`EventBrowser.State`) tracks a `CursorStack` of visited cursors so `Prev_page` can pop back without a second server-side direction.
- **`IAdminApi`:** `getEvents`/`EventQuery` replaced with `getEventPage: EventPageQuery -> Async<EventPage>`; added `getBoundedContexts`.
- Client (`EventBrowser` Types/State/Views) gained a search box, BC filter dropdown, date-range inputs, page-size selector, and Prev/Next controls with a "showing X-Y of Z" indicator.

Tests: 8 new Expecto tests (EventStoreTests.fs: search, BC-prefix filter, timestamp-range filter, composed filters, keyset pagination correctness, FTS backfill idempotency; AdministrationTests.fs: getEventPage through IAdminApi, BC filter resolution, getBoundedContexts). 299/299 passing (up from 291 baseline). `npm run build` passes.

Key files:
- `src/Server/EventStore.fs` — `createFtsIndex`, `QueryFilter`, `emptyQueryFilter`, `queryEventPage`
- `src/Server/Administration.fs` — `boundedContextPrefixes`, `getEventPage`, `getBoundedContexts`
- `src/Shared/Shared.fs` — `EventFilter`, `EventFilter.empty`, `EventPageQuery`, `EventPage`, updated `IAdminApi`
- `src/Client/Pages/EventBrowser/Types.fs`, `State.fs`, `Views.fs`
- `tests/Server.Tests/EventStoreTests.fs`, `AdministrationTests.fs`
- `.agentheim/knowledge/decisions/0020-event-explorer-fts5-search-and-keyset-pagination.md`
- `.agentheim/contexts/administration/README.md`
