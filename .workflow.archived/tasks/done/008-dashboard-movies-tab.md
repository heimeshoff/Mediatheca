# Task: Dashboard client — Movies tab

**ID:** 008
**Milestone:** M2 - Unified Dashboard
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** 007-dashboard-all-tab

## Objective
Movies tab on the dashboard shows recently added unwatched movies and movie stats.

## Details

### Content (src/Client/Pages/Dashboard/Views.fs)
- Fetch data via `getDashboardMoviesTab` API when Movies tab is activated

**Recently Added section:**
- Movies added to library, newest first
- Exclude movies that have been watched (have at least one watch session)
- Show as poster grid or list (similar to main Movies page but dashboard-sized)
- ~10 items

**Stats section:**
- Total movies in library
- Total watch sessions
- Total watch time (formatted as hours/days)
- Could be stat cards at the top of the tab (similar to old dashboard stat cards)

### State
- `MoviesTabLoaded of DashboardMoviesTab` message
- Store in model, render when Movies tab is active

## Acceptance Criteria
- [ ] Movies tab shows recently added unwatched movies
- [ ] Movies tab shows stats (count, sessions, watch time)
- [ ] Data fetched on tab activation
- [ ] Responsive layout

## Notes
- This tab will grow over time with more intelligence (genres breakdown, yearly stats, etc.)
- Keep it simple for now — recently added + stats

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete
- Replaced the Movies tab placeholder in `src/Client/Pages/Dashboard/Views.fs` with a fully functional Movies tab view
- Added `movieStatBadge` helper: renders a compact stat badge (value + label) using `bg-base-300/40` background
- Added `movieStatsRow`: displays 3 stat badges — Total Movies, Total Watch Sessions, Total Watch Time (formatted via existing `formatPlayTime`)
- Added `movieRecentlyAddedItem`: compact clickable row with poster thumbnail, movie name, and year — follows the same pattern as All tab items (e.g., `movieInFocusItem`). Navigates to `/movies/{slug}` on click.
- Added `moviesTabView`: composes stats row + "Recently Added" section card using `sectionCard Icons.movie`
- Wired `MoviesTab` case in main `view` function to render `moviesTabView` when `MoviesTabData` is `Some`, or `loadingView` when `None`
- Data fetching was already handled in State.fs (from task 007) — `SwitchTab MoviesTab` triggers `getDashboardMoviesTab` API call, result stored as `MoviesTabData`
- Build verified: `npm run build` succeeds with no errors
