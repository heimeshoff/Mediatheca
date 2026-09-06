---
id: design-system-fryq7
title: Remove the Movies / TV Series / Games items from the main menu (sidebar rail + mobile BottomNav) and delete their three list pages, plus every piece of code only those pages referenced — the Dashboard's per-media tabs already cover what they showed.
status: todo
type: refactor
context: design-system
created: 2026-09-06
completed:
depends_on: [design-system-001, intelligence-qh8mj]
blocks: []
tags: [navigation, sidebar, bottomnav, router, dead-code, movies, series, games]
related_adrs: [0014]
related_research: []
prior_art: [design-system-snpnv]
---

## Why

The main menu carries six top-group items (Dashboard / Movies / TV Series / Games / Catalogs / Friends). Three of them — Movies, TV Series, Games — open flat catalog list pages (`Pages/Movies`, `Pages/Series`, `Pages/Games`) that the Dashboard has since made redundant: it has its own Movies / Series / Games tabs (`DashboardTab`), and every "back" fallback from a detail page already lands on the Dashboard with the matching tab pre-selected (`PendingDashboardTab`), not on the list page. The vision is explicit that Mediatheca is "not a catalog to browse, but an intent-driven view of what's next." The builder asked for the three menu items and their target pages to go, together with any code that only they kept alive.

