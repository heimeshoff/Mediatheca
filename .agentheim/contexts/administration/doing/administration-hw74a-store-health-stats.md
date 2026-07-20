---
id: administration-hw74a
title: Store health tab — event volume stats, largest streams, storage sizes
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, stats]
related_adrs: [0003]
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
