---
id: administration-hw74a
title: Store health tab — event volume stats, largest streams, storage sizes
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-20
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, stats]
related_adrs: [0003, 0021]
related_research: []
prior_art: []
---

## Why
There is no view of the store as a whole — how fast it grows, which aggregates dominate, how big the database and image cache are. A health panel answers "is everything normal?" at a glance and gives the surgery/backup work a factual baseline.

## What
A **Health tab** (`/admin/health`) showing:
- Total event count and events per bounded context (stream-prefix breakdown).
- Events over time: per-day counts for the last ~90 days rendered as a sparkline/mini-bars (mono font for counts per design system).
- Top N largest streams by event count.
- Distinct event-type count and the top types by frequency.
- Storage: `mediatheca.db` file size, WAL sidecar size, `images/` directory size and file count.

## Acceptance criteria
- [ ] Health tab renders all sections from live data via `IAdminApi`.
- [ ] Per-BC and per-day numbers are consistent with direct SQL over the events table (spot-check in a test).
- [ ] Storage sizes reflect the actual data dir (DATA_DIR-aware, per Program.fs).
- [ ] Page loads in one round trip (single aggregate DTO, no N+1 calls).

## Notes
All queries are simple GROUP BYs over the indexed `events` table — no schema change needed. Timestamps are stored as ISO-8601 TEXT, so `substr(timestamp,1,10)` groups by day.

See ADR-0021 for the query-cost reasoning (index-only scans for stream/type breakdowns, bounded-window scan for the daily sparkline).

## Outcome
Added a single aggregate `IAdminApi.getHealthStats: unit -> Async<HealthStats>` and wired it into the Health tab.

- **Server:** `EventStore.fs` gained three index-backed aggregate queries (`getEventCountsByStream`, `getEventCountsByType`, `getDailyEventCounts`) — see ADR-0021 for why each is cheap (index-only GROUP BYs, and a `WHERE timestamp >= @since` bound for the 90-day window). `Administration.fs` combines these with filesystem facts (`fileSizeOrZero`, `directoryStats`) into `buildHealthStats`, producing: total event count; per-bounded-context counts (with an "Other" bucket for unmatched stream prefixes, so the breakdown always sums to the total); a zero-filled 90-day `DailyCounts` series; top-10 largest streams; distinct event-type count and top-10 event types by frequency; and storage sizes (`mediatheca.db`, `-wal` sidecar, `images/` cache size + file count). `Administration.create` now takes `dbPath`/`imagesDir` params (previously just `conn`) so storage stats reflect the real `DATA_DIR`-derived paths — `Program.fs`'s one call site (`Administration.create conn dbPath imageBasePath`) was updated to match; both were already in scope as local `let`s there, so this was a one-line, non-restructuring change.
- **Shared:** `Shared.fs` gained `BoundedContextEventCount`, `DailyEventCount`, `StreamEventCount`, `EventTypeCount`, `StorageStats`, `HealthStats`, and `IAdminApi.getHealthStats`, added additively alongside the existing event-explorer methods.
- **Client:** new `src/Client/Pages/AdminHealth/{Types,State,Views}.fs` — stat cards (total events, event types, DB size, image cache size), a CSS-bar 90-day sparkline (native `title` tooltips per bar, no charting dependency), bar-row lists for the bounded-context breakdown and top event types, a largest-streams table, and a storage breakdown card. Wired into `Pages/Admin/{Types,State,Views}.fs` by adding a `HealthModel`/`Health_msg` alongside the existing `EventBrowserModel`/`Event_browser_msg`, replacing the Health tab's placeholder-panel match arm — the Events tab and other placeholder tabs (Projections/Jobs/Surgery) are untouched. `Client.fsproj` compile order: `AdminHealth/{Types,State,Views}.fs` before `Admin/{Types,State,Views}.fs`.

Tests: 6 new Expecto tests in `AdministrationTests.fs` (total count vs. direct SQL; per-BC counts including the "Other" bucket, summing to the total; top-streams ordering; distinct/top event types; 90-day window coverage and today's-bucket spot check; storage stats against real temp files/dirs). 310/310 passing (up from 304 baseline). `npm run build` passes (Fable compiles cleanly, new page transforms with no errors).

Key files:
- `src/Server/EventStore.fs` — `getEventCountsByStream`, `getEventCountsByType`, `getDailyEventCounts`
- `src/Server/Administration.fs` — `buildHealthStats`, `fileSizeOrZero`, `directoryStats`, `create` (new `dbPath`/`imagesDir` params)
- `src/Server/Program.fs` — one-line call-site update (`Administration.create conn dbPath imageBasePath`)
- `src/Shared/Shared.fs` — `HealthStats` and friends, `IAdminApi.getHealthStats`
- `src/Client/Pages/AdminHealth/{Types,State,Views}.fs`
- `src/Client/Pages/Admin/{Types,State,Views}.fs`
- `src/Client/Client.fsproj`
- `tests/Server.Tests/AdministrationTests.fs`
- `.agentheim/knowledge/decisions/0021-health-tab-index-only-aggregate-queries.md`
- `.agentheim/contexts/administration/README.md`
