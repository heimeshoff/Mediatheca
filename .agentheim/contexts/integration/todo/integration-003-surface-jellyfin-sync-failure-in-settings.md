---
id: integration-003
title: Surface the persisted Jellyfin sync failure in the Settings UI
status: todo
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
But the Settings UI throws that failure away — so the single user still has no
in-app signal that the sync broke. This closes the loop: a silent breakage
becomes a visible one.

## What

The `JellyfinSyncStatus` already flows to the client (`getJellyfinSyncStatus`
in `Shared.fs:1189`; loaded via `Jellyfin_sync_status_loaded` —
`Settings/Types.fs:143`, `Settings/State.fs:467`). The DU
(`Shared.fs:940-944`) is:

- `SyncIdle of lastSyncTime: string option`
- `SyncInProgress`
- `SyncCompleted of result: JellyfinImportResult * lastSyncTime: string`
- `SyncFailed of error: string * lastSyncTime: string option`

**The gap:** the `Jellyfin_sync_status_loaded` handler
(`Settings/State.fs:469-476`) extracts only `lastTime` from the status and
stores it in `JellyfinLastSyncTime` — the error message in `SyncFailed` is
discarded. So this is *not* a pure render task. Two changes:

1. **Retain the status.** Add a model field (e.g.
   `JellyfinSyncStatus: JellyfinSyncStatus option`) to the Settings `Model`
   (`Settings/Types.fs:63-64`, next to `JellyfinLastSyncTime`), and set it in
   the `Jellyfin_sync_status_loaded` handler (`Settings/State.fs:469`) instead
   of dropping all but the timestamp. Keep `JellyfinLastSyncTime` populated as
   it is today (other UI reads it).
2. **Render it** in `jellyfinDetail` (`Settings/Views.fs:994`), near the
   existing last-sync label (`Views.fs:1000`):
   - `SyncFailed (error, lastTime)` → a clear failure/warning panel showing the
     persisted `error` message (it already includes the per-item counts) and
     the last-run time. This is the new behaviour.
   - `SyncCompleted` / `SyncInProgress` / `SyncIdle` → no visual regression;
     the existing success/last-synced display stays as-is.

## Acceptance criteria

- [ ] After a failed sync, the Settings → Jellyfin section shows a clear
      failure state with the persisted `error` message and the last-run time
      (last-run may be absent → render gracefully, since `lastTime` is
      `string option` for `SyncFailed`).
- [ ] The full `JellyfinSyncStatus` is retained in the Settings model (not
      reduced to just the timestamp) and refreshed by the existing
      `Load_jellyfin_sync_status` flow.
- [ ] When the last sync succeeded or is idle/in-progress, the existing display
      is visually unchanged (no regression).
- [ ] The failure panel follows the glassmorphism overlay rules per the
      styleguide (`.agentheim/contexts/design-system/styleguide.md`) — reuse the
      existing `feedbackAlert`/detail-card patterns in this file rather than a
      bespoke opaque box.
- [ ] No regression to the existing "Sync now" / scan / import trigger flows in
      `jellyfinDetail`.
- [ ] `npm run build` clean.

## Notes

- Frontend task: `depends_on: [design-system-001]` — gate is OPEN (styleguide
  signed off 2026-05-27).
- Read/render + one model-field addition; **no new API method** — the server
  side (`getJellyfinSyncStatus`, persisted result) shipped in `integration-001`.
- Relevant code: `Settings/Views.fs` `jellyfinDetail` (~994-1240),
  `Settings/State.fs` `Jellyfin_sync_status_loaded` (469-476) +
  `Load_jellyfin_sync_status` (466), `Settings/Types.fs` `Model` (63-64) and
  `Msg` (143), `Shared.fs` `JellyfinSyncStatus` (940-944).
- The error string is the human-readable summary built by `runJellyfinImport`
  (counts + per-item error list) — render it as-is; don't re-parse it.
- This is a single-side concern; the live failure is now reproducible since the
  server persists `jellyfin_last_sync_result` across restarts.
</content>
</invoke>
