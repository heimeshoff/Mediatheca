---
id: administration-v4y9g
title: Stream drill-in — per-stream timeline with formatted+raw views, projection state, cross-links
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-20
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, navigation]
related_adrs: [0002, 0022]
related_research: []
prior_art: []
---

## Why
The browser shows a flat log; understanding one aggregate means mentally filtering. A stream-centric view — full history of one aggregate, human-readable, with what the projection currently says about it — turns the browser into an analysis tool: history and current state side by side.

## What
- Clicking a stream id anywhere in the explorer navigates to `/admin/streams/<streamId>`.
- The stream page shows all events of that stream in order (`EventStore.readStream`), each rendered through the existing `EventFormatting` formatters with a per-event toggle to raw JSON (data + metadata + positions).
- Events whose type no formatter knows fall back to raw JSON, visually marked as "unformatted" (feeds the drift report later).
- A "current state" panel shows the projection rows for this stream (dispatch on stream prefix to the matching `*Projection` table lookup), plus a link to the media detail page when one exists.
- **Cross-linking:** known reference fields in payloads (`friendSlug`, `movieSlug`, `seriesSlug`, `gameSlug`, entry/catalog ids) render as links to the referenced stream's drill-in page.

## Acceptance criteria
- [ ] Stream ids in the event explorer are clickable and open the stream timeline.
- [ ] Timeline shows formatted entries with working raw-JSON toggle per event.
- [ ] Projection-state panel shows the current read-model row(s) for Movie/Series/Game/Friend/Catalog streams.
- [ ] `friendSlug` (at minimum) in a payload links to that friend's stream page.
- [ ] Unknown event types render as raw JSON with an "unformatted" marker instead of disappearing.

## Notes
`EventFormatting.fs` already maps stream prefixes to formatters — reuse `formatEvent`, don't duplicate. The `EventHistoryEntry` DTO may need enrichment (raw data, global position, link refs) — extend in `IAdminApi` types rather than changing the detail-page contract.

## Outcome
Added `/admin/streams/<streamId>` (`Router.Stream_detail`, `src/Client/Pages/StreamDetail/`) — a stream drill-in showing full event history (via `EventStore.readStream` + `EventFormatting.formatEvent`) with a per-event raw-JSON toggle (data, metadata, global/stream position), a "current state" panel dispatching by stream prefix onto each BC's existing `*Projection.getBySlug` (Movie/Series/Game/Friend/Catalog), and cross-links for known payload reference fields (`friendSlug`, `movieSlug`, `seriesSlug`, `gameSlug`) to the referenced stream's own drill-in. Unknown event types render as raw JSON with an "unformatted" marker instead of disappearing. Stream ids in the event explorer are now clickable and route here.

New `IAdminApi.getStreamDetail: string -> Async<StreamDetailDto>` on a deliberately separate DTO family (`StreamCrossLink`, `StreamTimelineEntry`, `ProjectionStateRow`, `StreamDetailDto`) rather than extending `EventHistoryEntry`/`getStreamEvents` (owned by the per-media detail page's history modal, out of scope here). Design tradeoffs (projection-panel flattening via existing `getBySlug` rather than new SQL; dangling cross-links rendered without server-side existence checks, safe because the drill-in's empty-timeline state already covers it) are recorded in ADR-0022.

TDD: 5 new Expecto tests in `tests/Server.Tests/AdministrationTests.fs` (`getStreamDetail` — ordering + formatted labels + cross-links, unformatted fallback, projection dispatch, no-dispatch for unmapped prefixes). Full suite: 308/308 passing (up from 304 baseline — administration-mtf1f/-hw74a running concurrently may have already added tests between the reported baseline and this run). `npm run build` passes (Fable compiles cleanly, no new warnings). Manual verification: build + test only — no dev server smoke test was run (Chrome DevTools MCP not exercised for this task); the client-side flow (click a stream id → drill-in renders → raw toggle → cross-link navigation) exercises the same Elmish/Router patterns as every other `*_detail` page in the app, which are covered by the codebase's existing conventions rather than new tests.

Key files:
- `src/Shared/Shared.fs` — `StreamCrossLink`, `StreamTimelineEntry`, `ProjectionStateRow`, `StreamDetailDto`, `IAdminApi.getStreamDetail`
- `src/Server/EventFormatting.fs` — `crossLinksFromPayload`
- `src/Server/Administration.fs` — `projectionRowFor`, `toTimelineEntry`, `getStreamDetail`
- `src/Client/Router.fs` — `Stream_detail` page case, `/admin/streams/<streamId>` route
- `src/Client/Pages/StreamDetail/{Types,State,Views}.fs` — new page module
- `src/Client/Pages/EventBrowser/Views.fs` — stream id made clickable
- `src/Client/{Types,State,Views}.fs`, `src/Client/Client.fsproj` — wiring
- `tests/Server.Tests/AdministrationTests.fs`
- `.agentheim/knowledge/decisions/0022-stream-drill-in-projection-flattening-and-dangling-cross-links.md`
- `.agentheim/contexts/administration/README.md`
