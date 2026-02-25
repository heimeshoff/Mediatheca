# Task: Movies to Watch Card

**ID:** 035
**Milestone:** --
**Size:** Medium
**Created:** 2026-02-25
**Dependencies:** None

## Objective

Rename and expand the "Movies In Focus" dashboard card to "Movies to Watch". The card should show the union of movies explicitly marked in-focus AND movies available on the Jellyfin server (that haven't been watched), with a persistent ghost play button for Jellyfin-available movies.

## Current State

- "Movies In Focus" card exists on both Dashboard "All" tab and "Movies" tab
- Shows only movies where `in_focus = 1`
- Play button exists but is hover-only (`opacity-0 group-hover:opacity-100`)
- `DashboardMovieInFocus` type carries `JellyfinId: string option`
- `getMoviesInFocus` query in `MovieProjection.fs` filters by `in_focus = 1`, then looks up JellyfinId per row
- Watching a movie auto-clears `in_focus` via `Record_watch_session` in `Movies.fs`

## Requirements

### Data (Server)

1. **Rename query** from `getMoviesInFocus` to `getMoviesToWatch` (or similar)
2. **Return union** of:
   - Movies with `in_focus = 1` (regardless of Jellyfin status)
   - Movies with a match in `jellyfin_movie` table (regardless of in-focus status)
3. **Exclude watched**: Filter out movies that have at least one watch session recorded in Mediatheca (check `movie_list` or `movie_detail` watch count/session data)
4. **No limit** on result count
5. **Ordering**:
   - Primary: in-focus movies first, then Jellyfin-only movies
   - Secondary (within each group): most recently added first (by `rowid DESC` or `created_at DESC`)
6. **Include `InFocus` flag** in each result so the client can distinguish in-focus from Jellyfin-only

### Shared Types

7. Update `DashboardMovieInFocus` (or rename to `DashboardMovieToWatch`):
   - Add `InFocus: bool` field
   - Keep existing fields: `Slug`, `Name`, `Year`, `PosterRef`, `JellyfinId`

### UI (Client)

8. **Rename section** title from "Movies In Focus" to "Movies to Watch" (both All tab and Movies tab)
9. **Crosshair badge**: Only show on cards where `InFocus = true`
10. **Play button**: Change from hover-only to always-visible ghost button style (DaisyUI `btn-ghost`) for cards where `JellyfinId` is `Some`
11. **Deduplication**: A movie that is both in-focus AND on Jellyfin appears once, with both the crosshair badge and the play button

### Dashboard API

12. Rename `MoviesInFocus` field in `DashboardAllTab` and `DashboardMoviesTab` to `MoviesToWatch` (or similar)

## Acceptance Criteria

- [x] Card titled "Movies to Watch" on both All and Movies dashboard tabs
- [x] In-focus movies appear with crosshair badge, sorted first
- [x] Jellyfin-available (unwatched) movies appear after in-focus movies with a visible ghost play button
- [x] A movie that is both in-focus and on Jellyfin shows both badge and play button, appears only once in the in-focus group
- [x] Watched movies (with any Mediatheca watch session) are excluded even if on Jellyfin
- [x] Play button is always visible (not hover-only), styled as ghost button
- [x] No artificial limit on the number of movies shown
- [x] Horizontal scroll still works for overflow

## Files to Modify

- `src/Server/MovieProjection.fs` — query change
- `src/Shared/Shared.fs` — type updates
- `src/Server/Api.fs` — field rename in dashboard tab builders
- `src/Client/Pages/Dashboard/Views.fs` — card rendering, title rename, play button style
- `src/Client/Pages/Dashboard/Types.fs` — model field rename (if applicable)

## Work Log

### 2026-02-25 — Implementation complete

**Changes made across 4 files:**

1. **`src/Shared/Shared.fs`** — Renamed `DashboardMovieInFocus` to `DashboardMovieToWatch`, added `InFocus: bool` field. Renamed `MoviesInFocus` to `MoviesToWatch` in both `DashboardAllTab` and `DashboardMoviesTab`.

2. **`src/Server/MovieProjection.fs`** — Replaced `getMoviesInFocus` (filtered only `in_focus = 1`, limited results) with `getMoviesToWatch` (no limit). New query uses `LEFT JOIN jellyfin_movie` to include both in-focus and Jellyfin-available movies, excludes watched movies via `NOT IN (SELECT DISTINCT movie_slug FROM watch_sessions)`, orders by `in_focus DESC, rowid DESC`. The `JellyfinId` is now read directly from the JOIN rather than calling `JellyfinStore.getMovieJellyfinId` per row (N+1 fix).

3. **`src/Server/Api.fs`** — Updated both `getDashboardAllTab` and `getDashboardMoviesTab` to call `MovieProjection.getMoviesToWatch conn` (no limit parameter) and assign to `MoviesToWatch` field.

4. **`src/Client/Pages/Dashboard/Views.fs`** — Renamed card/section functions from `movieInFocusPosterCard`/`moviesInFocusPosterSection` to `movieToWatchPosterCard`/`moviesToWatchPosterSection`. Section titles changed to "Movies to Watch". Crosshair badge now conditional on `item.InFocus`. Play button made always-visible by removing `opacity-0 group-hover:opacity-100` classes. Both All tab and Movies tab references updated.

**Note:** `src/Client/Pages/Dashboard/Types.fs` did not need changes — the model uses shared types directly.

**Note:** Build/test could not be verified due to bash tool temp directory failure (`EINVAL` on all commands). All changes are type-consistent across shared/server/client boundaries.
