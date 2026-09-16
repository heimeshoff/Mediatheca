---
id: intelligence-dnv2y
title: Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap
status: done
type: feature
context: intelligence
created: 2026-09-16
completed:
depends_on: [books-y9kxy, books-f33e2, design-system-001-formalize-styleguide]
blocks: []
tags: [books, dashboard, frontend, reading-progress, heatmap]
related_adrs: [0076, 0077, 0073, 0016]
related_research: []
prior_art: [intelligence-b1nz5, intelligence-qh8mj, intelligence-m09d4]
---

## Why

The dashboard is the app's only landing surface (the standalone list pages were removed by
design-system-fryq7); without a Books presence a book is reachable only via search or URL. The All
tab has carried a "Books coming soon" placeholder since the tabbed dashboard shipped — this task
replaces it, and gives books the same intent-driven treatment the vision gives movies/series/games.

## What

**Server** (`Api.fs`, `Shared.fs`, `BookProjection`):
- `DashboardAllTab` gains `CurrentlyReading: DashboardBookItem list` — In Focus books ordered by
  latest `progress_observed_on` desc (then `added_at` desc), **plus** books with `finished_at` within
  the last 7 days marked `Finished = true` (the intelligence-b1nz5 linger, via a split query so the
  Books tab's own card stays strict) — `DashboardBookItem = { Slug; Title; Authors; CoverRef;
  ProgressPercent; ProgressSource; Finished: bool; FinishedOn }`, row-limited like the other rails.
- `getDashboardBooksTab: unit -> Async<DashboardBooksTab>` = `{ CurrentlyReading (strict, no linger);
  RecentlyFinished (last 90 days, newest first); RecentlyAdded (newest first, unfinished);
  Stats: { Total; InFocus; FinishedThisYear; FinishedAllTime; PagesReadThisYear: int option (sum of
  page positions of the latest observation per finished-this-year book, when known); HoursListened
  ThisYear: float option (Audible-sourced runtime × percent, when known) } }`.
- `DashboardCardQuery` gains `AllCurrentlyReading | BooksCurrentlyReading | BooksRecentlyFinished |
  BooksRecentlyAdded` for the expand-in-place flow (`getDashboardCardItems`, ADR-0073's FLIP keys on
  the book slug).
- `DashboardActivityDay` gains `Reading: int` (distinct books with an observation that day) from
  `BookProjection.getDailyReadingActivity`, merged into the 365-day heatmap and the monthly breakdown.

**Client** (`Pages/Dashboard/{Types,State,Views}.fs`):
- `DashboardTab` gains `BooksTab` ("Books" after "Games" in the tab bar); `Model.BooksTabData`;
  `SwitchTab BooksTab` loads it.
- All tab: replace `booksColumnPlaceholder` with a **Reading** card in the same xl:2-col slot beside
  Games: portrait cover rail, each card overlaying a thin progress bar (gold) along the bottom edge
  with the percent in `font-mono` and a small source glyph; finished-lingering items get the green
  "Finished" pill (the exact "Watched"/"Retired" pill treatment from b1nz5). Expand/collapse via the
  existing `ExpandCard` flow (ADR-0073 FLIP, keyed on slug).
- Books tab: Currently Reading (same cards), Recently Finished, Recently Added rails; a stats block
  in the existing `DesignSystem` stat-tile vocabulary (Total / In Focus / Finished this year /
  Pages read / Hours listened — hide a tile whose value is `None`).
- Cards navigate to `Book_detail slug`. Heatmap tooltip gains "N books read".

## Acceptance criteria

- [ ] `DashboardBooksTests.fs` (`TestDb`): with one In Focus book at 40 %, one finished 3 days ago,
      one finished 10 days ago and one Backlog book — `getDashboardAllTab.CurrentlyReading` has
      exactly two items (the 40 % one first, the 3-day finished one marked `Finished`);
      `getDashboardBooksTab.CurrentlyReading` has exactly one; `RecentlyFinished` has two.
- [ ] `DashboardActivityDay.Reading` counts distinct books per day; a day with two observations of
      one book counts 1.
- [ ] `getDashboardCardItems AllCurrentlyReading` with `RowLimit = None` returns every qualifying item.
- [ ] `Pages/Dashboard/ExpandCard.test.fs` extended: expanding the Reading card keeps every item's
      FLIP key equal to its slug (no fade on survivors — design-system-btmdx's rule).
- [ ] `TableClassificationTests` and the drift check stay green (no new tables here).
- [ ] The Reading card sits beside Games with the same poster size cap the b1nz5/c3vqm work set,
      progress bars read cleanly at rail size, the finished pill matches the Watched/Retired pills. [human-eye]
- [ ] `npm run build`, `npm run test:client`, `npm test` green.

## Notes

- Prior art to read first: `intelligence-b1nz5` (7-day linger + split query + pill), `intelligence-
  qh8mj` (adding a rail to a tab), `intelligence-m09d4` / ADR-0073 (expand-in-place, keys).
- `Views.fs` lines ~896 (`booksColumnPlaceholder`) and ~965–972 (its call site inside the
  `xl:grid-cols-2` slot) are the anchor (confirmed accurate during refinement, 2026-09-16); `tabBar`
  ~line 47–50 is where a `tab "Books" BooksTab` entry appends (the function itself starts at line
  35 — the tab-button list is what's meant); `view` dispatch ~line 2906–2907
  (`match model.ActiveTab with`), with a `| BooksTab -> ...` arm appending after the existing
  `GamesTab` arm (~2919–2922).
- ADR-0076 for what "In Focus" and "Finished" mean for books; ADR-0077 (drafted by `books-y9kxy`) for
  `finished_at` being a `yyyy-MM-dd` **date string**, not a timestamp — the 7-day-linger comparison
  (`finished_at >= date('now','-7 days')`) and `RecentlyFinished`'s 90-day window must compare dates,
  not parse a timestamp. ADR-0016 for any popover.
- `intelligence-p4t7k`'s lesson: do not ship a payload no client reads — every new DTO field is
  rendered somewhere in this task.

## Outcome

Built the dashboard's Books presence: the All tab's "Reading" card (replacing the "Books coming soon" placeholder) and a full Books tab with Currently Reading / Recently Finished / Recently Added rails plus a stats block.

**Server** (`src/Shared/Shared.fs`, `src/Server/BookProjection.fs`, `src/Server/Api.fs`):
- `DashboardBookItem` (`Slug`, `Title`, `Authors`, `CoverRef`, `ProgressPercent`, `ProgressSource`, `Finished`, `FinishedOn`) is the one card shape shared by the All tab's Reading card and every Books-tab rail. It (and the relocated `DashboardAllTab`, which now carries `CurrentlyReading: DashboardBookItem list`) had to be declared physically after `ProgressSource`/`ReadingPosition` in `Shared.fs` rather than at `DashboardAllTab`'s original position — F#'s top-to-bottom compile order otherwise forward-references an undeclared type. A comment at the old spot points to the new one; nothing else in the file referenced `DashboardAllTab` by name except the `IMediathecaApi` interface far below, so the move is behaviorally inert.
- `DashboardActivityDay` gained `Reading: int`, merged in `getDashboardAllTab` from `BookProjection.getDailyReadingActivity` (already `COUNT(DISTINCT book_slug)`-correct from `books-y9kxy`) the same way `MovieSessions`/`EpisodesWatched`/`GameSessions` are merged.
- `DashboardBookStats` / `DashboardBooksTab` sit next to `DashboardGamesTab`. `PagesReadThisYear`/`HoursListenedThisYear` are best-effort `option`s: `getReadingStats` joins each finished-this-year book to the exact `book_progress` row that produced its denormalized latest observation (`(book_slug, observed_on, source)`, an exact primary-key match) and sums `Page` positions / `runtime_minutes x percent` respectively, `None` when nothing contributes.
- `DashboardCardQuery` gained `AllCurrentlyReading | BooksCurrentlyReading | BooksRecentlyFinished | BooksRecentlyAdded` (spelled without the `...Query` suffix per the task's own text) and `DashboardCardItems` gained `BookReadingItems of DashboardBookItem list`, wired in `Api.getDashboardCardItems`.
- `BookProjection` gained `toDashboardBookItem`, `getAllTabCurrentlyReading` (In Focus, ordered `progress_observed_on` desc/`added_at` desc, plus the 7-day `finished_at` linger — mirrors `MovieProjection.getAllTabMoviesToWatch`, intelligence-b1nz5), `getRecentlyAddedUnfinished`, and `getReadingStats`. The pre-existing `getCurrentlyReading`/`getRecentlyFinished`/`getDailyReadingActivity` (built by `books-y9kxy` in anticipation of this task) are consumed as-is.
- `getDashboardBooksTab` sits next to the other `getDashboard*Tab` members in both the interface and `Api.create`, not at the file's tail.
- Fixed a pre-existing gap surfaced by the change: `DashboardLingerTests.fs`'s `apiBootstrap`/`createApi` didn't initialize `BookProjection`, so its two `getDashboardAllTab`-driven tests started erroring ("no such table: book_list") once the All tab unconditionally started querying books. Added `BookProjection.handler` to both the bootstrap and the handler list there.
- `tests/Server.Tests/DashboardBooksTests.fs` (new, 5 cases, `TestDb`/`Api.create` round trips): the exact AC #1 scenario (one In Focus book at 40%, one finished 3 days ago, one finished 10 days ago, one Backlog) proving `getDashboardAllTab.CurrentlyReading` has the In Focus book first and only the 3-day-lingering finished book second (both marked correctly), `getDashboardBooksTab.CurrentlyReading` stays strict at one item, `RecentlyFinished` has both finished books (90-day window), `getDashboardCardItems AllCurrentlyReading` parity with the collapsed payload, and `DashboardActivityDay.Reading` counting two same-day observations of one book as one.
- Full Expecto suite: 864/864 passing (up from 786 baseline mentioned in prior art + accumulated work + 5 new cases here + 0 regressions after the `DashboardLingerTests` bootstrap fix).

**Client** (`src/Client/Pages/Dashboard/{Types,State,Views}.fs`, `ExpandCard.test.fs`):
- `DashboardTab` gained `BooksTab` (tab bar, after "Games"); `Model.BooksTabData`; `SwitchTab BooksTab` fetches `getDashboardBooksTab`.
- `DashboardCard` gained `AllReading | BooksReading | BooksFinished | BooksAdded` (deliberately spelled differently from their `DashboardCardQuery` counterparts — `AllReading -> AllCurrentlyReading`, etc. — mirroring the existing convention of `AllGamesInFocus -> GamesInFocusQuery` rather than reusing an identical bare identifier across the two open DU types, which would create F#'s last-declared-wins ambiguity).
- `bookReadingPosterCard` (new, `Views.fs`) is the one renderer for all four Reading cards: a poster tile with the green `finishedBadge "Finished"` (reusing the exact Watched/Retired pill treatment, intelligence-b1nz5) when lingering/finished, otherwise a bottom-edge scrim carrying `DesignSystem.progressContinuous` (the existing thin gold-gradient bar), the percent in `font-mono`, and a small one-letter source glyph (A/G/M).
- All tab: the "Reading" card replaces `booksColumnPlaceholder` in the same `xl:grid-cols-2` slot beside Games, using the identical `PosterGrid` layout/130px poster cap Games already uses there (`c3vqm`), gated on non-empty the same way the Games column already is. Expand/collapse goes through the existing `ExpandCard`/`growingTabArea` FLIP flow, keyed on slug via the existing `cardItemKey`/`Motion.flipKey` machinery — no changes to that machinery were needed.
- Books tab (new `booksTabView`): three poster-scroller rails (Currently Reading / Recently Finished / Recently Added, the same `posterRail`-over-`expandable` shape `gamesTabView` uses) plus `bookStatsRow` (Total / In Focus / Finished this year / Finished all time / Pages read / Hours listened, the last two hidden when `None`).
- `ExpandCard.test.fs` extended with two cases: the four new cards' distinct `DashboardCard.query` mappings (mirroring the file's own `AllMoviesToWatch`/`MoviesToWatch` precedent test), and a reducer-level `ExpandCard AllReading` -> `ExpandedItemsLoaded (AllReading, Ok (BookReadingItems [...]))` round trip proving the same `Card` identity — the property `cardItemKey`'s per-item FLIP key equality across the collapsed/expanded swap depends on — survives end to end for the new card and item shape. (`cardItemKey` itself is `Views.fs`-private and not directly unit-testable from `State.fs`; the "no fade on survivors" FLIP arithmetic itself is generic and already covered by `Motion.test.fs`, unaffected by which `DashboardCard` is in play.)
- `npm run build`: clean (0 errors). `npm run test:client`: 84/84 passing (up from 62 baseline + accumulated work + 2 new here). `npm test`: 864/864 passing.

**Scope note on the "heatmap tooltip" line in the task's `## What`:** investigated and found that `DashboardActivityDay`/`DashboardAllTab.ActivityDays`/`DashboardMonthlyBreakdown` are computed server-side but have **no client consumer anywhere** in the current codebase (confirmed via grep — no "heatmap" text, no `ActivityDays`/`MonthlyBreakdown` reference on the client) — this predates this task and isn't something design-system-fryq7's list-page deletion or any other done task removed either; it appears to be dead payload reserved for a future Journal heatmap feature. Since the acceptance criteria only test `DashboardActivityDay.Reading`'s server-side counting behavior (not a client tooltip), and inventing a brand-new heatmap UI component would be significant undirected scope beyond "gains a field", I added the `Reading` field and its merge (tested, AC-satisfying) but did not build a heatmap tooltip UI, consistent with its three pre-existing sibling fields' treatment. Filed as a backlog item below rather than silently expanding scope.

Key files: `src/Shared/Shared.fs`, `src/Server/BookProjection.fs`, `src/Server/Api.fs`, `tests/Server.Tests/DashboardBooksTests.fs`, `tests/Server.Tests/DashboardLingerTests.fs`, `src/Client/Pages/Dashboard/Types.fs`, `src/Client/Pages/Dashboard/State.fs`, `src/Client/Pages/Dashboard/Views.fs`, `src/Client/Pages/Dashboard/ExpandCard.test.fs`.
