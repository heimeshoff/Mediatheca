# Task: Dashboard Overhaul V3 — Full Tab Redesign

**ID:** 030
**Milestone:** M5 (Dashboard V3)
**Size:** Large
**Created:** 2026-02-25
**Dependencies:** None (builds on existing dashboard from 027)

## Objective

Major layout and feature overhaul of all four dashboard tabs. Removes clutter, merges related sections, introduces new visualizations (pie charts, spider graphs), adds director tracking, and makes everything mobile-first.

## Subtasks

- **031** — All Tab Overhaul
- **032** — Movies Tab Overhaul (includes director tracking backend)
- **033** — TV Series Tab Overhaul
- **034** — Games Tab Overhaul (includes In-Focus estimate rework)

---

## 031: All Tab Overhaul

### Changes

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
   - Change `gamePosterFromSession` from `w-[120px] sm:w-[130px]` — keep at this size (already matches Games In Focus).
   - Verify consistency: all poster scrollers on the All tab should use `w-[120px] sm:w-[130px]` except Series Next Up which stays at `w-[140px] sm:w-[150px]`.

7. **Next Up changes:**
   - Filter out abandoned series from `SeriesNextUp` list (check `abandoned` flag).
   - Show **10** series instead of 5 (update the `List.take` / `List.truncate` limit and the backend query limit if needed).

### Acceptance Criteria

- [ ] Media Overview card is gone
- [ ] Weekly summary text is gone
- [ ] Activity heatmap renders without card background
- [ ] Monthly breakdown is beside the heatmap on desktop, below on mobile
- [ ] Monthly legend shows totals (e.g., "12 Movies")
- [ ] Activity section is mobile-first and doesn't overflow
- [ ] Abandoned series excluded from Next Up
- [ ] Next Up shows 10 series
- [ ] All existing sections (hero spotlight, movies in focus, games in focus, new games, recently played) still present

---

## 032: Movies Tab Overhaul

### Changes

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

5. **NEW: Most Watched Directors (backend + frontend):**
   - **Backend:** Store director data from TMDB imports.
     - Add `director` column (or a `movie_crew` table) to track director(s) per movie.
     - During TMDB movie import/detail fetch, extract director from the credits crew list (job = "Director").
     - Backfill existing movies if possible.
   - **New projection query:** Aggregate top directors by watch count.
   - **New DTO:** `DashboardTopDirector` with name, movie count, optional profile image URL.
   - **Frontend:** Render same style as Most Watched Actors (circular photos, name, count).

### Acceptance Criteria

- [ ] Layout matches the specified row structure
- [ ] Recently Watched is a horizontal poster scroller with date + companions below
- [ ] Recently Added is in the right column, narrower
- [ ] Monthly Activity and Ratings Distribution are side-by-side (50/50)
- [ ] Movies In Focus is a full-width horizontal poster scroller
- [ ] Three "Most Watched" cards in a row (Actors, Directors, Watched With)
- [ ] Genre Breakdown is a pie/donut chart
- [ ] Director data is stored from TMDB imports
- [ ] Existing movies are backfilled with director data
- [ ] All existing tests pass

---

## 033: TV Series Tab Overhaul

### Changes

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
   - Genre Breakdown as **pie chart** (replaces bar graph).

3. **Recently Finished and Recently Abandoned:**
   - Keep existing list format but ensure sorted by last watch date.
   - May need backend query adjustment if currently sorted differently.

### Acceptance Criteria

- [ ] Episode Activity card is removed
- [ ] Next Up is a full-width horizontal poster scroller with info below
- [ ] Abandoned series excluded from Next Up
- [ ] Recently Finished and Recently Abandoned are side-by-side
- [ ] Both sorted by last watch date
- [ ] Monthly Activity, Ratings Distribution, Genre Breakdown in a row
- [ ] Genre Breakdown is a pie chart
- [ ] All existing tests pass

---

## 034: Games Tab Overhaul

### Changes

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
   - Status Distribution: convert from stacked bar to **pie chart**.
   - Genre Breakdown: convert from horizontal bars to **spider/radar chart**.

   **Row 4:** Monthly Play Time (2/3 left) | Recent Achievements (1/3 right)
   - `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`
   - Monthly Play Time: stacked/grouped bar chart showing **individual games color-coded** (not just a single "games" total).
   - Recent Achievements: existing Steam achievements section, moved here.

3. **Monthly Play Time — per-game color coding:**
   - Each game gets its own color in the bar chart.
   - Show which games were played each month as stacked segments.
   - Legend maps colors to game names.
   - May need backend changes to return per-game monthly data instead of aggregated totals.

### Acceptance Criteria

- [ ] In-Focus Estimate shows clamped remaining HLTB time for In-Focus games
- [ ] Recently Played includes finished games, excludes dismissed
- [ ] Recently Added excludes dismissed games
- [ ] Both are horizontal poster scrollers
- [ ] Status Distribution is a pie chart
- [ ] Genre Breakdown is a spider/radar chart
- [ ] Monthly Play Time shows per-game color-coded bars
- [ ] Recent Achievements is in the right column
- [ ] Monthly Play Time is in the left 2/3
- [ ] All existing tests pass

---

## Shared Work

### New Chart Components Needed

- **Pie/Donut chart** — Used in Movies (genre), Series (genre), Games (status distribution)
- **Spider/Radar chart** — Used in Games (genre breakdown)
- **Per-game stacked bar chart** — Monthly play time with individual game colors

Consider extracting to `src/Client/Components/Charts.fs` since pie charts are used in 3 places.

### Backend Changes Summary

- **Director tracking:** New storage + TMDB import integration + backfill + projection query (task 032)
- **In-Focus estimate query:** Filter games by InFocus status, join HLTB data, clamp calculation (task 034)
- **Per-game monthly play time:** New query returning monthly minutes per game slug (task 034)
- **Series Next Up limit:** Increase from 5 to 10, exclude abandoned (task 031 + 033)
- **Sort adjustments:** Recently Finished/Abandoned by last watch date (task 033)
- **Dismissed game filtering:** Exclude from recently played/added queries (task 034)

## Files Likely Changed

1. `src/Shared/Shared.fs` — New/updated DTOs
2. `src/Server/MovieProjection.fs` — Director queries, recently watched adjustments
3. `src/Server/SeriesProjection.fs` — Next Up limit/filter, sort adjustments
4. `src/Server/GameProjection.fs` — In-Focus estimate, per-game monthly, dismissed filtering
5. `src/Server/Api.fs` — Updated tab data assembly
6. `src/Server/TmdbClient.fs` or import module — Director extraction from TMDB
7. `src/Client/Pages/Dashboard/Views.fs` — Major view restructuring across all tabs
8. `src/Client/Pages/Dashboard/Types.fs` — Model updates
9. `src/Client/Pages/Dashboard/State.fs` — Data loading adjustments
10. `src/Client/Components/Charts.fs` — New shared chart components (pie, spider, per-game bars)
