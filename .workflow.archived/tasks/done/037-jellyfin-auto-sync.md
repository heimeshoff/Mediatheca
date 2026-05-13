# Task: Jellyfin Auto-Sync on App Visit

**ID:** 037
**Milestone:** --
**Size:** Medium
**Created:** 2026-03-02
**Dependencies:** None (builds on existing Jellyfin integration from task 017)

## Objective

Automatically sync Jellyfin watch history and play links when the user opens Mediatheca, so watched episodes are always up-to-date and new Jellyfin content gets play buttons without manual intervention. The sync runs non-blocking in the background with a 5-minute cooldown to avoid redundant work. A subtle UI indicator shows sync progress, and a toast notification summarizes results when done.

## Details

### Server: Background Sync Infrastructure

#### 1. New module: `JellyfinSync.fs`

Manages sync state and background execution. Single-user app, so mutable state with a simple lock is sufficient.

**Sync state (in-memory):**
- `syncInProgress: bool` — prevents concurrent syncs
- `lastSyncTime: DateTime option` — for cooldown enforcement
- `lastSyncResult: JellyfinImportResult option` — for status reporting

**Functions:**
- `triggerSync(conn, httpClient, getJellyfinConfig, projectionHandlers) -> Async<JellyfinSyncTriggerResult>`
  - Returns immediately with `Started | AlreadyInProgress | CooldownActive of lastSyncTime | NotConfigured`
  - If `Started`: spawns the sync on a background thread via `Async.Start`
  - Cooldown: skip if `lastSyncTime` is less than 5 minutes ago
  - Not configured: skip if Jellyfin credentials are missing/empty
- `getSyncStatus() -> JellyfinSyncStatus`
  - Returns `Idle | Syncing | Completed of JellyfinImportResult | Failed of string`

**Sync logic:**
- Reuses the existing `importJellyfinWatchHistory` logic from `Api.fs` (lines 3407–3648)
  - This already handles: scan + match by TMDB ID, auto-add unmatched items, persist Jellyfin IDs, sync watch sessions and episode progress
- Extract the core import logic into a shared function callable from both the Settings UI endpoint and the background sync
- On completion: update `lastSyncTime`, `lastSyncResult`, clear `syncInProgress`

#### 2. New shared types in `Shared.fs`

```fsharp
type JellyfinSyncTriggerResult =
    | SyncStarted
    | SyncAlreadyInProgress
    | SyncCooldownActive of lastSyncTime: string
    | SyncNotConfigured

type JellyfinSyncStatus =
    | SyncIdle
    | SyncInProgress
    | SyncCompleted of JellyfinImportResult
    | SyncFailed of string
```

#### 3. New API methods on `IMediathecaApi`

```fsharp
triggerJellyfinSync: unit -> Async<JellyfinSyncTriggerResult>
getJellyfinSyncStatus: unit -> Async<JellyfinSyncStatus>
```

#### 4. Registration in `Program.fs`

- Initialize `JellyfinSync` module (no DB tables needed — state is in-memory)
- Wire the new API methods into the API record

### Client: Trigger on Init + Status Polling

#### 1. Root `Types.fs` — new messages

```fsharp
| TriggerJellyfinSync
| JellyfinSyncTriggered of JellyfinSyncTriggerResult
| JellyfinSyncStatusReceived of JellyfinSyncStatus
| DismissJellyfinSyncToast
```

#### 2. Root `State.fs` — init and update

**On init:**
- Add `Cmd.OfAsync.perform api.triggerJellyfinSync () JellyfinSyncTriggered` to the batched init commands

**On `JellyfinSyncTriggered`:**
- If `SyncStarted`: set `model.JellyfinSyncing = true`, start polling `getJellyfinSyncStatus` every 2–3 seconds
- If `CooldownActive` / `AlreadyInProgress` / `NotConfigured`: do nothing (no indicator)

**On `JellyfinSyncStatusReceived`:**
- If `SyncInProgress`: continue polling
- If `SyncCompleted result`: set `JellyfinSyncing = false`, store result for toast, refresh dashboard data (re-call `getDashboardAllTab`), stop polling
- If `SyncFailed error`: set `JellyfinSyncing = false`, optionally show error toast

**Polling:** Use `Cmd.OfAsync.perform` with a small delay (`Async.Sleep 3000` then `api.getJellyfinSyncStatus`)

#### 3. Root model additions

```fsharp
JellyfinSyncing: bool
JellyfinSyncResult: JellyfinImportResult option
ShowJellyfinSyncToast: bool
```

#### 4. UI — Subtle sync indicator + toast

**Sync indicator (while syncing):**
- Small spinner icon in the top-right area of the layout (near existing nav/header)
- Could be a rotating Jellyfin-colored dot or a simple animated sync icon
- Only visible when `model.JellyfinSyncing = true`

**Toast notification (on completion):**
- Appears briefly (auto-dismiss after 5 seconds, or manual dismiss)
- Shows summary: "Jellyfin synced: 3 episodes, 1 movie added"
- Only shown if something actually changed (skip if all counts are 0)
- Render in root `Views.fs` as a fixed-position element

