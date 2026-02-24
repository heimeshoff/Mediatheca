# Protocol

---

## 2026-02-24 -- Task Completed: 018 - Game Trailer Playback

**Type:** Task Completion
**Task:** 018 - Game Trailer Playback
**Summary:** Implemented game trailer playback with Steam Store API (primary) and RAWG API (fallback) trailer fetching, new `getGameTrailer` API endpoint, and "Play Trailer" button with HTML5 video modal overlay on the game detail page. All 232 tests pass, `npm run build` succeeds.
**Files changed:** 8 files

---

## 2026-02-24 -- Task Started: 018 - Game Trailer Playback

**Type:** Task Start
**Task:** 018 - Game Trailer Playback
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Game Trailer Playback

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/018-game-trailers.md
**Summary:** Add "Play Trailer" to game detail pages using Steam Store API (primary, direct MP4/WebM URLs) with RAWG API fallback. HTML5 `<video>` modal overlay matching movie trailer UX. Includes new shared `GameTrailerInfo` type, `getGameTrailer` API endpoint, and full Elmish state management.

---

## 2026-02-24 -- Task Completed: 017 - Jellyfin Play Button

**Type:** Task Completion
**Task:** 017 - Jellyfin Play Button
**Summary:** Added Jellyfin play buttons throughout the app -- backend persists Jellyfin item IDs (movie, series, episode-level) during scan/import into new DB columns and a mapping table; frontend shows glassmorphism play buttons on dashboard hero spotlight, series poster cards, movie in-focus poster cards, and movie detail page, all opening the Jellyfin web UI in a new tab. All 232 tests pass.
**Files changed:** 9 files

---

## 2026-02-24 -- Task Started: 017 - Jellyfin Play Button

**Type:** Task Start
**Task:** 017 - Jellyfin Play Button
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Jellyfin Play Button

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/017-jellyfin-play-button.md
**Summary:** Add "Play in Jellyfin" buttons linking to Jellyfin's web UI for direct playback. Requires persisting Jellyfin item IDs (movies, series, episodes) during library scan. Play buttons appear on: dashboard hero (next-up episode), dashboard series poster cards (next episode), and movie detail pages. Items without a Jellyfin match show no button. Embedded HLS player deferred as future enhancement.

---

## 2026-02-24 -- Task Completed: 016 - Dashboard "All" Tab Overhaul V2

**Type:** Task Completion
**Task:** 016 - Dashboard "All" Tab Overhaul V2
**Summary:** Implemented Dashboard "All" tab overhaul V2 with hero episode spotlight (episode still preferred, series backdrop fallback), open-section Next Up without card chrome, poster-style Games & Focus cards, Recently Played summary stats (total hours + sessions), New Games card with family owner badges, and live Steam achievements card with 5-minute TTL cache and error state handling. All 16 acceptance criteria met.
**Files changed:** 9 files

---

## 2026-02-24 -- Task Started: 016 - Dashboard "All" Tab Overhaul V2

**Type:** Task Start
**Task:** 016 - Dashboard "All" Tab Overhaul V2
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Dashboard "All" Tab Overhaul V2

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/016-dashboard-overhaul-v2.md
**Summary:** Major dashboard redesign: hero episode spotlight with cinematic backdrop and episode description at top-left, card-less Next Up section, poster-style Games & Focus cards, "New Games" card with family ownership badges, Recently Played stats (total hours + sessions), and live Steam achievements card with error handling. Extends 2-column layout from task 015.

---

## 2026-02-20 -- Task Completed: 015 - Dashboard "All" Tab Visual Overhaul

**Type:** Task Completion
**Task:** 015 - Dashboard "All" Tab Visual Overhaul
**Summary:** Redesigned Dashboard "All" tab with 2-column grid layout (2/3 + 1/3), TV Series poster card horizontal scroller with shine/shadow effects and In Focus badges, pure CSS stacked bar chart for game play sessions (last 14 days, 8-color palette, clickable legend), Games In Focus below the chart, and full-width Movies In Focus poster scroller. New DashboardPlaySession shared type and server-side query.
**Files changed:** 6 files

