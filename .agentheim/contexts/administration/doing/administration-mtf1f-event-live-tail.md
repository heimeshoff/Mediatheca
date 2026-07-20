---
id: administration-mtf1f
title: Event explorer live tail — follow mode for incoming events
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-g5dfy, design-system-001]
blocks: []
tags: [admin-console, event-store, live]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why
Watching a Steam sync, Jellyfin import, or nightly refresh write events in real time is the fastest way to see what an integration actually does — and to catch it misbehaving. Today the only option is refreshing the browser page.

## What
- A "Follow" toggle on the Events tab. While on, the client polls `IAdminApi` for events with `global_position` greater than the last seen position (reuse `EventStore.readAllForward`), every ~2s, and prepends new rows with a subtle highlight animation.
- Active filters (stream, type, BC, search) apply to tailed events too.
- Toggling off stops polling; navigating away stops polling (Elmish subscription or interval cmd with proper disposal).

## Acceptance criteria
- [ ] With Follow on, an event appended by another action (e.g. rating a movie in a second tab) appears in the list within a few seconds without page reload.
- [ ] New rows respect the active filters.
- [ ] Polling stops when Follow is off or the page is left (no orphan intervals — verify no requests in devtools network log after leaving).

## Notes
Polling is fine for a single-user app — don't build SSE/WebSocket infrastructure for this; the SSE pattern stays reserved for rebuild progress (administration-qjcp4). Keep the poll interval a client-side constant.
