# Task: Dashboard client — tab structure + All tab

**ID:** 007
**Milestone:** M2 - Unified Dashboard
**Size:** Large
**Created:** 2026-02-19
**Dependencies:** 006-dashboard-api

## Objective
Dashboard page reworked into a tabbed layout with a fully functional All tab.

## Details

### Tab Structure (src/Client/Pages/Dashboard/Views.fs)
- Replace current dashboard with tabbed layout: **All** | **Movies** | **TV Series** | **Games**
- Use DaisyUI tab component (role="tablist" with tab buttons)
- All tab is the default active tab
- Tab state managed in Elmish model (not local React state, since tab changes trigger API calls)

### Model Changes (src/Client/Pages/Dashboard/Types.fs)
- New `DashboardTab = All | Movies | Series | Games` DU
- Model gains: `ActiveTab: DashboardTab`, `AllTabData: DashboardAllTab option`, `MoviesTabData: DashboardMoviesTab option`, etc.
- Lazy loading: only fetch tab data when tab is activated
- Remove old fields: `RecentMovies`, `RecentSeries`, `RecentActivity` (replaced by tab-specific data)

### State Changes (src/Client/Pages/Dashboard/State.fs)
- On init: fetch All tab data
- On tab switch: fetch that tab's data if not already loaded (or always refresh)
- New messages: `SwitchTab of DashboardTab`, `AllTabLoaded of DashboardAllTab`, `MoviesTabLoaded of ...`, etc.

### All Tab Layout (Views.fs)
Four sections, each as a card/region:

**1. TV Series: Next Up**
- Section header: "Next Up" with TV icon
- Each item: poster thumbnail, series name, "S{x}E{y}: {title}", watch-with friend pills (if any)
- In Focus items visually distinguished (subtle highlight or pin icon)
- Recently finished/abandoned items shown with status badge (Finished in green, Abandoned in red)
- Clicking navigates to series detail page

**2. Movies: In Focus**
- Section header: "Movies In Focus" with film icon
- Each item: poster thumbnail, movie name, year
- Clicking navigates to movie detail page

**3. Games: In Focus**
- Section header: "Games In Focus" with gamepad icon
- Each item: cover thumbnail, game name
- Clicking navigates to game detail page

**4. Games: Recently Played**
- Section header: "Recently Played" with clock/play icon
- Each item: cover thumbnail, game name, total play time, last played date
- If HowLongToBeat data available: small progress indicator (play time vs HLTB)
- Clicking navigates to game detail page

### Layout
- On desktop: consider 2-column grid (TV Series + Movies left, Games right) or single column — pick what reads best
- On mobile: single column, sections stacked vertically
- Each section limited to ~5-6 items with no "show more" (this is a glanceable overview)

### Design
- Follow glassmorphism design system for section cards
- Consistent with existing app styling (glass cards, Oswald headings, Inter body)
- Remove the old hero section with Jellyfin sync status and stat cards

## Acceptance Criteria
- [x] Tab bar with All/Movies/TV Series/Games tabs
- [x] All tab loads and displays all four sections
- [x] TV Series sorted: In Focus pinned to top, then by recency
- [x] Each item clickable, navigates to detail page
- [x] Responsive: works on mobile (stacked) and desktop
- [x] Follows design system conventions
- [x] Old dashboard layout fully replaced
- [x] Placeholder content for Movies/Series/Games tabs

## Notes
- The Movies/Series/Games tabs will be placeholder "coming soon" in this task — filled in by tasks 008-010
- Keep it clean and glanceable — this is the daily landing page

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete

**Files changed:**
- `src/Client/Pages/Dashboard/Types.fs` — Completely rewritten. Removed old model fields (Stats, RecentMovies, RecentSeries, RecentActivity, JellyfinSyncStatus) and old Msg cases. New DashboardTab DU (All | MoviesTab | SeriesTab | GamesTab). Model now has ActiveTab, AllTabData, MoviesTabData, SeriesTabData, GamesTabData, IsLoading. New Msg: SwitchTab, AllTabLoaded, MoviesTabLoaded, SeriesTabLoaded, GamesTabLoaded, TabLoadError.
- `src/Client/Pages/Dashboard/State.fs` — Completely rewritten. init returns model with ActiveTab=All, IsLoading=true. update handles SwitchTab (fetches tab data via API), all tab-loaded messages, and TabLoadError. Private fetchTabData helper dispatches the right API call per tab.
- `src/Client/Pages/Dashboard/Views.fs` — Completely rewritten. Tab bar with four tabs using pill-style buttons (matches DesignSystem.pill pattern). All tab renders four glass card sections: TV Series Next Up (with In Focus crosshair icon, Finished/Abandoned badges, friend pills, next episode info), Movies In Focus (crosshair icon, year), Games In Focus (crosshair icon), Games Recently Played (play time, last played date, HLTB progress). Placeholder tabs show "coming soon" message. Single-column stacked layout. All items are clickable links navigating to detail pages.
- `src/Client/State.fs` — Updated init and Url_changed Dashboard handler to use getDashboardAllTab API instead of old getDashboardStats/getRecentActivity/getMovies/getRecentSeries/importJellyfinWatchHistory calls.

**Build:** `npm run build` passes cleanly with no F# errors.
