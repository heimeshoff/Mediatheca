# Task: Jellyfin Play Button

**ID:** 017
**Milestone:** --
**Size:** Medium
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Add a "Play in Jellyfin" button to movies and series throughout the app, linking directly to the Jellyfin web UI for playback. The button opens Jellyfin's details page (`{serverUrl}/web/index.html#!/details?id={jellyfinItemId}`) in a new browser tab. Only items that have been matched to a Jellyfin library item during scan show the button.

## Details

### Backend: Persist Jellyfin Item IDs

Currently the Jellyfin library scan matches items by TMDB ID but does not persist the Jellyfin item ID. We need to store these mappings so the frontend can construct play links.

#### 1. Schema changes

- Add `jellyfin_id TEXT` column to `movie_detail` table
- Add `jellyfin_id TEXT` column to `series_detail` table (for the series itself)
- Create a mapping table or column for episode-level Jellyfin IDs (e.g. `series_episode_jellyfin` table with `series_slug`, `season_number`, `episode_number`, `jellyfin_id`)

#### 2. Scan logic updates (`Api.fs` / `Jellyfin.fs`)

- During Jellyfin library scan, when a movie/series is matched by TMDB ID, persist the Jellyfin item `Id` into the new column
- For series: after matching a series, fetch its episodes from Jellyfin (`/Shows/{seriesId}/Episodes?userId={userId}`) and store episode-level Jellyfin IDs matched by season+episode number

#### 3. API response updates

- Add `JellyfinId: string option` to `MovieDetail` DTO (or a new field in the response)
- Add `JellyfinId: string option` to `DashboardSeriesNextUp` (the episode's Jellyfin ID for next-up)
- Add `JellyfinId: string option` to `DashboardMovieDto` (or equivalent dashboard movie type)
- Add Jellyfin server URL to a shared config endpoint (or include it in responses) so the frontend can construct full URLs
- For series next-up: resolve the Jellyfin episode ID for the next unwatched episode

### Frontend: Play Buttons

#### 4. Dashboard hero (next-up series spotlight)

- Add a play button (play icon) to the hero episode spotlight section
- Button opens: `{serverUrl}/web/index.html#!/details?id={nextEpisodeJellyfinId}` in a new tab
- Only render if the episode has a Jellyfin ID
- Style: prominent play button with glassmorphism styling, positioned over the hero image

#### 5. Dashboard poster cards (series)

- Add a play button overlay on series poster cards in the Next Up horizontal scroller
- Button opens: `{serverUrl}/web/index.html#!/details?id={nextEpisodeJellyfinId}` in a new tab
- Only render if the next episode has a Jellyfin ID
- Style: small play icon overlay on the poster card (bottom-right corner or center on hover), semi-transparent background

#### 6. Movie detail page

- Add a play button to the movie detail hero/poster area
- Button opens: `{serverUrl}/web/index.html#!/details?id={movieJellyfinId}` in a new tab
- Only render if the movie has a Jellyfin ID
- Style: play button near the title or poster, consistent with the app's design system

### Shared

#### 7. Jellyfin URL construction

- Create a shared helper that constructs the Jellyfin play URL from server URL + item ID
- URL pattern: `{serverUrl}/web/index.html#!/details?id={jellyfinItemId}`
- All play buttons use `target="_blank"` to open in a new tab

## Acceptance Criteria

- [x] Jellyfin library scan persists movie Jellyfin IDs into the database
- [x] Jellyfin library scan persists series Jellyfin IDs into the database
- [x] Jellyfin library scan fetches and persists episode-level Jellyfin IDs
- [x] Dashboard hero section shows a play button when the next episode has a Jellyfin ID
- [x] Dashboard series poster cards show a play button when the next episode has a Jellyfin ID
- [x] Movie detail page shows a play button when the movie has a Jellyfin ID
- [x] Play buttons do NOT appear for items without a Jellyfin match
- [x] Clicking a play button opens the correct Jellyfin web UI page in a new tab
- [x] Play buttons follow the app's glassmorphism design system
- [x] Re-scanning the Jellyfin library updates Jellyfin IDs if they change

## Notes

- The Jellyfin web UI URL requires the user to be logged into Jellyfin in their browser separately
- Future enhancement: embedded HLS player using hls.js for inline playback (deferred — adds complexity with codec support, subtitles, auth proxy)
- Jellyfin episode lookup: use `/Shows/{seriesId}/Episodes?userId={userId}&Fields=ProviderIds` and match by `ParentIndexNumber` (season) + `IndexNumber` (episode)

## Work Log

### 2026-02-24

**Backend changes:**
- Added `jellyfin_id TEXT` column to `movie_detail` table (MovieProjection.fs migration)
- Added `jellyfin_id TEXT` column to `series_detail` table (SeriesProjection.fs migration)
- Created `series_episode_jellyfin` table with `(series_slug, season_number, episode_number, jellyfin_id)` primary key
- Updated `scanJellyfinLibrary` in Api.fs to persist Jellyfin IDs for matched movies and series, and fetch+store episode-level Jellyfin IDs via `/Shows/{seriesId}/Episodes`
- Updated `importJellyfinWatchHistory` in Api.fs to also persist Jellyfin IDs (Phase 1b) for both movies and series during import
- Updated `MovieProjection.getBySlug` to read and return `jellyfin_id`
- Updated `MovieProjection.getMoviesInFocus` to JOIN movie_detail for `jellyfin_id`
- Updated `SeriesProjection.getDashboardSeriesNextUp` to JOIN `series_episode_jellyfin` for the next-up episode's Jellyfin ID

**Shared DTO changes (Shared.fs):**
- Added `JellyfinId: string option` to `MovieDetail`
- Added `JellyfinEpisodeId: string option` to `DashboardSeriesNextUp`
- Added `JellyfinId: string option` to `DashboardMovieInFocus`
- Added `JellyfinServerUrl: string option` to `DashboardAllTab`

**Frontend changes:**
- Dashboard hero spotlight: Added glassmorphism play button (top-right) linking to Jellyfin episode page, only when JellyfinEpisodeId is present
- Dashboard series poster cards: Added glassmorphism play button overlay (bottom-right, visible on hover) linking to Jellyfin episode page
- Dashboard movie in-focus poster cards: Added glassmorphism play button overlay (bottom-right, visible on hover) linking to Jellyfin movie page
- Movie detail page: Added "Play in Jellyfin" button in the hero action buttons row, loads JellyfinServerUrl via API
- All play buttons use `target="_blank"` to open in a new tab
- Play buttons only render when both the Jellyfin server URL and the item's Jellyfin ID are available

**Build & Tests:** `npm run build` succeeds, all 232 Expecto tests pass.
