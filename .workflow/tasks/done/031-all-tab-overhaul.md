# Task: All Tab Overhaul

**ID:** 031
**Milestone:** M5 (Dashboard V3)
**Size:** Medium
**Created:** 2026-02-25
**Dependencies:** None

## Objective

Simplify the All tab by removing clutter, merging the activity heatmap with the monthly breakdown into a single responsive section, improving mobile layout, and updating Next Up to exclude abandoned series and show more items.

## Details

1. **Remove "Media Overview" card** — Delete the `crossMediaHeroStats` section entirely (the 4 stat cards: Total Media Time, Active Now, This Year, This Month).

2. **Remove weekly activity summary text** — Delete the "This week: X episodes, Y movies..." line.

3. **Activity Heatmap — no card chrome:**
   - Switch from `sectionCardOverflow` to `sectionOpen` (no visible card background).
   - Remove the title text that shows weekly counts.

4. **Monthly Breakdown merges into Activity section:**
   - Remove the standalone Monthly Breakdown card from the right column.
   - Place the stacked bar chart to the **right** of the heatmap on desktop (side by side), **below** on mobile.
   - Legend labels show totals: "12 Movies" / "15 TV Series" / "3 Games" instead of just category names. Totals = sum across all 12 months displayed.
   - Wrap both in a responsive container: `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`.

5. **Mobile-first activity section:**
   - Heatmap must not overflow on mobile — either scale down or scroll gracefully.
   - Monthly chart stacks below heatmap on small screens.

6. **Recently Played poster sizing:**
   - Verify consistency: all poster scrollers on the All tab should use `w-[120px] sm:w-[130px]` except Series Next Up which stays at `w-[140px] sm:w-[150px]`.

7. **Next Up changes:**
   - Filter out abandoned series from `SeriesNextUp` list (check `abandoned` flag).
   - Show **10** series instead of 5 (update the `List.take` / `List.truncate` limit and the backend query limit if needed).

## Acceptance Criteria

- [x] Media Overview card is gone
- [x] Weekly summary text is gone
- [x] Activity heatmap renders without card background
- [x] Monthly breakdown is beside the heatmap on desktop, below on mobile
- [x] Monthly legend shows totals (e.g., "42h Movies")
- [x] Activity section is mobile-first and doesn't overflow
- [x] Abandoned series excluded from Next Up
- [x] Next Up shows 10 series
- [x] All existing sections (hero spotlight, movies in focus, games in focus, new games, recently played) still present

### 2026-02-25 -- Work Completed

**What was done:**
- Removed `crossMediaHeroStats` call (Media Overview card) from `allTabView`
- Removed `weeklyActivitySummary` call from `allTabView`
- Refactored `activityHeatmap` into `activityHeatmapContent` (standalone, no card chrome)
- Refactored `crossMediaMonthlyChart` into `monthlyBreakdownContent` (standalone, no card chrome)
- Created new `activitySection` combining heatmap and monthly breakdown in `sectionOpen` wrapper with `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4` responsive layout
- Updated monthly legend labels to show totals: "42h Movies" / "85h TV Series" / "15h Games" (sum across all 12 months)
- Updated SQL query in `SeriesProjection.getDashboardSeriesNextUp` to exclude abandoned series (`WHERE sl.abandoned = 0`)
- Increased backend limit from `Some 6` to `Some 11` (10 items + 1 for hero spotlight)
- Removed standalone Monthly Breakdown card from right column in allTabView
- Verified poster sizing consistency: movies/games use `w-[120px] sm:w-[130px]`, Series Next Up uses `w-[140px] sm:w-[150px]`

**Acceptance criteria status:**
- [x] Media Overview card is gone -- removed `crossMediaHeroStats` call from allTabView
- [x] Weekly summary text is gone -- removed `weeklyActivitySummary` call from allTabView
- [x] Activity heatmap renders without card background -- uses `sectionOpen` via new `activitySection`
- [x] Monthly breakdown is beside the heatmap on desktop, below on mobile -- `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`
- [x] Monthly legend shows totals (e.g., "42h Movies") -- sumBy across all 12 months
- [x] Activity section is mobile-first and doesn't overflow -- heatmap has `overflow-x-auto`, chart stacks below on mobile
- [x] Abandoned series excluded from Next Up -- SQL WHERE clause `sl.abandoned = 0`
- [x] Next Up shows 10 series -- backend limit increased to 11 (10 + 1 hero)
- [x] All existing sections still present -- verified in allTabView: hero spotlight, next up, movies in focus, games chart, games in focus, new games

**Files changed:**
- src/Client/Pages/Dashboard/Views.fs -- Removed Media Overview and weekly summary from allTabView; refactored heatmap and monthly chart into combined activity section with sectionOpen; updated monthly legend with totals
- src/Server/SeriesProjection.fs -- Updated SQL to exclude abandoned series (WHERE sl.abandoned = 0)
- src/Server/Api.fs -- Increased SeriesNextUp limit from 6 to 11
