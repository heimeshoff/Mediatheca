# Task: Movies Tab Overhaul

**ID:** 032
**Milestone:** M5 (Dashboard V3)
**Size:** Large
**Created:** 2026-02-25
**Dependencies:** 031

## Objective

Restructure the Movies tab layout, convert lists to poster scrollers, add director tracking (backend + frontend), and replace the genre bar chart with a pie/donut chart.

## Details

1. **Layout restructure (top to bottom):**

   **Row 1:** Recently Watched (wider, ~2/3) | Recently Added (narrower, ~1/3)
   - `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`

   **Row 2:** Monthly Activity (50%) | Ratings Distribution (50%)
   - `grid grid-cols-1 md:grid-cols-2 gap-4`

   **Row 3:** Movies In Focus — full-width horizontal poster scroller

   **Row 4:** Most Watched Actors | Most Watched Directors | Most Watched With
   - `grid grid-cols-1 md:grid-cols-3 gap-4`

   **Row 5:** Genre Breakdown — pie chart (replaces horizontal bar graph)

2. **Recently Watched — horizontal poster scroller:**
   - Convert from list format to horizontal poster scroller.
   - Each poster card shows: movie poster, title below, watch date, companion names (if any).
   - Sorted by watch date (most recent first) — already the case.

3. **Movies In Focus — horizontal poster scroller:**
   - Convert from whatever current format to a full-width horizontal poster scroller.

4. **Genre Breakdown — pie chart:**
   - Replace the horizontal bar graph with a pie/donut chart.
   - Each genre gets a color segment with label.
   - Create shared pie chart component in `src/Client/Components/Charts.fs` for reuse by series and games tabs.

5. **NEW: Most Watched Directors (backend + frontend):**
   - **Backend:** Store director data from TMDB imports.
     - Add `director` column (or a `movie_crew` table) to track director(s) per movie.
     - During TMDB movie import/detail fetch, extract director from the credits crew list (job = "Director").
     - Backfill existing movies if possible.
   - **New projection query:** Aggregate top directors by watch count.
   - **New DTO:** `DashboardTopDirector` with name, movie count, optional profile image URL.
   - **Frontend:** Render same style as Most Watched Actors (circular photos, name, count).

## Acceptance Criteria

- [x] Layout matches the specified row structure
- [x] Recently Watched is a horizontal poster scroller with date + companions below
- [x] Recently Added is in the right column, narrower
- [x] Monthly Activity and Ratings Distribution are side-by-side (50/50)
- [x] Movies In Focus is a full-width horizontal poster scroller
- [x] Three "Most Watched" cards in a row (Actors, Directors, Watched With)
- [x] Genre Breakdown is a pie/donut chart
- [x] Shared pie chart component exists in Charts.fs
- [x] Director data is stored from TMDB imports
- [x] Existing movies are backfilled with director data
- [x] All existing tests pass

### 2026-02-25 -- Work Completed

**What was done:**
- Added `movie_crew` table to CastStore for storing director/crew data (linked to existing `cast_members` table)
- Added `addMovieCrew` function to CastStore for inserting crew records
- Updated `removeMovieCastAndCleanup` to also clean up movie_crew rows and check crew references before orphan cleanup
- Updated `removeSeriesCastAndCleanup` to check movie_crew references before orphan cleanup
- Added `getMoviesWithoutCrew` query to CastStore for identifying movies needing backfill
- Modified `addMovieToLibrary` in Api.fs to store directors (job = "Director") from TMDB credits during import
- Added director backfill logic to Program.fs that runs at startup, fetching crew from TMDB for existing movies
- Added `MoviesInFocus` and `JellyfinServerUrl` fields to `DashboardMoviesTab` shared type
- Updated `getDashboardMoviesTab` server endpoint to populate the new fields
- Created shared `Charts.fs` component with `donutChart` function (SVG donut with legend) for reuse across tabs
- Restructured Movies tab layout to 5-row design: (1) RecentlyWatched 2/3 + RecentlyAdded 1/3, (2) MonthlyActivity + RatingsDistribution 50/50, (3) MoviesInFocus full-width poster scroller, (4) Actors + Directors + WatchedWith in 3 columns, (5) Genre Breakdown as donut chart
- Created `recentlyWatchedPosterCard` component showing poster, title, date, and companion names
- Genre Breakdown now uses `Charts.donutChart` instead of horizontal bar chart
- Country distribution section retained as bonus row below genre breakdown

**Acceptance criteria status:**
- [x] Layout matches the specified row structure -- verified by code review of moviesTabView grid structure
- [x] Recently Watched is a horizontal poster scroller with date + companions below -- recentlyWatchedPosterCard renders poster, date, friends
- [x] Recently Added is in the right column, narrower -- grid-cols-[2fr_1fr] layout
- [x] Monthly Activity and Ratings Distribution are side-by-side (50/50) -- md:grid-cols-2
- [x] Movies In Focus is a full-width horizontal poster scroller -- uses movieInFocusPosterCard in overflow scroller
- [x] Three "Most Watched" cards in a row (Actors, Directors, Watched With) -- md:grid-cols-3
- [x] Genre Breakdown is a pie/donut chart -- Charts.donutChart replaces genreBreakdownBars
- [x] Shared pie chart component exists in Charts.fs -- src/Client/Components/Charts.fs created
- [x] Director data is stored from TMDB imports -- addMovieCrew called in addMovieToLibrary
- [x] Existing movies are backfilled with director data -- backfillDirectors runs at server startup
- [x] All existing tests pass -- npm test: 233 passed, 0 failed

**Files changed:**
- `src/Shared/Shared.fs` -- Added MoviesInFocus and JellyfinServerUrl to DashboardMoviesTab
- `src/Server/CastStore.fs` -- Added movie_crew table, addMovieCrew, getMoviesWithoutCrew; updated cleanup functions
- `src/Server/Api.fs` -- Store directors on movie import; populate MoviesInFocus/JellyfinServerUrl in movies tab
- `src/Server/Program.fs` -- Added director backfill at startup
- `src/Client/Components/Charts.fs` -- New shared donut chart component
- `src/Client/Client.fsproj` -- Added Charts.fs compile entry
- `src/Client/Pages/Dashboard/Views.fs` -- Restructured moviesTabView to 5-row layout with poster scrollers and donut chart
