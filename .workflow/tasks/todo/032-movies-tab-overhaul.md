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

- [ ] Layout matches the specified row structure
- [ ] Recently Watched is a horizontal poster scroller with date + companions below
- [ ] Recently Added is in the right column, narrower
- [ ] Monthly Activity and Ratings Distribution are side-by-side (50/50)
- [ ] Movies In Focus is a full-width horizontal poster scroller
- [ ] Three "Most Watched" cards in a row (Actors, Directors, Watched With)
- [ ] Genre Breakdown is a pie/donut chart
- [ ] Shared pie chart component exists in Charts.fs
- [ ] Director data is stored from TMDB imports
- [ ] Existing movies are backfilled with director data
- [ ] All existing tests pass
