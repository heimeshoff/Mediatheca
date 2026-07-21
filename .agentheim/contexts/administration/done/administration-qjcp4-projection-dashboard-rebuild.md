---
id: administration-qjcp4
title: Projection dashboard — checkpoint/lag overview and rebuild-by-command with streamed progress
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-21
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, projections, rebuild, sse]
related_adrs: [0002, 0024]
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
- [x] Projections tab lists all six handlers with checkpoint, lag, updated-at, and row counts.
- [x] Triggering a rebuild replays the projection from position 0 and shows live progress until completion; the list reflects the new checkpoint afterwards.
- [x] A second rebuild request for an already-rebuilding projection does not corrupt state (rejected with a visible message, or queued).
- [x] `Composition.fs` (moved out of `Program.fs` by infrastructure-w8fnp before this task landed) no longer force-rebuilds Series/Game at startup; server boots and serves correct data.
- [x] Expecto test: rebuild of a projection over a seeded in-memory event store produces the same rows as incremental projection.

## Notes
`Projection.rebuildProjection` (src/Server/Projection.fs:64) already does drop + replay — this task is about exposing it safely, not reimplementing it. Mind SQLite single-writer semantics: rebuild runs on the shared connection today; decide whether the background task opens its own connection (WAL allows concurrent readers; writes serialize via busy_timeout).

See ADR-0024 for the connection-strategy and concurrency-guard decisions made while implementing this.

## Outcome

Retired the startup-time forced rebuild of Series/Game in `src/Server/Composition.fs` (the successor to `Program.fs` after infrastructure-w8fnp's extraction) — `buildApp` now only calls `Projection.startAllProjections` (plain incremental catch-up). Added a Projections tab (`/admin/projections`, `src/Client/Pages/AdminProjections/`) listing checkpoint position, lag vs. store head, `updated_at`, and per-table row counts for all six registered projection handlers via a new `IAdminApi.getProjectionStats` method. Added a per-projection "Rebuild" command and a "Rebuild all" (client-side sequential orchestration over the same per-projection route) with live streamed progress via a new raw Giraffe SSE route, `Administration.projectionRebuildStreamHandler` at `/api/stream/rebuild-projection/{name}`, built on a new `Projection.rebuildProjectionWithProgress` that reports `RebuildProgress` (Position/Head/EventsProcessed) after every batch. A module-level `ConcurrentDictionary` guard rejects a second concurrent rebuild request for the same projection with a visible SSE message.

Key files:
- `src/Server/Projection.fs` — `getCheckpointInfo`, `RebuildProgress`, `rebuildProjectionWithProgress`.
- `src/Server/EventStore.fs` — `getMaxGlobalPosition`.
- `src/Server/Administration.fs` — `projectionTables`, `rebuildingProjections`, `buildProjectionStats`, `projectionRebuildStreamHandler`; `Administration.create` gained a `projectionHandlers` parameter.
- `src/Server/Composition.fs` — retired the forced Series/Game rebuild; wired the new SSE route and the extra `Administration.create` argument.
- `src/Shared/Shared.fs` — `ProjectionTableCount`, `ProjectionStatRow`, `IAdminApi.getProjectionStats`.
- `src/Client/Pages/AdminProjections/{Types,State,Views}.fs` — new page.
- `src/Client/Pages/Admin/{Types,State,Views}.fs` — wired the Projections tab in place of its placeholder panel.
- `tests/Server.Tests/ProjectionRebuildTests.fs` (new, 3 tests) and `tests/Server.Tests/AdministrationTests.fs` (+3 tests for `getProjectionStats`).
- `.agentheim/knowledge/decisions/0024-projection-rebuild-stream-connection-and-concurrency.md` (new ADR).

Full suite: 331/331 passing. `npm run build` exits 0 (only the pre-existing, out-of-scope `input-bordered` FS0039 warnings in `EventBrowser/Views.fs` remain, as expected).
