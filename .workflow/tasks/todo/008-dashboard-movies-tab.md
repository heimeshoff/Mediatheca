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
