# intelligence -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 1
- **Doing:** 0
- **Done:** 15
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **intelligence-h4qk2** — Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them (refactor) — `todo/intelligence-h4qk2-dashboardalltab-s-activitydays-monthlybreakdown-payload-has.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **intelligence-dnv2y** — Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap (feature) — `done/intelligence-dnv2y-dashboard-books-tab-and-reading-rail.md`
- **intelligence-b1nz5** — All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode" (feature) — `done/intelligence-b1nz5-finished-movies-retired-games-linger-on-dashboard.md`
- **intelligence-cs2dm** — Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4) (bug) — `done/intelligence-cs2dm-collapse-remounts-cards-keyed-branch-swap.md`
- **intelligence-m09d4** — Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073) (feature) — `done/intelligence-m09d4-dashboard-expand-flip-grow-animation.md`
- **intelligence-qh8mj** — Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted. (feature) — `done/intelligence-qh8mj-dashboard-games-tab-upcoming-rail.md`
- **intelligence-c3vqm** — Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair (bug) — `done/intelligence-c3vqm-dashboard-game-poster-size-cap-drop-badge.md`
- **intelligence-p4t7k** — Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it (refactor) — `done/intelligence-p4t7k-prune-dead-newgames-dashboard-payload.md`
- **intelligence-f6cfv** — Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped (refactor) — `done/intelligence-f6cfv-next-episode-card-avatars-top-left.md`
- **intelligence-wecjh** — Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild (refactor) — `done/intelligence-wecjh-dashboard-views-dead-code-sweep.md`
- **intelligence-encn4** — Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games" (refactor) — `done/intelligence-encn4-dashboard-games-books-open-sections.md`
- **intelligence-t8n3q** — Dashboard library-search control needs a hover affordance — pointer cursor and a "Ctrl + K" tooltip (bug) — `done/intelligence-t8n3q-dashboard-search-hover-affordance.md`
- **intelligence-p9m4t** — Dashboard "Movies to Watch" — wrap posters in the filmstrip well (feature) — `done/intelligence-p9m4t-movies-to-watch-filmstrip.md`
- **intelligence-h7v2q** — Dashboard "Next episode" — cinematic hero cards (backdrop + still + progress + watched-with + Jellyfin play) (feature) — `done/intelligence-h7v2q-next-episode-cinematic-hero-cards.md`
- **intelligence-r4m2p** — Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight (bug) — `done/intelligence-r4m2p-dashboard-search-pin-responsive-split.md`
- **intelligence-dq8rk** — Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split (feature) — `done/intelligence-dq8rk-dashboard-3a-layout.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/intelligence/INDEX.md`
