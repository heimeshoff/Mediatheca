# Task: Dashboard "All" Tab Visual Overhaul

**ID:** 015
**Milestone:** --
**Size:** Large
**Created:** 2026-02-20
**Dependencies:** None

## Objective
Redesign the dashboard "All" tab from a stack of uniform lists into a visually engaging, spatially varied layout. TV Series Next Up gets proper poster cards in a horizontal scroller, Games Recently Played gets a stacked bar chart, and the layout uses a 2-column grid on desktop instead of single-column stacking.

## Details

### Desktop Layout (lg+)

```
+----------------------------------------------+-----------------------------+
|  TV Series Next Up                           |  Games Recently Played      |
|  <- [poster] [poster] [poster] [poster] ->   |  [stacked bar chart]        |
|     Severance The Bear  Shogun  Andor        |  last 14 days               |
|     S2E03     S3E01     S1E08   S2E01        |  [game legend]              |
|                                              |-----------------------------+
|                                              |  Games In Focus             |
|                                              |  [compact list / cards]     |
+----------------------------------------------+-----------------------------+
|  Movies In Focus                                                           |
|  [poster cards or enhanced layout]                                         |
+----------------------------------------------------------------------------+
```

- Top row: CSS Grid `lg:grid-cols-3`, left section spans 2 columns (`lg:col-span-2`), right section spans 1 (`lg:col-span-1`).
- Bottom row: full width.
- Mobile: single column, sections stack vertically.

### TV Series Next Up (top-left, 2/3 width)

- **Horizontal scroll** of poster cards (`overflow-x-auto`, snap scrolling).
- Each card: proper poster image (not 40px thumbnail), series name below, episode info ("S2E03: Episode Title") in muted text below that.
- In Focus series get a visual indicator (crosshair badge or glow).
- Finished/Abandoned badges overlaid on poster.
- Friend pills below episode info if `WatchWithFriends` is populated.
- Poster size: roughly 140-160px wide (aspect 2:3), so ~4-5 visible at a time in the 2/3 column.
- Use the existing `PosterCard` shine/shadow effects for visual polish.
- Click navigates to series detail.

### Games Recently Played (top-right, 1/3 width)

**Stacked bar chart** showing the last 14 days of play sessions:
- X-axis: dates (last 14 days, most recent on the right).
- Y-axis: minutes played.
- Each day's bar is split by game with different colors per game.
- Render as pure CSS/HTML (no charting library) -- `div` bars with percentage heights, stacked via flexbox column-reverse or absolute positioning.
- Color legend below the chart mapping colors to game names.
- Clicking a game name in the legend navigates to the game detail page.

**New API endpoint needed:**
- `getDashboardPlaySessions: int -> Async<DashboardPlaySession list>` -- returns all play sessions from the last N days across all games.
- `DashboardPlaySession = { GameSlug: string; GameName: string; CoverRef: string option; Date: string; MinutesPlayed: int }`
- Server query: `SELECT ps.game_slug, gl.name, gl.cover_ref, ps.date, ps.minutes_played FROM game_play_session ps JOIN game_list gl ON gl.slug = ps.game_slug WHERE ps.date >= @from_date ORDER BY ps.date`

### Games In Focus (below bar chart, right column)

- Compact list or small poster cards -- similar to current but in the narrower right column.
- Crosshair icon + game name, clickable to game detail.

### Movies In Focus (bottom row, full width)

- Enhanced from tiny thumbnails -- consider slightly larger poster cards or a horizontal scroll similar to series.
- Crosshair badge, movie name + year.
- Click navigates to movie detail.

### Mobile Layout

- Single column stack: TV Series Next Up (still horizontal scroll) → Games Recently Played (bar chart) → Games In Focus → Movies In Focus.
- Bar chart adapts to full width.

### Glass Card Sections

- Each section wrapped in existing `sectionCard` pattern (glass card with icon + title header).
- Maintain glassmorphism design rules (semi-transparent bg, backdrop-blur, subtle border).

## Acceptance Criteria

- [x] All tab uses a 2-column grid on desktop (2/3 + 1/3 split)
- [x] TV Series Next Up renders as horizontally scrollable poster cards (not list rows)
- [x] Poster cards have shine/shadow effects and show episode info below
- [x] In Focus series are visually indicated (badge or glow)
- [x] Games Recently Played shows a stacked bar chart of the last 14 days
- [x] Bar chart segments are colored by game with a legend
- [x] New API endpoint provides cross-game daily play session data
- [x] Games In Focus appears below the bar chart in the right column
- [x] Movies In Focus spans the full width in the bottom row
- [x] Mobile layout stacks sections vertically, horizontal scroll still works
- [x] Per-media tabs (Movies, Series, Games) remain unchanged
- [x] All sections remain clickable (navigate to detail pages)
- [x] No charting library added -- bar chart is pure CSS/HTML
- [x] Design passes glassmorphism rules (no backdrop-filter nesting issues)

## Work Log

### 2026-02-20

**Changes made:**

1. **Shared types** (`src/Shared/Shared.fs`): Added `DashboardPlaySession` type with `GameSlug`, `GameName`, `CoverRef`, `Date`, `MinutesPlayed`. Added `PlaySessions` field to `DashboardAllTab`.

2. **Server query** (`src/Server/PlaytimeTracker.fs`): Added `getDashboardPlaySessions` function that queries `game_play_session` joined with `game_detail` for the last N days, returning cross-game daily session data.

3. **Server API** (`src/Server/Api.fs`): Updated `getDashboardAllTab` handler to include 14-day play sessions via `PlaytimeTracker.getDashboardPlaySessions conn 14`.

4. **Dashboard Views** (`src/Client/Pages/Dashboard/Views.fs`): Major rewrite of the All tab:
   - **2-column grid layout**: `grid grid-cols-1 lg:grid-cols-3` with left column (`col-span-2`) and right column (`col-span-1`).
   - **TV Series poster scroller**: Horizontal scrollable row of proper poster cards (140-150px wide) with poster shine/shadow effects, In Focus badge (warning-colored crosshair), Finished/Abandoned badges overlaid, episode info below, friend pills.
   - **Stacked bar chart**: Pure CSS/HTML chart showing last 14 days of play sessions. Bars use flexbox `column-reverse` with percentage heights. 8-color palette from the theme (primary, info, secondary, accent, warning, success, error, custom blue). Tooltips on hover showing date and total time. Clickable legend entries navigate to game detail.
   - **Games In Focus**: Compact list below the bar chart in the right column.
   - **Movies In Focus**: Full-width poster scroller in the bottom row with crosshair badges.
   - **Mobile**: Single column stack, horizontal scrollers still work.
   - Per-media tabs (Movies, Series, Games) remain completely unchanged.
   - All glassmorphism rules followed -- no backdrop-filter nesting issues.

**Build:** `npm run build` succeeds. `npm test` passes all 232 tests.