### Settings: "Last Synced" Display for All Integrations

Show the last synchronization timestamp in each integration's Settings panel. Consistent format across all three: relative time (e.g., "2 minutes ago") with the full datetime on hover (tooltip).

#### 1. Steam (Playtime Sync)

**Backend already exists** — `PlaytimeSyncStatus.LastSyncTime` is returned by `api.getPlaytimeSyncStatus()` but never displayed in the UI.

**Client changes (`Settings/Types.fs`, `State.fs`, `Views.fs`):**
- Add `PlaytimeSyncStatus: PlaytimeSyncStatus option` to Settings model
- Add `Load_playtime_sync_status` / `Playtime_sync_status_loaded` messages
- Load on Settings page init (add to `Cmd.batch` in Settings init)
- Display in `steamDetail` view, near the top of the section (below the description or next to the badge)
- Format: "Last synced: 2 hours ago" or "Never synced" if `LastSyncTime = None`

#### 2. Jellyfin

**Backend:** The new `JellyfinSync.fs` module tracks `lastSyncTime` in memory. Persist it to `SettingsStore` as `jellyfin_last_sync` so it survives server restarts. Expose via `getJellyfinSyncStatus` (already planned above — `SyncCompleted` carries the result, but also return `lastSyncTime` in the status).

**Extend `JellyfinSyncStatus`:**
```fsharp
type JellyfinSyncStatus =
    | SyncIdle of lastSyncTime: string option
    | SyncInProgress
    | SyncCompleted of result: JellyfinImportResult * lastSyncTime: string
    | SyncFailed of error: string * lastSyncTime: string option
```

**Client changes:**
- Add `JellyfinLastSyncTime: string option` to Settings model
- Load from `getJellyfinSyncStatus` on Settings page init
- Display in `jellyfinDetail` view, same style as Steam

#### 3. Steam Family

**Backend:** Currently no last-sync tracking. Add `SettingsStore.setSetting conn "steam_family_last_sync" (DateTime.UtcNow.ToString("o"))` at the end of the `importSteamFamilyLibrary` handler in `Api.fs`.

**New API method:**
```fsharp
getSteamFamilyLastSync: unit -> Async<string option>
```
Implementation: `SettingsStore.getSetting conn "steam_family_last_sync"`

**Client changes:**
- Add `SteamFamilyLastSync: string option` to Settings model
- Add `Load_steam_family_last_sync` / `Steam_family_last_sync_loaded` messages
- Load on Settings page init
- Display in `steamFamilyDetail` view, same style as Steam/Jellyfin

#### Shared UI Helper

Create a small helper function in `Views.fs` (or a shared component) to render the "last synced" line consistently:

```fsharp
let lastSyncLabel (lastSync: string option) =
    match lastSync with
    | None -> Html.span [ prop.className "text-sm text-base-content/50"; prop.text "Never synced" ]
    | Some isoTime ->
        // Parse ISO time and show relative format + tooltip with full datetime
        Html.span [
            prop.className "text-sm text-base-content/50"
            prop.title isoTime  // full datetime on hover
            prop.text (sprintf "Last synced: %s" (formatRelativeTime isoTime))
        ]
```

Relative time formatting: use a simple F# function that computes "just now", "X minutes ago", "X hours ago", "yesterday", "X days ago" from the ISO timestamp.

## Acceptance Criteria

