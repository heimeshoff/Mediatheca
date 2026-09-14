---
id: intelligence-b1nz5
title: All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode"
status: todo
type: feature
context: intelligence
created: 2026-09-14
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, all-tab, movies-to-watch, games-in-focus, linger, finished, retired]
related_adrs: [0073, 0015, 0048]
related_research: []
prior_art: [intelligence-p9m4t, intelligence-h7v2q, intelligence-qh8mj]
---

## Why

Builder, 2026-09-14: *"TV series that have been finished will still be shown for a while on
the main dashboard. I want the same thing to happen for a movie that I finished or for a game
that I retired."*

Today the three All-tab rails behave differently:

- **Series:** `SeriesProjection.getDashboardSeriesNextUp` keeps a fully watched series on the
  "Next episode" rail for **7 days** after its latest watched episode (`MAX(watched_date) >=
  date('now', '-7 days')`), and the card turns green (`IsFinished` → `IsComplete`).
- **Movies:** `MovieProjection.getMoviesToWatch` drops a movie the moment any watch session
  exists (`slug NOT IN (SELECT movie_slug FROM watch_sessions)`), so it vanishes as soon as
  you log it.
- **Games:** `GameProjection.getGamesInFocus` is `status = 'InFocus'` only, so a game drops
  off the moment it's marked Retired. Nothing records *when* a game was retired.

That last moment of "I just finished this" is worth seeing on the dashboard for a few days,
consistently across all three media types.

## What

One rule across the All tab: **a finished item stays on its rail for 7 days after it was
finished, and is visibly marked as finished.**

### Movies — "Movies to Watch" (All tab)

- The rail shows what it shows today (unwatched and (In Focus or on Jellyfin)), **plus any
  movie whose latest watch session `date` is within the last 7 days**. Builder's choice: this
  applies to *any* watched movie, even one that was never on the rail (not In Focus, not on
  Jellyfin).
- `DashboardMovieToWatch` gains what the view needs to tell the two kinds apart (e.g.
  `WatchedDate: string option` or `IsFinished: bool`, whichever reads best in `Shared.fs`).
- The expanded card (`getDashboardCardItems MoviesToWatchQuery`) follows the same rule, so
  collapsed and expanded show the same set (ADR-0073 FLIP keys stay stable).
- **Scope:** All tab only. The Movies tab's own "Movies to Watch" card
  (`Views.fs` `Card = MoviesToWatch`) uses the same query today. It should keep its strict
  unwatched-only list, because that tab already has "Recently Watched". Split the query
  (e.g. an `includeRecentlyFinished` flag or a separate function) rather than changing the
  Movies tab by accident.

### Games — "Games" rail (All tab, `GamesInFocus`)

- Record **when a game was retired**: a new `retired_at` column on `game_list`, written by
  `GameProjection`'s `Game_status_changed Retired` handler from the event's own
  `StoredEvent.Timestamp` (event-derived, therefore replayable, therefore a projection
  column. Same reasoning as `prior_play_time`, games-p6vkz). Any other status change clears it
  to NULL. Add it with the same idempotent `try ALTER TABLE … with _ -> ()` idiom
  `createTables` already uses.
- The rail shows `status = 'InFocus'` games **plus `status = 'Retired'` games whose
  `retired_at` is within the last 7 days**.
- `DashboardGameInFocus` gains a retired flag/date for the view.
- The expanded card (`GamesInFocusQuery`) follows the same rule.
- Existing already-retired games get `retired_at = NULL` from the ALTER and so don't linger.
  That's correct: they were retired long ago. A projection rebuild would fill it in from
  history.

### Ordering

Match the series rail's feel: the most recently finished/retired items sort **first** (most
recent first), followed by the rail's existing items in their existing order. (This is a
default chosen during capture; the builder didn't state an order.)

### Look

A lingering movie or game carries a clear "finished" / "retired" mark (success-green, the same
voice the finished series card uses), so it doesn't read as still to do. Use existing
design-system tokens and badge patterns (the StyleGuide page is canonical, ADR-0015). No new
design-system primitive unless the specimen genuinely lacks one. For a watched movie, the
Jellyfin play button and the In Focus badge may drop; use your judgement against the
styleguide.

## Acceptance criteria

- [ ] A movie with a watch session dated within the last 7 days appears in the All-tab
      `DashboardAllTab.MoviesToWatch` payload, whether or not it is In Focus or on Jellyfin;
      one whose latest watch session is 8+ days old does not (Expecto, in-memory SQLite).
- [ ] Unwatched movies that qualify today (In Focus or Jellyfin-linked) still appear, unchanged.
- [ ] The Movies tab's "Movies to Watch" payload still excludes every watched movie.
- [ ] Projecting `Game_status_changed Retired` sets `game_list.retired_at` to that event's
      timestamp; a later `Game_status_changed` to any other status clears it (Expecto).
- [ ] A game retired within the last 7 days appears in `DashboardAllTab.GamesInFocus`; one
      retired 8+ days ago, or with `retired_at` NULL, does not; InFocus games still appear.
- [ ] `getDashboardCardItems` for `MoviesToWatchQuery` (All-tab card) and `GamesInFocusQuery`
      returns the same lingering items as the collapsed payloads.
- [ ] Lingering items sort ahead of the rail's other items, most recently finished first.
- [ ] Lingering items expose a finished/retired flag in the shared DTOs, and the All-tab view
      renders a distinct finished/retired mark for exactly those items (inspectable in the
      rendered markup, e.g. a badge element only present when the flag is set).
- [ ] The finished/retired mark reads clearly as "done" and sits naturally with the finished
      series card's green treatment. [human-eye]
- [ ] `npm run build`, `npm test`, `npm run test:client` all pass.

## Notes

- Series reference implementation: `src/Server/SeriesProjection.fs`
  `getDashboardSeriesNextUp` (the 7-day `WHERE` clause and the `isFinished` flag) and
  `src/Client/Pages/Dashboard/Views.fs` `seriesProgressOf` (`IsComplete` → green).
- Movie rail: `MovieProjection.getMoviesToWatch`; view `movieToWatchFilmstripItem` /
  `movieToWatchPosterCard`; card wiring around `Views.fs` `Card = AllMoviesToWatch`.
- Game rail: `GameProjection.getGamesInFocus`; view `gameInFocusPosterCard`; card wiring
  `Card = AllGamesInFocus`; status projection in `GameProjection.handleEvent`'s
  `Games.Game_status_changed` branch (the handler receives the `StoredEvent`, which carries
  `Timestamp`).
- Server API: `Api.fs` `getDashboardAllTab` and the `getDashboardCardItems` match
  (`MoviesToWatchQuery` / `GamesInFocusQuery`). Check whether the Movies tab and All tab
  currently share `MoviesToWatchQuery` (`Types.fs`: `AllMoviesToWatch | MoviesToWatch ->
  MoviesToWatchQuery`). If so, the All-tab card needs its own query case to keep the Movies
  tab strict.
- The 7-day window is shared with series. If it's worth a named constant, a small shared
  helper is fine, but don't refactor the series query's behaviour.
- **Never touch the live database.** Fixtures and in-memory SQLite only. The `retired_at`
  column arrives via the idempotent ALTER on next server start; any backfill/rebuild on the
  live DB is a builder action, not the worker's.
- Styleguide gate: `depends_on: [design-system-001]` per the intelligence README's frontend
  gate.
