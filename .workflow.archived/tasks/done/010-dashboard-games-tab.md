# Task: Dashboard client — Games tab

**ID:** 010
**Milestone:** M2 - Unified Dashboard
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** 007-dashboard-all-tab

## Objective
Games tab on the dashboard shows recently added games, recently played games, and game stats.

## Details

### Content (src/Client/Pages/Dashboard/Views.fs)
- Fetch data via `getDashboardGamesTab` API when Games tab is activated

**Recently Added section:**
- Games added to library, newest first
- ~10 items
- Show as cover grid or list

**Recently Played section:**
- Games with recent play sessions, sorted by last play date
- Each item: cover, game name, total play time, last played date
- If HowLongToBeat data available: show completion percentage (play time / HLTB hours)
- ~10 items

**Stats section:**
- Total games in library
- Total play time (formatted as hours)
- Games completed count
- Games in progress (Playing status) count

### State
- `GamesTabLoaded of DashboardGamesTab` message
- Store in model, render when Games tab is active

## Acceptance Criteria
- [ ] Games tab shows recently added games
- [ ] Games tab shows recently played with play time
- [ ] HowLongToBeat comparison shown when available
- [ ] Stats displayed
- [ ] Data fetched on tab activation

## Notes
- HowLongToBeat comparison only shows if task 012/013 are completed — gracefully handle missing data
- This tab will grow with more game intelligence over time

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete
- Added `gameStatsRow` function: 4 stat badges (Total Games, Play Time, Completed, In Progress) using shared `statBadge` helper and `formatPlayTime` for hours display
- Added `gameRecentlyAddedItem` function: compact rows with cover thumbnail, name, and year; clicks navigate to game detail via `Router.navigate ("games", item.Slug)`
- Added `gamesTabView` function: composes stats row + "Recently Added" section (gamepad icon) + "Recently Played" section (hourglass icon); reuses existing `gameRecentlyPlayedItem` which already handles HLTB comparison ("Xh / Xh" format)
- Replaced `placeholderTab "Games"` in the main view with `model.GamesTabData |> Option.map gamesTabView` pattern (matching Movies/Series tabs)
- Follows same glass card + compact row style as Movies and Series tabs
- Build verified: `npm run build` passes with no errors
- Only file modified: `src/Client/Pages/Dashboard/Views.fs`