- [ ] Opening Mediatheca triggers a non-blocking Jellyfin sync automatically
- [ ] Sync only runs if Jellyfin credentials are configured
- [ ] Sync skips if the last sync completed less than 5 minutes ago
- [ ] Sync skips if another sync is already in progress
- [ ] Watch history (movies + episodes) is synced from Jellyfin
- [ ] New Jellyfin items with TMDB IDs are auto-added to Mediatheca
- [ ] Jellyfin play-link IDs are refreshed for all matched items
- [ ] Subtle spinner visible during sync
- [ ] Toast notification with summary shown on completion (only if changes occurred)
- [ ] Dashboard data refreshes after sync completes (new play buttons, updated watch status)
- [ ] Existing manual "Scan Library" and "Import Watch History" in Settings still work independently
- [ ] No regressions — existing Jellyfin features unaffected
- [ ] Settings > Steam shows "Last synced: ..." using existing `PlaytimeSyncStatus.LastSyncTime`
- [ ] Settings > Jellyfin shows "Last synced: ..." from persisted sync time
- [ ] Settings > Steam Family shows "Last synced: ..." from persisted import time
- [ ] All three show "Never synced" when no sync has occurred
- [ ] Full datetime visible on hover (tooltip) for all three

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Server/JellyfinSync.fs` | **New** — sync state management, background trigger, status reporting |
| `src/Server/Api.fs` | Extract import logic into shared function; wire new API methods; persist `jellyfin_last_sync` and `steam_family_last_sync`; add `getSteamFamilyLastSync` |
| `src/Server/Program.fs` | Initialize JellyfinSync, register API methods |
| `src/Shared/Shared.fs` | New types (`JellyfinSyncTriggerResult`, `JellyfinSyncStatus`), new API methods (`triggerJellyfinSync`, `getJellyfinSyncStatus`, `getSteamFamilyLastSync`) |
| `src/Client/Types.fs` | New root messages + model fields (sync indicator/toast) |
| `src/Client/State.fs` | Init trigger, polling, status handling, dashboard refresh |
| `src/Client/Views.fs` | Sync indicator + toast in root view |
| `src/Client/Pages/Settings/Types.fs` | New model fields (`PlaytimeSyncStatus`, `JellyfinLastSyncTime`, `SteamFamilyLastSync`) + messages |
| `src/Client/Pages/Settings/State.fs` | Load sync status on init for all three integrations |
| `src/Client/Pages/Settings/Views.fs` | "Last synced" display in Steam, Jellyfin, and Steam Family panels; shared `lastSyncLabel` helper |

## Reuse

- **Existing import logic**: `Api.fs` lines 3407–3648 (`importJellyfinWatchHistory`) — extract core into a reusable function
- **PlaytimeTracker pattern**: `PlaytimeTracker.fs` — reference for background sync architecture
- **JellyfinStore**: Already handles ID persistence (`clearAll`, `setMovieJellyfinId`, etc.)
- **SettingsStore**: For checking if Jellyfin is configured
- **Toast pattern**: Check if any existing toast/notification component exists in the client

## Verification

1. `npm run build` — verify Fable + server compilation
2. `npm test` — run existing tests
3. Configure Jellyfin credentials in Settings, manually scan once to establish baseline
4. Restart or revisit the app — verify sync triggers automatically (check server logs for `[JellyfinSync]` messages)
5. Watch an episode in Jellyfin, revisit Mediatheca — verify episode shows as watched after sync
6. Refresh the page within 5 minutes — verify sync is skipped (cooldown)
7. Verify toast appears with summary when sync finds changes
8. Verify no toast when nothing changed
9. Open Settings — verify "Last synced: ..." appears in Steam, Jellyfin, and Steam Family panels
10. Hover over each "Last synced" timestamp — verify full datetime tooltip
11. For an integration that has never synced — verify "Never synced" is shown
12. Chrome DevTools MCP: check for console errors, verify play buttons render, verify Settings page renders correctly

## Work Log

**2026-03-02 — Implementation Complete**

All acceptance criteria implemented across 10 files (1 new, 9 modified):

### Server Changes
- **`src/Server/JellyfinSync.fs` (NEW)**: In-memory sync state management with `syncInProgress`, `lastSyncTime`, `lastSyncResult`. Thread-safe via `lock`. Supports 5-minute cooldown, persists last sync time to SettingsStore. `triggerSync` spawns background async, `getSyncStatus` reports current state. Initialized from persisted DB setting at startup.
- **`src/Server/Api.fs`**: Extracted 240-line `importJellyfinWatchHistory` inline handler into reusable `runJellyfinImport` function. Replaced inline code with delegation to extracted function. Added `triggerJellyfinSync`, `getJellyfinSyncStatus`, `getSteamFamilyLastSync` API implementations. Added `steam_family_last_sync` persistence to `runSteamFamilyImport`.
- **`src/Server/Program.fs`**: Added `JellyfinSync.initialize conn` call at startup.
- **`src/Server/Server.fsproj`**: Added `JellyfinSync.fs` to compilation order.

### Shared Changes
- **`src/Shared/Shared.fs`**: Added `JellyfinSyncTriggerResult` and `JellyfinSyncStatus` DUs. Added `triggerJellyfinSync`, `getJellyfinSyncStatus`, `getSteamFamilyLastSync` to `IMediathecaApi`.

### Client Changes
- **`src/Client/Types.fs`**: Added `JellyfinSyncing`, `JellyfinSyncResult`, `ShowJellyfinSyncToast` to model. Added `TriggerJellyfinSync`, `JellyfinSyncTriggered`, `JellyfinSyncStatusReceived`, `DismissJellyfinSyncToast` messages.
- **`src/Client/State.fs`**: Triggers sync on app init. Polls status every 3s while syncing. On completion: refreshes dashboard, shows toast if changes occurred (auto-dismisses after 5s).
- **`src/Client/Views.fs`**: Glassmorphic sync indicator (top-right spinner) during sync. Glassmorphic toast notification (bottom-right) showing summary of changes.
- **`src/Client/Pages/Settings/Types.fs`**: Added `PlaytimeSyncStatus`, `JellyfinLastSyncTime`, `SteamFamilyLastSync` to model with corresponding messages.
- **`src/Client/Pages/Settings/State.fs`**: Loads sync status for all three integrations on init.
- **`src/Client/Pages/Settings/Views.fs`**: Added `formatRelativeTime` and `lastSyncLabel` helper functions. Shows "Last synced: X ago" (with full ISO tooltip) or "Never synced" in Steam, Jellyfin, and Steam Family panels.

### Verification
- `npm run build` — Fable + server compilation passes
- `npm test` — All 233 tests pass, no regressions
