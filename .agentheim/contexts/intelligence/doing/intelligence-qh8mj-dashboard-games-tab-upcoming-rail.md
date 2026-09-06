---
id: intelligence-qh8mj
title: Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.
status: doing
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
