---
id: intelligence-dnv2y
title: Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap
status: doing
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
