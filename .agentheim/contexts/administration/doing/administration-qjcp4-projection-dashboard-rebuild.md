---
id: administration-qjcp4
title: Projection dashboard — checkpoint/lag overview and rebuild-by-command with streamed progress
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, projections, rebuild, sse]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why
Projections are disposable read models, but today rebuilding one means editing `Program.fs` — which currently hard-rebuilds the Series and Game projections on every server start (Program.fs:160-161) as a workaround. Rebuild should be an explicit operator command with visible progress, and the startup hack should die.

## What
- **Projections tab** (`/admin/projections`) listing every registered `ProjectionHandler`: checkpoint position (`projection_checkpoints`), lag vs. store head (`MAX(global_position)`), checkpoint `updated_at`, and row counts of the projection's tables.
- **Rebuild command:** per-projection "Rebuild" and a "Rebuild all" button. Server runs `Projection.rebuildProjection` on a background task; progress (current position / head, events processed, events/sec) streams to the client via the same streaming-handler pattern as `/api/stream/import-steam-family` (src/Server/Api.fs / Program.fs webApp route).
- Rebuild-in-progress state is visible (per-projection spinner + progress bar); concurrent rebuild requests for the same projection are rejected or queued, not doubled.
- **Retire the startup hack:** remove the unconditional `rebuildProjection` calls for Series/Game from `Program.fs`; startup goes back to plain catch-up (`startAllProjections`).

## Acceptance criteria
- [ ] Projections tab lists all six handlers with checkpoint, lag, updated-at, and row counts.
- [ ] Triggering a rebuild replays the projection from position 0 and shows live progress until completion; the list reflects the new checkpoint afterwards.
- [ ] A second rebuild request for an already-rebuilding projection does not corrupt state (rejected with a visible message, or queued).
- [ ] `Program.fs` no longer force-rebuilds Series/Game at startup; server boots and serves correct data.
- [ ] Expecto test: rebuild of a projection over a seeded in-memory event store produces the same rows as incremental projection.

## Notes
`Projection.rebuildProjection` (src/Server/Projection.fs:64) already does drop + replay — this task is about exposing it safely, not reimplementing it. Mind SQLite single-writer semantics: rebuild runs on the shared connection today; decide whether the background task opens its own connection (WAL allows concurrent readers; writes serialize via busy_timeout).