---

## 2026-02-20 -- Batch Started: [015]

**Type:** Batch Start
**Tasks:** 015 - Dashboard "All" Tab Visual Overhaul
**Mode:** Sequential (after 014 completed)

---

## 2026-02-20 -- Task Completed: 014 - Event History Viewer on Detail Pages

**Type:** Task Completion
**Task:** 014 - Event History Viewer on Detail Pages
**Summary:** Implemented event history viewer across all 5 entity detail pages with a new backend API for human-readable event formatting, two shared frontend components (ActionMenu with glassmorphism dropdown, EventHistoryModal with date-grouped timeline and category icons), and per-page integration that replaces standalone delete buttons with hover-reveal action menus. 232 tests passing.
**Files changed:** 24 files

---

## 2026-02-20 -- Batch Started: [014]

**Type:** Batch Start
**Tasks:** 014 - Event History Viewer on Detail Pages
**Mode:** Sequential (conflicts with 015 on Shared.fs and Api.fs)

---

## 2026-02-20 -- Idea Captured: Dashboard Visual Overhaul

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/015-dashboard-visual-overhaul.md
**Summary:** Redesign the dashboard "All" tab from stacked lists to a spatially varied 2-column layout. TV Series Next Up becomes a Netflix-style horizontal poster scroller (top-left, 2/3 width). Games Recently Played gets a stacked bar chart of the last 14 days (top-right, 1/3 width) with Games In Focus below it. Movies In Focus spans the full bottom row. New API endpoint for cross-game daily play sessions. Pure CSS bar chart, no charting library.

---

## 2026-02-20 -- Idea Captured: Event History Viewer

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/014-event-history-viewer.md
**Summary:** Event history viewer on every detail page (Movies, Series, Games, Friends, Catalogues). Hover-reveal action menu replaces standalone Remove/Delete buttons. "Event Log" opens a glassmorphism modal with a polished timeline of human-readable events grouped by date with icons. ContentBlocks streams merged into entity timelines. New shared `ActionMenu` and `EventHistoryModal` components.

---

## 2026-02-19 — Task Completed: 010 - Dashboard Games tab

**Type:** Task Completion
**Task:** 010 - Dashboard Games tab
**Summary:** Implemented Games tab with stats row (total games, play time, completed, in progress), recently added games section, and recently played section with HLTB comparison display.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 009 - Dashboard Series tab

**Type:** Task Completion
**Task:** 009 - Dashboard Series tab
**Summary:** Implemented TV Series tab with stats row (series, episodes, watch time), full next-up list (In Focus sorted first), recently finished section (green badges), and recently abandoned section (red badges). Refactored statBadge for reuse.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 008 - Dashboard Movies tab

**Type:** Task Completion
**Task:** 008 - Dashboard Movies tab
**Summary:** Implemented Movies tab with stats row (movies, sessions, watch time) and recently added unwatched movies as compact clickable rows with poster thumbnails.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Batch Started: [008, 009, 010]

