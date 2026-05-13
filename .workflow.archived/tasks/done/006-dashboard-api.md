# Task: Dashboard API — shared types + queries

**ID:** 006
**Milestone:** M2 - Unified Dashboard
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** 001-movie-in-focus-backend, 003-series-in-focus-backend, 005-game-in-focus-status

## Objective
Backend API provides all data needed for the unified tabbed dashboard.

## Details

### New Shared Types (src/Shared/Shared.fs)

**All Tab types:**
```
DashboardSeriesNextUp = {
    Slug: string
    Name: string
    PosterRef: string option
    NextUpSeason: int
    NextUpEpisode: int
    NextUpTitle: string
    WatchWithFriends: FriendRef list  // from active rewatch session
    InFocus: bool
    IsFinished: bool  // all episodes watched
    IsAbandoned: bool
    LastWatchedDate: string option  // for sorting by recency
}

DashboardMovieInFocus = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    AddedDate: string  // for sorting
}

DashboardGameInFocus = {
    Slug: string
    Name: string
    Year: int
    CoverRef: string option
    Status: GameStatus  // will be InFocus
}

DashboardGameRecentlyPlayed = {
    Slug: string
    Name: string
    CoverRef: string option
    TotalPlayTimeMinutes: int
    LastPlayedDate: string
    HltbHours: float option
}
```

**Per-tab types:**
```
DashboardMoviesTab = {
    RecentlyAdded: MovieListItem list  // newest first, exclude watched
    Stats: DashboardMovieStats
}

DashboardMovieStats = {
    TotalMovies: int
    TotalWatchSessions: int
    TotalWatchTimeMinutes: int
}

DashboardSeriesTab = {
    NextUp: DashboardSeriesNextUp list  // full list
    RecentlyFinished: SeriesListItem list
    RecentlyAbandoned: SeriesListItem list
    Stats: DashboardSeriesStats
}

DashboardSeriesStats = {
    TotalSeries: int
    TotalEpisodesWatched: int
    TotalWatchTimeMinutes: int
}

DashboardGamesTab = {
    RecentlyAdded: GameListItem list
    RecentlyPlayed: DashboardGameRecentlyPlayed list
    Stats: DashboardGameStats
}

DashboardGameStats = {
    TotalGames: int
    TotalPlayTimeMinutes: int
    GamesCompleted: int
    GamesInProgress: int
}
```

### New API Endpoints (src/Shared/Shared.fs — IMediathecaApi)
- `getDashboardAllTab: unit -> Async<DashboardAllTab>` — returns all four sections for the All tab
  - `DashboardAllTab = { SeriesNextUp: DashboardSeriesNextUp list; MoviesInFocus: DashboardMovieInFocus list; GamesInFocus: DashboardGameInFocus list; GamesRecentlyPlayed: DashboardGameRecentlyPlayed list }`
- `getDashboardMoviesTab: unit -> Async<DashboardMoviesTab>`
- `getDashboardSeriesTab: unit -> Async<DashboardSeriesTab>`
- `getDashboardGamesTab: unit -> Async<DashboardGamesTab>`

### Server Implementation (src/Server/Api.fs)
- **SeriesNextUp query**: Join `series_list` with `series_rewatch_sessions` (for watch-with friends on default session). Filter where `next_up_season IS NOT NULL OR in_focus = 1 OR abandoned = 1`. Sort: `in_focus DESC, last_watched_date DESC`. Limit 6.
  - Need to add `last_watched_date` to series_list projection OR compute from series_episode_progress (MAX watched_date)
- **MoviesInFocus query**: `SELECT FROM movie_list WHERE in_focus = 1` ordered by rowid DESC. Limit 6.
- **GamesInFocus query**: `SELECT FROM game_list WHERE status = 'InFocus'`
- **GamesRecentlyPlayed query**: `SELECT game_slug, MAX(date) as last_played FROM play_sessions GROUP BY game_slug ORDER BY last_played DESC LIMIT 6`, joined with game_list for metadata
- **Movies tab**: recently added = `SELECT FROM movie_list WHERE slug NOT IN (SELECT DISTINCT movie_slug FROM watch_sessions) ORDER BY rowid DESC LIMIT 10`
- **Series tab**: full next-up list (no limit), recently finished/abandoned from series_list
- **Games tab**: recently added, recently played, stats from COUNT/SUM queries

