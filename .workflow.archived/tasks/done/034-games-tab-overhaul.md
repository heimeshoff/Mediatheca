# Task: Games Tab Overhaul

**ID:** 034
**Milestone:** M5 (Dashboard V3)
**Size:** Large
**Created:** 2026-02-25
**Dependencies:** 033

## Objective

Restructure the Games tab with a new In-Focus Estimate hero card, convert lists to poster scrollers, replace charts with pie and spider/radar visualizations, and add per-game color-coded monthly play time.

## Details

1. **Backlog Estimate → In-Focus Estimate:**
   - Rename and rework the hero card.
   - Show estimated remaining time for **In-Focus games only** to reach main storyline (HLTB "Main Story" time).
   - Calculation: For each In-Focus game, `max(0, hltb_main - played_minutes)`. Clamp negatives to 0 (if played longer than HLTB suggests, don't count negative).
   - Sum all clamped remainders.
   - Display as "Xh remaining" or similar.

2. **Layout restructure (top to bottom):**

   **Row 1:** In-Focus Estimate hero card (full width)

   **Row 2:** Recently Played | Recently Added
   - Both as horizontal poster scrollers (movie-poster style cards).
   - Recently Played **includes finished games**.
   - **Exclude dismissed games** from both lists.

   **Row 3:** Status Distribution (pie chart) | Genre Breakdown (spider/radar graph)
   - `grid grid-cols-1 md:grid-cols-2 gap-4`
   - Status Distribution: convert from stacked bar to **pie chart** (reuse shared Charts.fs component).
   - Genre Breakdown: convert from horizontal bars to **spider/radar chart** (new component in Charts.fs).

   **Row 4:** Monthly Play Time (2/3 left) | Recent Achievements (1/3 right)
   - `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`
   - Monthly Play Time: stacked/grouped bar chart showing **individual games color-coded** (not just a single "games" total).
   - Recent Achievements: existing Steam achievements section, moved here.

3. **Monthly Play Time — per-game color coding:**
   - Each game gets its own color in the bar chart.
   - Show which games were played each month as stacked segments.
   - Legend maps colors to game names.
   - May need backend changes to return per-game monthly data instead of aggregated totals.

## Acceptance Criteria

- [x] In-Focus Estimate shows clamped remaining HLTB time for In-Focus games
- [x] Recently Played includes finished games, excludes dismissed
- [x] Recently Added excludes dismissed games
- [x] Both are horizontal poster scrollers
- [x] Status Distribution is a pie chart (using shared Charts.fs component)
- [x] Genre Breakdown is a spider/radar chart (new Charts.fs component)
- [x] Monthly Play Time shows per-game color-coded bars
- [x] Recent Achievements is in the right column
- [x] Monthly Play Time is in the left 2/3
- [x] All existing tests pass

### 2026-02-25 -- Work Completed

**What was done:**
- Added `InFocusEstimate` and `GameMonthlyPlayTime` types to Shared.fs
- Added `InFocusEstimate` and `MonthlyPlayTimePerGame` fields to `DashboardGamesTab`
- Added `getInFocusEstimate` query in GameProjection.fs: calculates clamped `max(0, hltb_main_minutes - played_minutes)` for InFocus games
- Added `getMonthlyPlayTimePerGame` query in GameProjection.fs: returns per-game monthly play time data grouped by month and game
- Updated `getGamesRecentlyPlayed` to exclude dismissed games (WHERE status != 'Dismissed')
- Updated `getRecentlyAddedGames` to exclude dismissed games (WHERE status != 'Dismissed')
- Updated Api.fs to include new InFocusEstimate and MonthlyPlayTimePerGame data in getDashboardGamesTab
- Added spider/radar chart component (`radarChart`) to Charts.fs with grid rings, axis lines, data polygon, and axis labels
- Exposed `chartColors` and `chartBgColors` from Charts.fs for external consumers
- Replaced `backlogTimeEstimateCard` with `inFocusEstimateCard` showing remaining HLTB time for InFocus games
- Created `gameRecentlyPlayedPosterCard` and `gameRecentlyAddedPosterCard` for poster-style horizontal scrollers
- Created `perGameMonthlyPlayTimeChart` with stacked color-coded bars per game and a legend
- Restructured gamesTabView layout: Row 1 (In-Focus Estimate), Row 2 (Recently Played | Recently Added poster scrollers), Row 3 (Status Distribution pie | Genre Breakdown radar), Row 4 (Monthly Play Time 2/3 | Achievements 1/3)

**Acceptance criteria status:**
- [x] In-Focus Estimate shows clamped remaining HLTB time for In-Focus games -- new `getInFocusEstimate` query with `max(0, hltb_minutes - played_minutes)` clamping, displayed in `inFocusEstimateCard`
- [x] Recently Played includes finished games, excludes dismissed -- SQL query has no status filter on play sessions (includes all) with `WHERE gl.status != 'Dismissed'`
- [x] Recently Added excludes dismissed games -- SQL query has `WHERE status != 'Dismissed'`
- [x] Both are horizontal poster scrollers -- using `flex gap-3 overflow-x-auto snap-x` with poster card components
- [x] Status Distribution is a pie chart (using shared Charts.fs component) -- calls `Charts.donutChart data.Stats.StatusDistribution`
- [x] Genre Breakdown is a spider/radar chart (new Charts.fs component) -- calls `Charts.radarChart data.Stats.GenreDistribution`
- [x] Monthly Play Time shows per-game color-coded bars -- `perGameMonthlyPlayTimeChart` with stacked segments colored per game and legend
- [x] Recent Achievements is in the right column -- in `grid-cols-[2fr_1fr]` right column
- [x] Monthly Play Time is in the left 2/3 -- in `grid-cols-[2fr_1fr]` left column
- [x] All existing tests pass -- 233 tests passed, `npm run build` and `npm test` both succeed

**Files changed:**
- src/Shared/Shared.fs -- Added InFocusEstimate type, GameMonthlyPlayTime type, extended DashboardGamesTab with InFocusEstimate and MonthlyPlayTimePerGame fields
- src/Server/GameProjection.fs -- Added getInFocusEstimate and getMonthlyPlayTimePerGame queries; updated getGamesRecentlyPlayed and getRecentlyAddedGames to exclude dismissed games
- src/Server/Api.fs -- Updated getDashboardGamesTab to include InFocusEstimate and MonthlyPlayTimePerGame data
- src/Client/Components/Charts.fs -- Added radarChart component; exposed chartColors and chartBgColors arrays
- src/Client/Pages/Dashboard/Views.fs -- Replaced backlogTimeEstimateCard with inFocusEstimateCard; added poster card variants for games; added perGameMonthlyPlayTimeChart; restructured gamesTabView layout per spec
