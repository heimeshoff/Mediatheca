---
id: intelligence-qh8mj
title: Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.
status: done
type: feature
context: intelligence
created: 2026-09-06
completed:
depends_on: [design-system-001]
blocks: [design-system-fryq7]
tags: [dashboard, games-tab, upcoming, release-date, poster-rail]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk, intelligence-c3vqm]
---

## Why

design-system-fryq7 removes the Movies / TV Series / Games list pages. The Games list page is the only surface that shows **Upcoming** — unreleased Steam-linked games, sorted soonest-first with TBA last (games-ev65k). The builder wants that view kept, so it moves onto the Dashboard's Games tab *before* the list page goes. The server side already exists and is Expecto-tested (`GameProjection.getUpcomingGames`); this task is a read-model field plus a rail.

## What

- **DTO:** add `Upcoming: GameListItem list` to `DashboardGamesTab` (`src/Shared/Shared.fs`).
- **Server:** `getDashboardGamesTab` in `Api.fs` fills it from the existing `GameProjection.getUpcomingGames conn` — no new query, no change to its filter/sort semantics or its tests.
- **Client:** `gamesTabView` (`Pages/Dashboard/Views.fs`) renders an "Upcoming" section — a horizontal poster rail in the same `sectionCardOverflow` chrome as "Recently Played" / "Recently Added" (`overflow-x-auto snap-x snap-mandatory scroll-px-2` + `DesignSystem.scrollbarHidden`, per design-system-hs4vm). Recommended placement: its own full-width row directly under Row 2 (Recently Played | Recently Added). **Absent, not empty-rendered**, when there are no unreleased games — matching the list page's behaviour, so the tab layout is unchanged for a library with nothing upcoming.
- **Card:** reuse `gameRecentlyAddedPosterCard` (or a sibling) and carry the release-date / "Upcoming" badge the list page's card showed via `PlayFacetsDisplay.releaseDateBadge`, so the date (or "Upcoming" for TBA) reads on the poster — that badge becomes this rail's consumer once the list page is gone.
- Leave the Games list page itself untouched — design-system-fryq7 deletes it and, with this task done, keeps `GameProjection.getUpcomingGames` alive and drops only the now-unreferenced `IMediathecaApi.getUpcomingGames` endpoint.

## Acceptance criteria

- [ ] `DashboardGamesTab` has an `Upcoming: GameListItem list` field and `getDashboardGamesTab` populates it from `GameProjection.getUpcomingGames` (an Expecto case over `getDashboardGamesTab` or the projection asserts an unreleased game appears in it, soonest-first, and a released game does not).
- [ ] The Dashboard Games tab renders an "Upcoming" rail with one poster per item when `Upcoming` is non-empty, and renders no "Upcoming" section at all when it is empty.
- [ ] Each Upcoming poster links to `/games/{slug}` and shows the release-date badge (or "Upcoming" for TBA).
- [ ] The rail uses `DesignSystem.scrollbarHidden` and the same snap/scroll classes as the sibling Games-tab rails.
- [ ] Existing `GameReleaseDateProjectionTests` / `AddGameFromSteamTests` cases for `getUpcomingGames` are unchanged and pass.
- [ ] `npm run build` clean, `npm test` passes.
- [ ] The rail sits naturally in the Games tab's rhythm alongside the other poster rails. [human-eye]

## Notes

- Ordered ahead of design-system-fryq7 by design (`blocks`); fryq7's `depends_on` names this task.
- Prior art: intelligence-dq8rk (3a Dashboard layout, the section-card chrome), intelligence-c3vqm (Games poster sizing on the Dashboard — match the capped size). games-ev65k (games BC) is the origin of the Upcoming semantics.
- README delta to report: intelligence README (Games tab sections) and games README (Upcoming section now lives on the Dashboard).

## Outcome

Added `DashboardGamesTab.Upcoming: GameListItem list` (`src/Shared/Shared.fs`), wired in `Api.fs`'s `getDashboardGamesTab` straight from the existing, already-tested `GameProjection.getUpcomingGames conn` (no new query, no filter/sort change). Added an Expecto case in `tests/Server.Tests/GameReleaseDateProjectionTests.fs` that exercises the real `IMediathecaApi.getDashboardGamesTab` handler (via `Api.create` over a `TestDb`-backed factory) and asserts `Upcoming` is soonest-first and excludes a released game — driven red (asserted against a temporary `Upcoming = []` stub) before the real wiring went in, then green.

On the client, `Pages/Dashboard/Views.fs`'s `gamesTabView` gained a new full-width row directly under Row 2 (Recently Played | Recently Added), using the same `sectionCardOverflow`/`scrollbarHidden`/snap-scroll chrome as its siblings (`Icons.calendar`, title "Upcoming"), rendered only `if not (List.isEmpty data.Upcoming)` — absent, not empty-rendered, matching the list page's prior behaviour. Added `gameUpcomingPosterCard`, a sibling of `gameRecentlyAddedPosterCard` (same 130px poster size per intelligence-c3vqm) whose bottom line is `PlayFacetsDisplay.releaseDateBadge item.ReleaseDate` instead of the release year, and which links to `/games/{slug}` exactly like the other Games-tab poster cards.

`npm run build` is clean; the full Expecto suite is green (686 passed, up from 685 baseline — the one new case). The Games list page itself was left untouched, as instructed — `design-system-fryq7` (which depends on this task) will delete it and drop only the now-unreferenced `IMediathecaApi.getUpcomingGames` endpoint, since `GameProjection.getUpcomingGames` now has this Dashboard consumer too.

**Games README delta (another BC — reported here, not applied):** `.agentheim/contexts/games/README.md` around line 152-154 currently reads "...and the Games tab's Upcoming section (`GameProjection.getUpcomingGames`, soonest-first, TBA/unparseable last, absent when nothing is unreleased) all share, so the three surfaces can never disagree about what counts as upcoming." This should become "...and the Dashboard Games tab's Upcoming rail (`GameProjection.getUpcomingGames`, soonest-first, TBA/unparseable last, absent when nothing is unreleased; moved from the Games list page by intelligence-qh8mj) all share, so the three surfaces can never disagree about what counts as upcoming." — a `replace` op with anchor `IsUnreleased` (the bullet's bold lead-in) once `design-system-fryq7` also lands, since that task is the one actually deleting the list page this bullet currently describes.
