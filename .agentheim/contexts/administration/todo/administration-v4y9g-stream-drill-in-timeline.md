---
id: administration-v4y9g
title: Stream drill-in — per-stream timeline with formatted+raw views, projection state, cross-links
status: todo
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, event-store, navigation]
related_adrs: [0002]
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
