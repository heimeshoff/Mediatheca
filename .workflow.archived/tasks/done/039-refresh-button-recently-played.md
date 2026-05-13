# Task 039: Refresh Button on Recently Played Card

**Status:** Done
**Size:** Small
**Created:** 2026-04-09
**Milestone:** --

## Description

Add a refresh button to the top-right corner of the "Recently Played" card on the Games tab of the dashboard. The button triggers a Steam playtime sync (`triggerPlaytimeSync`) and then re-fetches both the Games tab and All tab data so the UI reflects the latest play sessions immediately.

### Problem

After playing a game, the user returns to the dashboard and wants to see updated play session data without waiting for the daily 4 AM UTC auto-sync. This is especially important when a newly played game wasn't in the library yet — the sync auto-creates it and records the session.

## Acceptance Criteria

- [ ] Refresh icon button visible in the top-right corner of the "Recently Played" card header
- [ ] Clicking it calls `triggerPlaytimeSync`, then re-fetches Games tab data AND All tab data
- [ ] Spinning icon animation while sync + data fetch is in progress
- [ ] Button disabled during sync to prevent double-clicks
- [ ] No new backend work — uses existing `triggerPlaytimeSync` and `getDashboardGamesTab`/`getDashboardAllTab` API methods

## Implementation Notes

### Client changes only

**Types.fs** — Add messages:
- `TriggerPlaytimeSync` — user clicked refresh
- `PlaytimeSyncCompleted of Result<PlaytimeSyncResult, string>` — sync finished, trigger tab data refetch
- Possibly a `IsSyncing: bool` field on the model

**State.fs** — Handle new messages:
- `TriggerPlaytimeSync`: set `IsSyncing = true`, call `api.triggerPlaytimeSync()`
- `PlaytimeSyncCompleted Ok`: call `fetchTabData` for both Games and All tabs, set `IsSyncing = false`
- `PlaytimeSyncCompleted Error`: set `IsSyncing = false`, optionally show error

**Views.fs** — Modify `sectionCardOverflow` or the specific Recently Played card:
- Add a refresh button (small icon) to the card header, positioned top-right
- Pass `dispatch` and `isSyncing` state to control the button
- Use a rotating animation class on the icon while syncing

### Existing infrastructure
- `triggerPlaytimeSync: unit -> Async<Result<PlaytimeSyncResult, string>>` in `IMediathecaApi`
- `sectionCardOverflow` renders the card wrapper (Views.fs ~line 106)
- Games tab recently played card at Views.fs ~line 4031

## Dependencies

None — all backend APIs already exist.

## Work Log

### 2026-04-09 — Implementation complete
- Added `arrowPath` and `arrowPathSm` refresh icons to `Icons.fs`
- Added `IsSyncing: bool` to Dashboard Model and `TriggerPlaytimeSync` / `PlaytimeSyncCompleted` messages to `Types.fs`
- Added message handlers in `State.fs`: triggers sync API call, then refreshes both Games and All tab data on completion
- Created `sectionCardOverflowWithAction` helper in `Views.fs` for card headers with action buttons
- Replaced the Recently Played card in `gamesTabView` with the new helper, adding a circular refresh button with spin animation while syncing
- Passed `isSyncing` and `dispatch` through to `gamesTabView`
- Build verified successfully with `npm run build`
