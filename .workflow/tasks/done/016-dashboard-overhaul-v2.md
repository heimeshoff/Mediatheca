# Task: Dashboard "All" Tab Overhaul V2

**ID:** 016
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** 015 (completed)

## Objective

Redesign the Dashboard "All" tab with a hero episode spotlight, streamlined Next Up section, poster-style game cards, a "New Games" section with family ownership, and a live Steam achievements card. This builds on the 2-column layout from task 015.

## Details

### Left Column Changes

#### 1. Hero Episode Spotlight (NEW — top of left column)

Full-width cinematic section showcasing the first series from the Next Up list:

- **Image**: Episode still from TMDB preferred (`/tv/{id}/season/{s}/episode/{e}/images`), falling back to series backdrop if no still exists.
- **Layout**: Full width of the left column, image as background with gradient overlay fading to dark at the bottom.
- **Overlay content**: Series title (Oswald heading), episode label ("S2E03: Episode Title"), and episode overview/description text (Inter body, 2-3 lines max with line-clamp).
- **Click**: Navigates to the series detail page.
- **Design system name**: `hero-spotlight` (for CSS class / style guide reference).

**Data changes required:**
- Add to `DashboardSeriesNextUp`: `BackdropRef: string option`, `EpisodeStillRef: string option`, `EpisodeOverview: string option`
- Server: Fetch episode still URL and overview from TMDB season detail data (already fetched during series import) or from stored episode data if available. Fall back to series `BackdropRef` for the image.

#### 2. Next Up Section (RESTYLE)

Remove the glass card wrapper. Keep the section title "Next Up" with its icon, then render the horizontal poster scroller directly — no card background, no border, no backdrop-filter. Just the title and the scrollable episode list with the existing scroll indicators.

- **Design system name**: `section-open` (a title + content section without card chrome).

#### 3. Movies & Focus (KEEP, same position)

Remains on the left side below Next Up. No changes to content or styling.

### Right Column Changes

#### 4. Recently Played — Summary Stats (ENHANCE)

Below the existing stacked bar chart, add a stats row inside the same card:

- **Total hours**: Sum of all `MinutesPlayed` from `PlaySessions` in the displayed period, formatted as hours (e.g., "42h").
- **Sessions count**: Count of `PlaySessions` entries in the period.
- Display as two stat items side by side: `[icon] 42h played` and `[icon] 18 sessions`.

#### 5. Games & Focus — Poster Cards (RESTYLE)

Switch from compact list items to poster-style cards matching the size used in the Recently Played section's game entries. Show 2-3 cards per row in the right column using a CSS grid or flex wrap.

#### 6. New Games Card (NEW)

New section below Games & Focus on the right side:

- **Title**: "New Games" with an appropriate icon.
- **Content**: Last 10 games added to the library, ordered by date added (most recent first).
- Each entry shows: game cover image (small poster), game name, and family owner badges (friend avatar + name for each `FamilyOwner`).
- Wrapped in the standard `sectionCard` glass card.

**Data changes required:**
- New shared type:
  ```
  type DashboardNewGame = {
      Slug: string
      Name: string
      Year: int
      CoverRef: string option
      AddedDate: string
      FamilyOwners: FriendRef list
  }
  ```
- Add `NewGames: DashboardNewGame list` to `DashboardAllTab`.
- Server: Query games ordered by creation/import date (descending), limit 10, join with family ownership data.

#### 7. Steam Achievements Card (NEW)

New section on the right side (below New Games or wherever fits naturally):

- **Title**: "Recent Achievements" with a trophy icon.
- **Content**: Last 5-10 achievements across all Steam games.
- Each entry: achievement icon, achievement name, game name, unlock date.
- **Error state**: If Steam API is unavailable or returns errors, show an inline message in the card body (e.g., "Could not connect to Steam" or the error hint). Never hide the card — always show it with the error state.
- **Loading state**: Show skeleton/spinner while fetching.
- Wrapped in `sectionCard` glass card.

**Data changes required:**
- New shared types:
  ```
  type SteamAchievement = {
      GameName: string
      GameAppId: int
      AchievementName: string
      AchievementDescription: string
      IconUrl: string option
      UnlockTime: string
  }
  ```
- New API endpoint: `getSteamRecentAchievements: unit -> Async<Result<SteamAchievement list, string>>`
- Server implementation:
  1. Use `IPlayerService/GetRecentlyPlayedGames` to get games played recently (already exists).
  2. For each game, call `ISteamUserStats/GetPlayerAchievements/v1/?appid={id}&key={key}&steamid={steamid}` to get achievements with unlock times.
  3. Optionally call `ISteamUserStats/GetSchemaForGame/v2/?appid={id}&key={key}` to get achievement display names, descriptions, and icon URLs.
  4. Merge, sort by unlock time descending, take the top 10.
  5. This is a live fetch (no persistence) — cache in memory for a short TTL (e.g., 5 minutes) to avoid hammering the API.
- Client: Fetch on dashboard load. Show loading state, then results or error message.

