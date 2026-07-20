---
id: 0020
title: Event explorer uses FTS5 external-content search and client-tracked keyset pagination
scope: administration
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [administration-g5dfy]
related_research: []
---

# ADR 0020: Event explorer uses FTS5 external-content search and client-tracked keyset pagination

## Context

The event browser (administration-p0jka) offered only two LIKE-dropdowns and a
fixed limit/offset — no payload search, no time/position/BC filtering, and
offset pagination that skips or duplicates rows as the append-only log grows
underneath it. administration-g5dfy adds free-text payload search, composable
filters (stream, event type, bounded context, timestamp range), and correct
pagination — and does it in a way `administration-mtf1f` (live tail: "everything
after global position N with these same filters") can extend without a rewrite.

## Decision

### FTS5 external-content index

`events_fts` is an FTS5 virtual table with `content='events', content_rowid=
'global_position'` — it stores no copy of `events.data`, only the inverted
index, and is kept in sync by an `AFTER INSERT` trigger (no update/delete
trigger needed: events are immutable and never removed). Migration lives in
`EventStore.createFtsIndex`, called from `EventStore.initialize` alongside
`createTables`.

Free-text search input is never handed to FTS5's own query-language parser —
it's wrapped as a quoted phrase (`toFtsPhraseQuery`) so punctuation and FTS5
operator characters in ordinary search terms (`blade-runner`, `friend's`)
don't throw syntax errors.

**Backfill/idempotency signal — a real gotcha:** the migration checks whether
`events_fts` already exists in `sqlite_master` *before* creating it, and only
runs FTS5's `INSERT INTO events_fts(events_fts) VALUES ('rebuild')` (full
resync from the content table) when it didn't. The first implementation
instead compared `COUNT(*) FROM events` against `COUNT(*) FROM events_fts` and
rebuilt on mismatch — this is wrong and silently never rebuilds: an unfiltered
`COUNT(*)`/`SELECT` against an external-content FTS5 table is satisfied by
enumerating the content table's rowids directly, without consulting the
inverted index at all. A freshly created, entirely unindexed `events_fts`
table already reports the "correct" row count, so the mismatch never fires and
`MATCH` queries silently return nothing. Caught by a test that drops the FTS
objects (simulating a pre-migration database), re-runs `initialize`, and
asserts a search actually finds the pre-existing row — a bare row-count
assertion on `events_fts` would not have caught this.

### Keyset pagination, one direction

`EventStore.queryEventPage` pages strictly newest-first via `global_position <
@before`. There is deliberately no server-side "after" (ascending) direction
for going backward through pages already seen — the client
(`EventBrowser.State`) keeps a `CursorStack` of the `Before` cursor that
produced each page it has visited, and `Prev_page` pops it. This keeps the
server-side keyset logic to one direction and one WHERE clause, at the cost of
the client owning pagination history (acceptable: it's a single Elmish model,
not shared state).

`queryEventPage` fetches `pageSize + 1` rows to derive `HasMore` without a
second query, and runs one `COUNT(*)` (over the same filter WHERE clause, no
keyset condition) for `TotalMatches`.

### Filter shape is shared with the future live-tail query

`EventFilter` (Shared.fs) and `EventStore.QueryFilter` (server-internal
mirror, deliberately not identical — see below) hold every filter dimension:
search, stream, event type, bounded context, timestamp range.
`administration-mtf1f`'s live-tail query is expected to reuse `EventFilter`
as-is and add its own ascending/"after" pagination query rather than
introducing a second filter shape.

`EventStore.QueryFilter.StreamPrefix` is a resolved `stream_id` prefix (e.g.
`"Movie-"`), not a bounded-context name — the BC-name-to-prefix table
(`Administration.boundedContextPrefixes`, mirroring each BC's own `streamId`
helper: `Movies.streamId`, `Series.streamId`, etc.) lives in
`Administration.fs`, not `EventStore.fs`, so the event store itself stays
decoupled from domain BC naming conventions. `Administration.create` resolves
`EventFilter.BoundedContext` to `QueryFilter.StreamPrefix` before calling
`EventStore.queryEventPage`.

### API surface change

`IAdminApi.getEvents: EventQuery -> Async<EventDto list>` was replaced (not
kept alongside) with `getEventPage: EventPageQuery -> Async<EventPage>`, and
`getBoundedContexts: unit -> Async<string list>` was added. `getEvents`/
`EventQuery` had exactly one consumer (`EventBrowser`, itself being upgraded
by this task) since landing minutes earlier in administration-p0jka, so there
was no reason to carry the old shape forward as dead code.

## Consequences

### Positive
- Payload search works without a fragile LIKE-scan over `events.data`, and
  scales as the log grows (FTS5 index, not a table scan).
- Pagination is correct under concurrent appends (no skipped/duplicated rows,
  unlike offset pagination against a growing table).
- The filter shape is already the right shape for administration-mtf1f to
  extend.

### Negative
- Two filter-shaped types to keep in sync by hand (`Shared.EventFilter` and
  `EventStore.QueryFilter`) — deliberate (see Alternatives), but is a seam a
  future worker must remember exists.
- Client owns pagination history (`CursorStack`); if the Admin console ever
  needs deep-linkable pagination (e.g. a URL with a page number), this cursor
  stack does not survive a page reload. Not needed today (admin tool, not a
  bookmarked user-facing view).

### Neutral
- The FTS backfill runs the O(n) `rebuild` at most once per genuinely
  unmigrated database; every subsequent restart is two cheap existence/no-op
  checks.

## Alternatives considered

- **Rebuild `events_fts` on every `COUNT(*)` mismatch** — this was the first
  implementation and is wrong; see the gotcha above. Existence-check against
  `sqlite_master` is both simpler and correct.
- **Server-side bidirectional keyset (`Before` and `After`)** — rejected for
  now: the client-side cursor stack covers the only pagination UI this task
  needs (linear forward/backward through a single query's results) with less
  server-side surface. If a future task needs true random-access paging (jump
  to page N), revisit.
- **`EventStore.QueryFilter` = `Shared.EventFilter` directly** — rejected:
  `EventStore.fs` has no reference to `Mediatheca.Shared` today (it's pure
  server-side infrastructure operating on primitive types), and introducing
  that dependency to save one small mapping function in `Administration.fs`
  wasn't worth coupling the event store to the wire-contract module.

## References

- `src/Server/EventStore.fs` — `createFtsIndex`, `QueryFilter`, `queryEventPage`.
- `src/Server/Administration.fs` — `boundedContextPrefixes`, `getEventPage`.
- `src/Shared/Shared.fs` — `EventFilter`, `EventPageQuery`, `EventPage`, `IAdminApi`.
- `src/Client/Pages/EventBrowser/` — `Types.fs`, `State.fs` (cursor stack), `Views.fs`.
- `tests/Server.Tests/EventStoreTests.fs` — FTS backfill idempotency test,
  keyset pagination test, filter composition tests.
