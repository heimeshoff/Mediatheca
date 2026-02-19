# Protocol

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