**Type:** Batch Start
**Tasks:** 008 - Dashboard Movies tab, 009 - Dashboard Series tab, 010 - Dashboard Games tab
**Mode:** Sequential (same file conflicts — Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 007 - Dashboard client — tab structure + All tab

**Type:** Task Completion
**Task:** 007 - Dashboard client — tab structure + All tab
**Summary:** Complete dashboard rewrite. Replaced old hero/stats/activity layout with tabbed dashboard (All/Movies/Series/Games). All tab shows four glass card sections: TV Series Next Up (with In Focus pinning, friend pills, finished/abandoned badges), Movies In Focus, Games In Focus, Games Recently Played (with HLTB progress). Placeholder tabs for individual media types. Updated root State.fs for new init/navigation.
**Files changed:** 4 files (Dashboard/Types.fs, Dashboard/State.fs, Dashboard/Views.fs, root State.fs)

---

## 2026-02-19 — Batch Started: [007]

**Type:** Batch Start
**Tasks:** 007 - Dashboard client — tab structure + All tab
**Mode:** Sequential (single task, blocks 008-010)

---

## 2026-02-19 — Task Completed: 013 - HowLongToBeat display

**Type:** Task Completion
**Task:** 013 - HowLongToBeat display
**Summary:** Added fetchHltbData API endpoint, HLTB section on game detail page with progress bar comparison (play time vs HLTB average), "Fetch from HowLongToBeat" button for games without data, graceful "no data" state.
**Files changed:** 5 files (Shared.fs, Api.fs, GameDetail/Types.fs, GameDetail/State.fs, GameDetail/Views.fs)

---

## 2026-02-19 — Task Completed: 006 - Dashboard API

**Type:** Task Completion
**Task:** 006 - Dashboard API
**Summary:** Added 11 shared types and 4 API endpoints for unified dashboard tabs (All/Movies/Series/Games). Implemented query functions across MovieProjection, SeriesProjection, and GameProjection for next-up, in-focus, recently played, recently added, and stats data.
**Files changed:** 5 files (Shared.fs, Api.fs, MovieProjection.fs, SeriesProjection.fs, GameProjection.fs)

---

## 2026-02-19 — Batch Started: [006, 013]

**Type:** Batch Start
**Tasks:** 006 - Dashboard API, 013 - HowLongToBeat display
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Task Completed: 011 - Steam description backfill

**Type:** Task Completion
**Task:** 011 - Steam description backfill
**Summary:** Added description backfill phase to Steam library import. New Game_description_set event. After main import loop, queries games with empty descriptions + steam_app_id, fetches from Steam Store API with 300ms rate limiting, sets description/short_description/website/play_modes.
**Files changed:** 3 files (Games.fs, GameProjection.fs, Api.fs)

---

## 2026-02-19 — Task Completed: 005 - Game InFocus status

**Type:** Task Completion
**Task:** 005 - Game InFocus status
**Summary:** Added InFocus to GameStatus DU (Backlog → InFocus → Playing → ...) in Shared, Server, and Client. Updated filter badges, status selectors, serialization. 3 new tests, 232 total passing.
**Files changed:** 7 files (Shared.fs, Games.fs, GameProjection.fs, Games/Views.fs, GameDetail/Views.fs, GamesTests.fs)

---

## 2026-02-19 — Task Completed: 004 - Series In Focus UI

**Type:** Task Completion
**Task:** 004 - Series In Focus UI
**Summary:** Added In Focus toggle button (crosshair icon) to series detail hero section and circular badge overlay on series list poster cards. Mirrors Movie In Focus UI exactly.
**Files changed:** 4 files (SeriesDetail/Types.fs, SeriesDetail/State.fs, SeriesDetail/Views.fs, Series/Views.fs)

---

## 2026-02-19 — Batch Started: [004, 005, 011]

**Type:** Batch Start
**Tasks:** 004 - Series In Focus UI, 005 - Game InFocus status, 011 - Steam description backfill
**Mode:** Parallel (batch of 3)

---

## 2026-02-19 — Task Completed: 003 - Series In Focus backend

**Type:** Task Completion
**Task:** 003 - Series In Focus backend
**Summary:** Added Series_in_focus_set/cleared events, InFocus flag on ActiveSeries, auto-clear on episode/season/episodes-up-to watched, projection columns, shared DTOs, API endpoint, and 11 new tests. 229 tests passing.
**Files changed:** 5 files (Series.fs, SeriesProjection.fs, Shared.fs, Api.fs, SeriesTests.fs)

---

## 2026-02-19 — Task Completed: 002 - Movie In Focus UI

**Type:** Task Completion
**Task:** 002 - Movie In Focus UI
**Summary:** Added In Focus toggle button (crosshair icon) to movie detail hero section and circular badge overlay on movie list poster cards. New crosshair icons (filled, outline, small) in Icons module.
**Files changed:** 5 files (Icons.fs, MovieDetail/Types.fs, MovieDetail/State.fs, MovieDetail/Views.fs, Movies/Views.fs)

---

## 2026-02-19 — Batch Started: [002, 003]

**Type:** Batch Start
**Tasks:** 002 - Movie In Focus UI, 003 - Series In Focus backend
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Task Completed: 012 - HowLongToBeat API client

**Type:** Task Completion
**Task:** 012 - HowLongToBeat API client
**Summary:** Created HowLongToBeat.fs module with searchGame function. Implements 3-step API flow (endpoint discovery from Next.js bundles, auth token fetch, search POST). Includes caching, Jaccard similarity matching, graceful degradation, and 403 retry logic.
**Files changed:** 2 files (HowLongToBeat.fs new, Server.fsproj modified)

---

## 2026-02-19 — Task Completed: 001 - Movie In Focus backend

**Type:** Task Completion
**Task:** 001 - Movie In Focus backend
**Summary:** Added Movie_in_focus_set/cleared events, InFocus flag on ActiveMovie state, auto-clear on watch session recording, projection columns, shared DTO fields, API endpoint, and 9 new tests. 218 tests passing.
**Files changed:** 5 files (Movies.fs, MovieProjection.fs, Shared.fs, Api.fs, MoviesTests.fs)

---

## 2026-02-19 — Batch Started: [001, 012]

**Type:** Batch Start
**Tasks:** 001 - Movie In Focus backend, 012 - HowLongToBeat API client
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Planning: v1 Finish Line — 4 Milestones, 13 Tasks

**Type:** Planning
**Summary:** Broke down the v1 finish line vision into 4 milestones and 13 concrete tasks. M1 (In Focus) covers the cross-cutting In Focus concept across Movies, Series, and Games. M2 (Unified Dashboard) reworks the landing page into a tabbed layout with All/Movies/Series/Games tabs. M3 (Steam Description Backfill) adds description enrichment during Steam import. M4 (HowLongToBeat) integrates HLTB completion times via reverse-engineered internal API. HLTB research confirmed no official API — requires dynamic endpoint discovery, auth tokens, and graceful degradation.
**Milestones created/updated:** M1 (In Focus), M2 (Unified Dashboard), M3 (Steam Description Backfill), M4 (HowLongToBeat Integration)
**Tasks created:** 001-movie-in-focus-backend, 002-movie-in-focus-ui, 003-series-in-focus-backend, 004-series-in-focus-ui, 005-game-in-focus-status, 006-dashboard-api, 007-dashboard-all-tab, 008-dashboard-movies-tab, 009-dashboard-series-tab, 010-dashboard-games-tab, 011-steam-description-backfill, 012-hltb-api-client, 013-hltb-display
**Tasks moved to backlog:** None (all in todo)
**Ideas incorporated:** None

---

## 2026-02-19 — Brainstorm: v1 Finish Line — Unified Dashboard + In Focus

**Type:** Brainstorm
**Summary:** Defined the v1 finish line around a unified tabbed dashboard (All/Movies/TV Series/Games) and the cross-cutting "In Focus" concept. In Focus is a toggle flag for Movies and TV Series (auto-clearing on consumption) and a lifecycle status for Games (Backlog → InFocus → Playing → ...). Dashboard All tab shows intent-driven sections: TV Series Next Up (In Focus pinned to top, then by recency), Movies In Focus, Games In Focus, Games Recently Played. REQ-207 replaced with unified dashboard, REQ-208 narrowed to description backfill on Steam import, REQ-209 (HowLongToBeat) unchanged.
**Vision updated:** Yes
**Key decisions:**
- In Focus is a toggle flag for Movies (auto-clears on watch session) and TV Series (auto-clears on first episode watched)
- In Focus is a status in the Game lifecycle between Backlog and Playing
- TV Series dashboard sorting: In Focus pinned to top, then by most recent watch activity
- No separate Games Dashboard — unified tabbed dashboard replaces REQ-207
- REQ-208 narrowed: Steam import already works, just add description backfill for existing games
- Individual dashboard tabs (Movies/Series/Games) will grow over time with stats and intelligence

---
