---
id: 0021
title: Health tab uses index-only GROUP BYs and a bounded time window, not caching
scope: administration
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [administration-hw74a]
related_research: []
---

# ADR 0021: Health tab uses index-only GROUP BYs and a bounded time window, not caching

## Context

The Health tab (administration-hw74a) answers "is everything normal?" for the
event store as a whole: total events, per-bounded-context breakdown, a
90-day activity sparkline, the largest streams, event-type frequency, and
storage sizes. Every one of these except storage is an aggregate over the
append-only `events` table, which only grows — a health panel that itself
gets slower as the store grows would undermine its own purpose. This ADR
records the cost reasoning behind each query and why no caching layer was
introduced for a first cut.

## Decision

Three query shapes, each traded off against a scan of the full row data:

1. **Per-bounded-context and largest-streams:**
   `EventStore.getEventCountsByStream` — `SELECT stream_id, COUNT(*) FROM
   events GROUP BY stream_id`. `idx_events_stream_id` is a B-tree already
   ordered by `stream_id`, so SQLite answers this as a streaming
   index-only scan (no row/page access beyond the index itself, no temp
   sort) — cost scales with total event count but touches only index
   pages, not full rows. One scan serves both the bounded-context breakdown
   (grouping streams by prefix in F#) and the top-10-largest-streams list
   (sort + truncate in F#), so the query runs once, not twice.

2. **Event-type frequency:** `EventStore.getEventCountsByType` — the same
   shape over `idx_events_event_type`, serving both the distinct-type count
   and the top-10-by-frequency list from one scan.

3. **Daily activity (sparkline):** `EventStore.getDailyEventCounts` —
   `SELECT substr(timestamp,1,10), COUNT(*) FROM events WHERE timestamp >=
   @since GROUP BY day`, with `@since` = today minus 89 days. The `WHERE`
   clause is answered by `idx_events_timestamp` as a range scan bounded to
   the window, then only the matched ~90-day slice is grouped — cost tracks
   window size, not total store history, so this query's cost is constant
   as the store grows, unlike (1) and (2).

Storage stats (`mediatheca.db` size, `-wal` sidecar size, `images/` size and
file count) are filesystem facts (`FileInfo.Length`, a recursive
`Directory.GetFiles` walk), not SQL at all — `Administration.create` takes
`dbPath`/`imagesDir` (the same paths `Program.fs` already derives from
`DATA_DIR`) so these reflect the real data dir.

All of the above is exposed as one `IAdminApi.getHealthStats: unit ->
Async<HealthStats>` returning a single aggregate DTO, so the tab loads in
one round trip (no N+1).

## Consequences

### Positive
- (1) and (2) are index-only scans — no row-data I/O — and each answers two
  of the tab's questions from a single query.
- (3) is bounded by the window, not total history: the sparkline stays cheap
  forever regardless of how large the event log grows.
- One round trip for the whole tab.

### Negative / accepted tradeoff
- (1) and (2) are still O(total events), even though index-only: every
  append grows the scan cost linearly. For a single-user personal library
  app, total event count is expected to stay in the thousands-to-tens-of-
  thousands range for years, where an index-only scan is sub-millisecond —
  this is fine today. **If the store grows past roughly 100k-1M events and
  the Health tab becomes perceptibly slow, the fix is a small materialized
  summary table (e.g. `stream_event_counts`, `event_type_counts`) maintained
  incrementally on append, not a rewrite of this query shape** — noted here
  rather than built now, since building a caching layer before there's a
  real growth signal would be speculative.
- The 90-day window is fixed (not user-configurable) — acceptable for a
  first cut; a `days` parameter can be added to `getDailyEventCounts` /
  `EventPageQuery`-style request later without a shape change.

## Alternatives considered

- **Cache/materialize the aggregates on every append** (e.g. an in-memory
  running total, or a summary table updated in the same transaction as
  `appendToStream`) — rejected for now: adds write-path complexity and a
  second source of truth to keep consistent, for a query that's currently
  fast enough to compute on demand. Revisit if event count grows large
  enough that the plain GROUP BY becomes noticeably slow (see above).
- **Full-table scan without index-only optimization** (e.g. computing
  breakdowns in F# after `SELECT * FROM events`) — rejected: would pull
  every row's `data`/`metadata` TEXT columns over the wire for no reason:
  strictly worse than letting SQLite answer via the index.
- **Unbounded daily counts (all history)** — rejected: cost would grow
  without bound as the store ages, defeating the point of a sparkline that's
  supposed to answer "recent activity at a glance."

## References

- `src/Server/EventStore.fs` — `getEventCountsByStream`,
  `getEventCountsByType`, `getDailyEventCounts`.
- `src/Server/Administration.fs` — `buildHealthStats`, `fileSizeOrZero`,
  `directoryStats`, `create` (now takes `dbPath`/`imagesDir`).
- `src/Shared/Shared.fs` — `HealthStats`, `BoundedContextEventCount`,
  `DailyEventCount`, `StreamEventCount`, `EventTypeCount`, `StorageStats`,
  `IAdminApi.getHealthStats`.
- `src/Client/Pages/AdminHealth/` — `Types.fs`, `State.fs`, `Views.fs`.
- `tests/Server.Tests/AdministrationTests.fs` — spot-check tests comparing
  `getHealthStats` output against direct SQL / known fixture data.
