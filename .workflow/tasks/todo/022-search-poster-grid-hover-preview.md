# Task: Search Poster Grid with Hover Preview

**ID:** 022
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Redesign the search modal (Ctrl+K) results from text-based lists to a **4-column poster grid** with **hover preview popovers**. Hovering a poster for 500ms shows a glassmorphic popover with rich details fetched on demand. Nothing is written to the database until the user clicks a poster to import.

## Details

### 1. Poster Grid Layout (`src/Client/Components/SearchModal.fs`)

Replace the current `space-y-1` list layout with a CSS grid in all four tabs:

- **Grid:** `grid grid-cols-4 gap-3` inside the scrollable content area
- **Poster card:** Each result renders as a poster thumbnail with:
  - Poster/cover image filling the card (`aspect-[2/3]`, `object-cover`, `rounded-lg`)
  - Name overlaid at the bottom in a subtle gradient (`bg-gradient-to-t from-black/70`) — truncated to 2 lines
  - Fallback placeholder icon (movie/tv/gamepad) when no image is available
  - On the Library tab, a small media-type badge (Movie/Series/Game) in the corner
- **Modal width:** Increase from `max-w-2xl` to `max-w-4xl` to accommodate the grid comfortably
- **Keyboard navigation:** Adapt ↑↓ to navigate rows (4 items per row), add ←→ for column navigation. Selected poster gets `ring-2 ring-primary` highlight.

#### Image Sources per Tab

| Tab | Image field | URL pattern |
|-----|------------|-------------|
| Library (movie/series) | `PosterRef` | `/images/{posterRef}` |
| Library (game) | `CoverRef` | `/images/{coverRef}` |
| Movies/Series | `PosterPath` | `https://image.tmdb.org/t/p/w185{posterPath}` (upgrade from w92) |
| Games | `BackgroundImage` | Direct RAWG URL (already full-size) |

### 2. Hover Preview Popover — Shared Component

Create a reusable `HoverPreview` React component:

- **Trigger:** `onMouseEnter` starts a 500ms timer; `onMouseLeave` cancels it
- **Position:** Appears to the right of the hovered poster (or left if near the right edge). Positioned absolutely relative to the grid container.
- **Style:** Glassmorphic panel (following the project's glassmorphism convention — semi-transparent bg, `backdrop-filter: blur(24px) saturate(1.2)`, subtle border, inset highlight). Render as a **sibling** to the grid, not a child, to avoid the nested `backdrop-filter` issue.
- **Size:** Fixed width (~320px), content-driven height, max-height with overflow scroll
- **Animation:** Fade in (`opacity 0→1`, 150ms ease-out)
- **Dismissal:** Disappears on mouse leave from both poster and popover (with a small grace period for moving between them)

### 3. Library Hover Preview — Local DB Fetch

When hovering a library item, fetch full detail from the local database:

**New Msg variants:**
- `Hover_library_item of slug: string * MediaType` — triggered after 500ms hover delay
- `Hover_preview_loaded of HoverPreviewData` — data returned from API
- `Hover_preview_failed`
- `Hover_clear` — mouse left, clear preview

**New shared type** (`src/Shared/Shared.fs`):
```fsharp
type SearchPreviewData = {
    Title: string
    Year: int
    Overview: string
    Genres: string list
    PosterRef: string option
    Cast: CastMemberDto list       // top 5 for movies/series
    Rating: float option
    Runtime: int option            // movies
    SeasonCount: int option        // series
    PlayTime: int option           // games (minutes)
    HltbHours: float option        // games
}
```

**API endpoint:** Reuse existing `getMovieDetail` / `getSeriesDetail` / `getGameDetail` endpoints (they already exist and return all the data needed). The client just picks the fields it needs for the preview.

**Preview content for library items:**
- **Movie:** Title, year, overview (truncated ~3 lines), top 3-5 cast names, runtime, genres
- **Series:** Title, year, overview, season count, episode progress, genres
- **Game:** Title, year, description (truncated), genres, play time, HLTB hours

### 4. External Hover Preview — TMDB Fetch (Movies/Series Tab)

When hovering a TMDB result, fetch details from the TMDB API:

**New API endpoint** (`src/Shared/Shared.fs`, `src/Server/Api.fs`):
```fsharp
previewTmdbMovie: int -> Async<TmdbPreviewData option>
previewTmdbSeries: int -> Async<TmdbPreviewData option>
```

**New shared type:**
```fsharp
type TmdbPreviewData = {
    Title: string
    Year: int option
    Overview: string
    Genres: string list
    PosterPath: string option
    BackdropPath: string option
    Cast: string list              // top 5 names
    Runtime: int option            // movies
    SeasonCount: int option        // series
    Rating: float option
}
```

**Server implementation** (`src/Server/Tmdb.fs`):
- `GET /3/movie/{id}?append_to_response=credits` — fetches movie details + cast in one call
- `GET /3/tv/{id}?append_to_response=credits` — fetches series details + cast
- Cache results (same in-memory cache pattern as search, 1hr TTL)
- **Read-only:** No events emitted, nothing written to DB

**Preview content:**
- Title, year, overview (truncated), top 5 cast names, genres, rating, runtime/seasons

### 5. External Hover Preview — RAWG Fetch (Games Tab)

When hovering a RAWG result, fetch details from RAWG:

**New API endpoint:**
```fsharp
previewRawgGame: int -> Async<RawgPreviewData option>
```

**New shared type:**
```fsharp
type RawgPreviewData = {
    Name: string
    Year: int option
    Description: string           // HTML stripped to plain text
    Genres: string list
    BackgroundImage: string option
    Screenshots: string list       // first 2-3 screenshot URLs
    Rating: float option
    Metacritic: int option
    Platforms: string list
}
```

**Server implementation** (`src/Server/Rawg.fs`):
- `GET /api/games/{id}` — fetches game details
- `GET /api/games/{id}/screenshots` — fetches screenshots (optional, could be a second call or combined)
- Cache results (1hr TTL)
- **Read-only:** No events emitted, nothing written to DB

**Preview content:**
- Name, year, description (truncated), genres, rating, metacritic, platforms, 1-2 screenshot thumbnails

### 6. State Management Updates (`src/Client/Components/SearchModal.fs`, `src/Client/State.fs`)

**New model fields:**
```fsharp
HoverTarget: (string * MediaType) option    // slug or "tmdb:{id}" / "rawg:{id}"
HoverPreview: HoverPreviewState             // NotHovering | Loading | Loaded of data | Failed
```

**Debounce pattern:** Same 500ms timer approach as the search debounce — track a hover version to cancel stale hovers.

**Cache previews in memory:** Once a preview is loaded, store it in a `Map<string, PreviewData>` so re-hovering the same item is instant.

### 7. Keyboard Navigation Updates

Current: ↑↓ moves through a flat list.
New: Grid navigation with 4 columns.

- **←→**: Move between columns in the same row
- **↑↓**: Move between rows (jump 4 items)
- **Enter**: Still imports/navigates to selected item
- **Tab/Shift+Tab**: Still switches between tabs
- Show preview for keyboard-selected item (same as hover)

## Acceptance Criteria

- [ ] All four search tabs display results as a 4-column poster grid
- [ ] Posters show the correct image (local path for library, TMDB URL for movies/series, RAWG URL for games)
- [ ] Hovering a poster for 500ms shows a glassmorphic preview popover
- [ ] Library hover fetches full detail from local DB and shows name, year, overview, cast/description, genres
- [ ] Movies/Series tab hover fetches from TMDB API and shows title, overview, cast, rating
- [ ] Games tab hover fetches from RAWG API and shows name, description, screenshots, rating
- [ ] Hover previews are cached in memory (re-hovering is instant)
- [ ] No data is written to the database on hover — imports only happen on click
- [ ] Preview popover uses glassmorphism (semi-transparent bg, backdrop-blur, border)
- [ ] Preview popover renders as a sibling to the grid (not nested inside a backdrop-filter parent)
- [ ] Keyboard navigation works with grid layout (←→↑↓)
- [ ] Keyboard-selected items also show the preview popover
- [ ] Fallback placeholder shown for items without poster images
- [ ] No performance issues with rapid hovering (debounce prevents excessive API calls)
- [ ] All existing tests pass