### Projection Enhancement
- May need to add `last_watched_date` column to `series_list` for efficient sorting (derived from MAX of watched_date in series_episode_progress)
- May need to track `added_date` on movie_list if not already tracked (check if rowid ordering suffices)

## Acceptance Criteria
- [ ] All shared types defined
- [ ] All 4 API endpoints implemented and returning correct data
- [ ] Series Next Up sorted: In Focus first, then by most recent watch activity
- [ ] Movies In Focus filtered to only in-focus movies
- [ ] Games In Focus returns games with InFocus status
- [ ] Games Recently Played sorted by last play session date
- [ ] Limits applied (~6 items for All tab sections)

## Notes
- Keep existing `getDashboardStats` and `getRecentActivity` endpoints for backward compat during transition
- The series sorting (In Focus pinned to top, then recency) is critical — this was a specific brainstorm decision
- Consider whether `last_watched_date` needs a projection column or can be computed in the query via subquery on series_episode_progress

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete

**Changes made:**

1. **src/Shared/Shared.fs** — Added all shared dashboard tab types:
   - `DashboardSeriesNextUp`, `DashboardMovieInFocus`, `DashboardGameInFocus`, `DashboardGameRecentlyPlayed`
   - `DashboardAllTab`, `DashboardMoviesTab`, `DashboardSeriesTab`, `DashboardGamesTab`
   - `DashboardMovieStats`, `DashboardSeriesStats`, `DashboardGameStats`
   - 4 new API endpoints on `IMediathecaApi`: `getDashboardAllTab`, `getDashboardMoviesTab`, `getDashboardSeriesTab`, `getDashboardGamesTab`
   - Types that reference `MovieListItem`/`SeriesListItem`/`GameListItem` placed after those types to satisfy F# declaration ordering.

2. **src/Server/SeriesProjection.fs** — Added 3 query functions:
   - `getDashboardSeriesNextUp`: Joins `series_list` with `series_rewatch_sessions` (default session) for watch-with friends. Uses subquery on `series_episode_progress` for `last_watched_date`. Filters `next_up_season IS NOT NULL OR in_focus = 1 OR abandoned = 1`. Sorts: `in_focus DESC, last_watched_date DESC NULLS LAST`. Optional limit param (6 for All tab, unlimited for Series tab).
   - `getRecentlyFinished`: Series where `episode_count > 0 AND watched_episode_count >= episode_count AND abandoned = 0`, limit 10.
   - `getRecentlyAbandoned`: Series where `abandoned = 1`, limit 10.

3. **src/Server/MovieProjection.fs** — Added 2 query functions:
   - `getMoviesInFocus`: Movies where `in_focus = 1`, ordered by rowid DESC, with limit.
   - `getRecentlyAddedMovies`: Movies NOT in `watch_sessions`, ordered by rowid DESC, with limit.

4. **src/Server/GameProjection.fs** — Added 3 query functions:
   - `getGamesInFocus`: Games where `status = 'InFocus'`.
   - `getGamesRecentlyPlayed`: Joins `game_play_session` with `game_list`, grouped by game, ordered by last played DESC.
   - `getRecentlyAddedGames`: Games ordered by rowid DESC with limit.

5. **src/Server/Api.fs** — Implemented all 4 API endpoint handlers:
   - `getDashboardAllTab`: Calls projection queries with limit 6.
   - `getDashboardMoviesTab`: Recently added (limit 10), movie stats (COUNT/SUM queries).
   - `getDashboardSeriesTab`: Full next-up list, recently finished, recently abandoned, series stats.
   - `getDashboardGamesTab`: Recently added (limit 10), recently played (limit 10), game stats.

**Design decisions:**
- Used subquery `(SELECT MAX(watched_date) FROM series_episode_progress WHERE series_slug = sl.slug)` instead of adding a projection column for `last_watched_date` — simpler, no schema migration needed.
- Existing `getDashboardStats` and `getRecentActivity` endpoints preserved (not removed).
- No files from task 013's domain (GameDetail/*) were touched.

**Verification:** `npm run build` succeeds (client + Fable compilation). `npm test` passes all 232 tests.