### Mobile Layout

Single column stack order: Hero Spotlight → Next Up → Movies & Focus → Recently Played → Games & Focus → New Games → Recent Achievements.

## Acceptance Criteria

- [x] Hero episode spotlight fills the left column top with cinematic image + gradient overlay
- [x] Episode still preferred, falls back to series backdrop
- [x] Episode overview text displayed in the hero spotlight
- [x] Next Up section has no card background (open section style)
- [x] Movies & Focus remains on left side below Next Up
- [x] Recently Played card shows total hours and session count below the bar chart
- [x] Games & Focus uses poster cards (same size as Recently Played game items)
- [x] New Games card shows last 10 games added with family owner badges
- [x] Steam Achievements card shows last 5-10 achievements across all games
- [x] Achievements card shows error state with message when Steam API fails
- [x] New `DashboardSeriesNextUp` fields: `BackdropRef`, `EpisodeStillRef`, `EpisodeOverview`
- [x] New `DashboardNewGame` DTO with `FamilyOwners` and `AddedDate`
- [x] New `getSteamRecentAchievements` API endpoint (live fetch, short TTL cache)
- [x] Mobile layout stacks all sections vertically
- [x] All glassmorphism rules followed (no backdrop-filter nesting issues)
- [x] Design system names documented: `hero-spotlight`, `section-open`

## Work Log

### 2026-02-24

**All acceptance criteria met.** Build passes (`npm run build`), all 232 tests pass (`npm test`).

**Changes made:**

1. **Shared types** (`src/Shared/Shared.fs`):
   - Added `BackdropRef`, `EpisodeStillRef`, `EpisodeOverview` fields to `DashboardSeriesNextUp`
   - Created `DashboardNewGame` type with `Slug`, `Name`, `Year`, `CoverRef`, `AddedDate`, `FamilyOwners`
   - Created `SteamAchievement` type with `GameName`, `GameAppId`, `AchievementName`, `AchievementDescription`, `IconUrl`, `UnlockTime`
   - Added `NewGames: DashboardNewGame list` to `DashboardAllTab`
   - Added `getSteamRecentAchievements` API endpoint to `IMediathecaApi`

2. **Server** (`src/Server/Steam.fs`):
   - Implemented `getPlayerAchievements` (ISteamUserStats/GetPlayerAchievements/v1)
   - Implemented `getGameSchema` (ISteamUserStats/GetSchemaForGame/v2) for display names/icons
   - Implemented `getRecentAchievements` with 5-minute in-memory TTL cache
   - Added all necessary Steam API response decoders

3. **Server** (`src/Server/SeriesProjection.fs`):
   - Updated `getDashboardSeriesNextUp` SQL to JOIN with `series_detail` (for `backdrop_ref`) and `series_episodes` (for `still_ref`, `overview`)
   - Populates new fields on the DTO

4. **Server** (`src/Server/GameProjection.fs`):
   - Added `getDashboardNewGames` function querying last N games by rowid DESC with family_owners

5. **Server** (`src/Server/Api.fs`):
   - Updated `getDashboardAllTab` to include `NewGames`
   - Added `getSteamRecentAchievements` endpoint implementation

6. **Client** (`src/Client/Pages/Dashboard/Types.fs`):
   - Added `AchievementsState` DU (NotLoaded, Loading, Ready, Error)
   - Added `Achievements` field to `Model`
   - Added `AchievementsLoaded` message

7. **Client** (`src/Client/Pages/Dashboard/State.fs`):
   - Added achievements fetch on All tab load
   - Handles `AchievementsLoaded` message

8. **Client** (`src/Client/Pages/Dashboard/Views.fs`):
   - Added `heroSpotlight` component (cinematic image + gradient overlay + episode info)
   - Added `sectionOpen` wrapper (title + content, no card chrome)
   - Added `seriesNextUpOpenScroller` (open section version of Next Up)
   - Added `gameInFocusPosterCard` and `gamesInFocusPosterSection` (poster grid instead of list)
   - Added `playSessionSummaryStats` (total hours + session count)
   - Added `gamesRecentlyPlayedChartWithStats` (chart + stats)
   - Added `newGameItem` and `newGamesSection` (with family owner badges)
   - Added `achievementItem` and `achievementsSection` (loading/error/ready states)
   - Rewrote `allTabView` with 2-column layout: left (hero, next up, movies) + right (recently played, games in focus, new games, achievements)

9. **CSS** (`src/Client/index.css`):
   - Added `.hero-spotlight` styles (border, shadow, hover effect)
   - Added `.section-open` styles (no background/border/backdrop-filter)

**Files changed:** 8
- `src/Shared/Shared.fs`
- `src/Server/Steam.fs`
- `src/Server/SeriesProjection.fs`
- `src/Server/GameProjection.fs`
- `src/Server/Api.fs`
- `src/Client/Pages/Dashboard/Types.fs`
- `src/Client/Pages/Dashboard/State.fs`
- `src/Client/Pages/Dashboard/Views.fs`
- `src/Client/index.css`
