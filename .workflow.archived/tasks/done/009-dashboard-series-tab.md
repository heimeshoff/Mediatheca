# Task: Dashboard client — TV Series tab

**ID:** 009
**Milestone:** M2 - Unified Dashboard
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** 007-dashboard-all-tab

## Objective
TV Series tab on the dashboard shows full next-up list, recently finished/abandoned series, and series stats.

## Details

### Content (src/Client/Pages/Dashboard/Views.fs)
- Fetch data via `getDashboardSeriesTab` API when Series tab is activated

**Next Up section:**
- Full list of series with unwatched episodes (not limited to 6 like All tab)
- Same sorting: In Focus first, then by most recent watch activity
- Each item: poster, series name, next episode (S##E##: title), watch-with friends
- Clicking navigates to series detail

**Recently Finished section:**
- Series where all episodes have been watched
- Show completion badge

**Recently Abandoned section:**
- Series marked as abandoned
- Show abandoned badge (red)

**Stats section:**
- Total series in library
- Total episodes watched
- Total series watch time (episodes × runtime)

### State
- `SeriesTabLoaded of DashboardSeriesTab` message
- Store in model, render when Series tab is active

## Acceptance Criteria
- [ ] Series tab shows full next-up list (not truncated)
- [ ] Recently finished and abandoned sections
- [ ] Stats displayed
- [ ] In Focus sorting preserved
- [ ] Data fetched on tab activation

## Notes
- The Next Up section is the core value — it answers "what should I watch tonight?"
- Recently finished/abandoned gives a sense of completion and progress

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implemented TV Series tab
**File modified:** `src/Client/Pages/Dashboard/Views.fs`

**Changes:**
- Renamed `movieStatBadge` to `statBadge` for reuse across tabs (Movies tab still works, just uses shared helper)
- Added `seriesStatsRow` — displays Total Series, Total Episodes Watched, and Total Watch Time using the shared `statBadge` component
- Added `seriesCompactItem` — reusable compact row for `SeriesListItem` with poster thumbnail, name, year, and a badge parameter (used for Finished/Abandoned)
- Added `seriesTabView` — main series tab layout with:
  - Stats row at top (same pattern as Movies tab)
  - Full Next Up list (reuses existing `seriesNextUpItem` from All tab, not truncated)
  - Recently Finished section with green "Finished" badges (uses `Icons.trophy` header)
  - Recently Abandoned section with red "Abandoned" badges (uses `Icons.tv` header)
- Replaced `placeholderTab "TV Series"` in the main `view` function with `model.SeriesTabData` match — shows `seriesTabView` when data is loaded, `loadingView` while loading
- Data fetching was already wired in `State.fs` via `getDashboardSeriesTab`; no state changes needed

**Build:** `npm run build` passes with no errors.
