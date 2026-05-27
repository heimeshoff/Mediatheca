---
id: integration-003
title: Surface the persisted Jellyfin sync failure in the Settings UI
status: backlog
type: feature
context: integration
created: 2026-05-27
completed:
commit:
depends_on: [design-system-001]
blocks: []
tags: [jellyfin, sync, settings, frontend]
related_adrs: [0010]
related_research: []
prior_art: [integration-001]
---

## Why

`integration-001` / ADR 0010 made a Jellyfin sync failure observable on the
server: the last result (counts + error list / failure message) is persisted
and reachable as `JellyfinSyncStatus.SyncFailed` via `getJellyfinSyncStatus`.
But the Settings UI does not yet show that failure — a user still has no signal
in the app that the sync broke. This closes the loop so a silent breakage
becomes a visible one for the single user.

## What

In the Jellyfin section of the Settings page, render the
`JellyfinSyncStatus`:
- `SyncFailed (error, lastTime)` -> a visible error/warning state showing the
  message (it includes the counts) and when it last ran.
- `SyncCompleted (result, lastTime)` -> the existing success summary.
- `SyncInProgress` / `SyncIdle` -> existing states.

## Acceptance criteria

- [ ] When the last sync failed, the Settings Jellyfin section shows a clear
      failure state with the persisted message and last-run time.
- [ ] When the last sync succeeded, the existing success summary is unchanged.
- [ ] Overlays/cards follow the glassmorphism rules (per the styleguide gate).
- [ ] No regression to the existing "Sync now" trigger flow.

## Notes

- Frontend task: `depends_on: [design-system-001]` per the BC Frontend gate.
- Data already exists server-side; this is a read/render task, no new API.
