---
id: administration-btvqa
title: Integrity checks — shadow-table replay drift detector and unknown-event report
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-qjcp4, design-system-001]
blocks: []
tags: [admin-console, projections, integrity, drift]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why
Trust in an event-sourced system rests on "the projection is exactly what the log says". Nothing verifies that today — a bug in a handler or a missed catch-up would go unnoticed until the UI looks wrong. Same for schema drift: event types the code no longer handles (legacy cases like `"Playing"` folded into `InFocus`) accumulate silently.

## What
- **Drift detector:** replay all events through a projection's handler into shadow tables (handler `Init` parameterized with a table-name prefix, or a separate in-memory/attached database), diff shadow vs. live tables, report row-level discrepancies per projection. Read-only with respect to live data.
- **Unknown-event report:** distinct event types in the store that (a) no projection handler processes and/or (b) `EventFormatting.formatEvent` cannot format — surfaced as a list with counts and sample events.
- Both live on the Projections (or Health) tab with a "Run check" action; results are displayed, not persisted (or persisted with a timestamp — refine).

## Acceptance criteria
- [ ] Drift check on a healthy store reports zero discrepancies; a deliberately corrupted projection row (test setup) is detected and reported.
- [ ] Unknown-event report lists event types with counts; a fabricated unknown type in a test store shows up.
- [ ] Live projection tables are untouched by a drift run.

## Notes
Needs refinement before work: the shadow-table mechanism requires `ProjectionHandler` to parameterize its table names (today they're hard-coded in each `*Projection.fs` `Init`), or an `ATTACH`ed scratch database with identical table names — decide the approach (likely an ADR). Determining "which event types a handler processes" may need handlers to declare their handled types explicitly — worth doing anyway for the drift report.
