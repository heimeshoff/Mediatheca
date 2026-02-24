# Task: TV Series Tab Overhaul

**ID:** 033
**Milestone:** M5 (Dashboard V3)
**Size:** Medium
**Created:** 2026-02-25
**Dependencies:** 032

## Objective

Restructure the TV Series tab by removing Episode Activity, converting Next Up to a poster scroller, rearranging the layout, and replacing the genre bar chart with a pie chart (reusing the shared component from 032).

## Details

1. **Remove Episode Activity card** — Delete entirely.

2. **Layout restructure (top to bottom):**

   **Row 1:** Next Up — full-width horizontal poster scroller
   - Poster cards with series poster, extra info below (episode label like S2E5, progress, companions).
   - **Exclude abandoned series.**
   - Show generous count (10+).

   **Row 2:** Recently Finished (50%) | Recently Abandoned (50%)
   - `grid grid-cols-1 md:grid-cols-2 gap-4`
   - Both sorted by **last watch date** (most recent first).

   **Row 3:** Monthly Activity | Ratings Distribution | Genre Breakdown (pie chart)
   - `grid grid-cols-1 md:grid-cols-3 gap-4` or stacked as appropriate.
   - Genre Breakdown as **pie chart** (reuse shared component from Charts.fs).

3. **Recently Finished and Recently Abandoned:**
   - Keep existing list format but ensure sorted by last watch date.
   - May need backend query adjustment if currently sorted differently.

## Acceptance Criteria

- [x] Episode Activity card is removed
- [x] Next Up is a full-width horizontal poster scroller with info below
- [x] Abandoned series excluded from Next Up
- [x] Recently Finished and Recently Abandoned are side-by-side
- [x] Both sorted by last watch date
- [x] Monthly Activity, Ratings Distribution, Genre Breakdown in a row
- [x] Genre Breakdown is a pie chart (using shared Charts.fs component)
- [x] All existing tests pass

### 2026-02-25 -- Work Completed

**What was done:**
- Removed Episode Activity card from the series tab view
- Converted Next Up from a list to a full-width horizontal poster scroller with poster images, episode labels (S2E5), progress bars showing watched/total episodes, and companion friend pills
- Added explicit filter to exclude abandoned series from Next Up display
- Moved Recently Finished and Recently Abandoned into a side-by-side grid layout (grid-cols-1 md:grid-cols-2)
- Updated backend queries for getRecentlyFinished and getRecentlyAbandoned to sort by last watch date (MAX(watched_date) from series_episode_progress) instead of rowid
- Restructured bottom row to show Monthly Activity, Ratings Distribution, and Genre Breakdown in a 3-column grid (grid-cols-1 md:grid-cols-3)
- Replaced genre bar chart with donut/pie chart using shared Charts.donutChart component
- Added JellyfinServerUrl field to DashboardSeriesTab type and populated it in the API handler to support Jellyfin play buttons on poster cards

**Acceptance criteria status:**
- [x] Episode Activity card is removed -- seriesTabView no longer includes episodeActivityChart
- [x] Next Up is a full-width horizontal poster scroller with info below -- uses sectionCardOverflow with seriesTabPosterCard showing poster, episode label, progress bar, companions
- [x] Abandoned series excluded from Next Up -- explicit List.filter (fun s -> not s.IsAbandoned)
- [x] Recently Finished and Recently Abandoned are side-by-side -- grid grid-cols-1 md:grid-cols-2 gap-4
- [x] Both sorted by last watch date -- SQL queries updated with LEFT JOIN on MAX(watched_date) from series_episode_progress
- [x] Monthly Activity, Ratings Distribution, Genre Breakdown in a row -- grid grid-cols-1 md:grid-cols-3 gap-4
- [x] Genre Breakdown is a pie chart (using shared Charts.fs component) -- Charts.donutChart data.Stats.GenreDistribution
- [x] All existing tests pass -- 233 tests pass

**Files changed:**
- src/Shared/Shared.fs -- Added JellyfinServerUrl field to DashboardSeriesTab type
- src/Server/Api.fs -- Populated JellyfinServerUrl in getDashboardSeriesTab handler
- src/Server/SeriesProjection.fs -- Updated getRecentlyFinished and getRecentlyAbandoned to sort by last watch date
- src/Client/Pages/Dashboard/Views.fs -- Rewrote seriesTabView: removed Episode Activity, added seriesTabPosterCard with progress bar, poster scroller for Next Up, side-by-side Recently Finished/Abandoned, 3-column stats row with donut chart for genre