Removing them also stops the app eagerly loading the full movie, series and game lists at startup (`State.init` runs all three list pages' `init ()` on every app load purely to keep their models warm).

## What

**Navigation (design-system owns the rail — see README "Layered sidebar nav"):**
- Drop the `Movies`, `TV Series`, `Games` entries from `Components/Sidebar.fs` and the `Movies`, `Series`, `Games` entries from `Components/BottomNav.fs`. Remaining top group: Dashboard / Catalogs / Friends (bottom group untouched).
- The Dashboard nav item stays active while on a `Movie_detail` / `Series_detail` / `Game_detail` page (those are reached from, and return to, the Dashboard), so the rail always has an active item. Replace `Route.isMoviesSection` / `isSeriesSection` / `isGamesSection` with a single `Route.isDashboardSection` covering `Dashboard` + the three detail cases.
- Update the StyleGuide "Sidebar Nav" specimen (`Pages/StyleGuide/Views.fs`, ~L1430–1500) so it shows the real rail's items, not Movies/TV Series/Games. `DesignSystem.listPageHeaderPattern`'s "Movies" specimen text is just sample copy — leave it (Catalogs/Friends still use the pattern).

**Pages and router:**
- Delete `src/Client/Pages/Movies/`, `src/Client/Pages/Series/`, `src/Client/Pages/Games/` (Types/State/Views) and their `<Compile>` entries in `Client.fsproj`.
- Remove `Movie_list` / `Series_list` / `Game_list` from the `Page` DU, `toUrl`, `navigateTo`; remove `MovieListModel` / `SeriesListModel` / `GameListModel` and `Movie_list_msg` / `Series_list_msg` / `Game_list_msg` from the root `Types.fs` / `State.fs` / `Views.fs`.
- Bare `/movies`, `/series`, `/games` URLs (old bookmarks, browser history from earlier sessions) resolve to `Dashboard` with the matching tab pre-selected via the existing `PendingDashboardTab` mechanism — not `Not_found`. The `/movies/{slug}` etc. detail routes are untouched.

**Detail pages' list-page escapes (three each: Movie/Series/Game):**
- The post-delete `Router.navigate "movies"|"series"|"games"` in each detail `State.fs` (`Movie_removed` / `Series_removed` / `Game_removed` Ok branches) and the "Back to Movies/Series/Games" link in each detail `Views.fs` not-found branch now go to the Dashboard (matching tab preferred, plain Dashboard acceptable). No client code may target the three list routes afterwards.

**Global search modal — the one real entanglement:**
- `SearchModal.initWithGames` is seeded at all five open sites (`Open_search_modal`, `Dashboard.Open_search_modal`, and the three list pages' own open messages) from the three list models' `Movies` / `Series` / `Games`, and `filterLibrary` searches those lists for local library hits. With the list models gone, the modal must obtain its library snapshot itself: recommended — the root `Open_search_modal` handler opens the modal empty and batches `api.getMovies` / `api.getSeries` / `api.getGames` into a new `SearchModal.Library_loaded` message (fresh on every open; drop the `Load_movies`/`Load_series`/`Load_games` reload commands in `Import_completed`, which only existed to refresh those models). Any equivalent that keeps local library search working is fine.

**Dead code that goes with the pages (verify each has no other consumer before deleting — `npm run build` is the oracle):**
- `Pages.Games.Types.PlayFacetFilter` and the status/facet filter machinery (games-j6wkr, client-only).
- `DesignSystem.statusBadgeLabel`, `PlayFacetsDisplay.facetBadges` — the tree-wide grep found no consumer outside the three list Views (`releaseDateBadge` was on this list until intelligence-qh8mj adopted it; verify).
- The Games page's **Upcoming section** (games-ev65k) has moved to the Dashboard Games tab by intelligence-qh8mj (a dependency of this task), which reads `GameProjection.getUpcomingGames` through `getDashboardGamesTab`. So **keep** `GameProjection.getUpcomingGames` and its Expecto cases, and keep `ReleaseDate` / `IsUnreleased` plus `ReleaseDateParsing`. Drop only the standalone `IMediathecaApi.getUpcomingGames` endpoint (Shared.fs + its `Api.fs` handler), which the deleted page was the sole caller of. `PlayFacetsDisplay.releaseDateBadge` is likewise expected to survive as the Dashboard rail's badge — re-check before deleting.
- The three `Route.is*Section` predicates (replaced as above).

## Acceptance criteria

- [ ] `Components/Sidebar.fs` and `Components/BottomNav.fs` contain no `Movie_list` / `Series_list` / `Game_list` item; the rail's top group is Dashboard / Catalogs / Friends.
- [ ] `src/Client/Pages/Movies`, `src/Client/Pages/Series`, `src/Client/Pages/Games` no longer exist and `Client.fsproj` has no `<Compile>` entry for them.
- [ ] `Router.Page` has no `Movie_list` / `Series_list` / `Game_list` case; `grep -rn 'format "movies"\|format "series"\|format "games"\|navigate "movies"\|navigate "series"\|navigate "games"' src/Client` (excluding tuple detail-route forms) is empty.
- [ ] `Route.parseUrl ["movies"]`, `["series"]`, `["games"]` return `Dashboard`; the detail routes `["movies"; slug]` etc. still return the detail page (cover with a `*.test.fs` Vitest case per ADR-0064).
- [ ] Deleting a movie / series / game from its detail page, and the detail pages' not-found "Back to …" links, navigate to the Dashboard — no navigation targets a removed route.
- [ ] Opening the global search modal (sidebar button, keyboard shortcut, Dashboard) with a non-empty library still lists matching library movies, series and games in the local-results section; a search-modal `*.test.fs` or an e2e spec covers the seeded-from-fetch path.
- [ ] Root `State.init` no longer issues `getMovies` / `getSeries` / `getGames` at app start.
- [ ] `IMediathecaApi.getUpcomingGames` and its `Api.fs` handler are gone; `GameProjection.getUpcomingGames` (now fed to the Dashboard by intelligence-qh8mj), its tests, `ReleaseDate` / `IsUnreleased` and `ReleaseDateParsing` remain and pass.
- [ ] `DesignSystem.statusBadgeLabel`, `PlayFacetsDisplay.facetBadges`, `PlayFacetFilter`, and `Route.isMoviesSection` / `isSeriesSection` / `isGamesSection` are gone (or, if a consumer turned up, the task Outcome names it).
- [ ] The StyleGuide "Sidebar Nav" specimen renders the same item set as the live rail.
- [ ] `npm run build` is clean, `npm test` passes, `npm run test:client` passes.
- [ ] The rail still shows an active item while on a Movie / Series / Game detail page (Dashboard highlighted). [human-eye]

## Notes

- **Resolved 2026-09-06:** the builder chose to keep the Games list page's *Upcoming* section (games-ev65k) — intelligence-qh8mj moves it onto the Dashboard Games tab first, and this task now depends on it. Only the standalone `getUpcomingGames` API endpoint is deleted here; the projection query stays.
- Prior art: **design-system-snpnv** shipped the list-page type scale (grid captions, page header, filter pills) — those primitives stay because Catalogs/Friends use them; only their three original consumers vanish. ADR-0014 governs the rail's active-state look; this task changes membership, not the look.
- Per-BC README deltas to report: design-system README "Layered sidebar nav" (item list), games README (Upcoming section / list-page filters), movies + series READMEs if they describe the list page.
- `tests/e2e/game-detail-persistent-cards.spec.ts` only visits `/#/games/{slug}` — unaffected.
- The current uncommitted edit in `src/Client/DesignSystem.fs` (filmstrip `scrollbarHidden`) is the builder's own WIP, unrelated — don't fold it into this task's diff.
