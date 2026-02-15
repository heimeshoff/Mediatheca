# Current State

**Last Updated:** 2026-02-15
**Current Phase:** 7A (Game Catalog + Play Sessions) — Implementation complete
**Current Task:** All Phase 7A requirements implemented

## Recent Progress

- **2026-02-15**: Search modal tabbed interface with unified keyboard navigation
  - External results (Movies, Series, Games) now displayed as tabs — only one visible at a time
  - Tab key cycles between tabs (Shift+Tab reverses), replacing left/right arrow column switching
  - Unified focus: library results and tab results never simultaneously selected
  - Down arrow from end of library enters active tab; Up arrow from top of tab returns to library
  - Games tab now fully keyboard-navigable (was mouse-only before)
  - Extracted renderRawgItem helper for consistent game result rendering
  - Modal narrowed to max-w-2xl (single column instead of two-column layout)
  - Updated keyboard hints footer: "tab switch" replaces "←→ switch"
- **2026-02-15**: Series overview card status line redesign
  - Episode count and status now on one line: progress left, status right
  - Abandoned series show "Abandoned" in red (text-error)
  - Fully watched series show "Finished" in green (text-success)
  - Otherwise shows next episode (S{x}E{y}) in primary color
  - Added `abandoned` column to series_list projection table
  - Added `IsAbandoned` to SeriesListItem shared type
- **2026-02-15**: Search modal & movies overview UX improvements
  - Removed genre filter buttons from Movies overview (will be replaced with proper filter system later)
  - Search modal input: no border on focus, background color change instead
  - Keyboard navigation now works for library entries (was TMDB-only)
  - Enter on library entry navigates to it; Enter on TMDB result imports it
  - Arrow keys flow: library section → TMDB columns (seamless up/down transition)
  - Selection highlighting and scroll-into-view for library items
  - Updated keyboard hints footer ("select" instead of "import")
- **2026-02-15**: Add abandon/unabandon series feature
  - New events: Series_abandoned, Series_unabandoned with full serialization
  - New commands: Abandon_series, Unabandon_series with idempotent decide logic
  - Abandoned bool field on ActiveSeries state, IsAbandoned on SeriesDetail DTO
  - Projection: abandoned column on series_detail table
  - API: abandonSeries/unabandonSeries endpoints
  - UI: toggle button above Remove Series (warning style), text changes based on state
- **2026-02-14**: Search modal two-column layout with keyboard navigation
  - TMDB results split into Movies (left) and Series (right) columns with divider
  - Full keyboard nav: ↑↓ navigate rows, ←→ switch columns, Enter imports, Esc closes
  - Selection highlighting with ring + background, auto-scroll into view
  - ReactComponent with local useState for selection (no Elmish roundtrip)
  - Left/Right arrow only captured when selection is active (preserves cursor movement)
  - Selection resets on new query, keyboard hints shown in modal footer
  - Widened modal to max-w-4xl for two-column layout
- **2026-02-14**: Unified search modal (Ctrl+K) for movies and TV series
  - Added MediaType to TmdbSearchResult shared type
  - Search modal now queries both TMDB movie and TV search APIs in parallel
  - Import routes to addMovie or addSeries based on result type
  - Library search shows both movies and series with type badges
  - Navigation after import/selection routes to correct detail page
  - Wired up Series page Open_tmdb_search (was TODO)
  - Fixed missing SeriesProjection.handler registration in Program.fs
- **2026-02-14**: Phase 6 (TV Series) implementation complete
  - Series.fs: Full aggregate with 18 event types, named rewatch sessions, batch episode operations
  - SeriesSerialization: Complete Thoth.Json.Net serialization for all events including nested season/episode data
  - Tmdb.fs: TV series search, details, season/episode import, credits, image downloads
  - SeriesProjection.fs: 6 projection tables (series_list, series_detail, series_seasons, series_episodes, series_rewatch_sessions, series_episode_progress)
  - CastStore.fs: series_cast junction table with save/get/cleanup
  - Api.fs: 25+ new API endpoints for series CRUD, social features, rewatch sessions, episode progress
  - Shared.fs: All Series DTOs, SeriesStatus, request types, IMediathecaApi extensions
  - Client: Series list page with poster grid and search, Series detail page with hero, tabs, season sidebar, episode list with watch toggles
  - Router: Series_list and Series_detail routes, navigation (sidebar + bottom nav)
  - Tests: 21 new tests (domain + serialization), all passing
  - 139 tests total, 137 passing, 2 errored (pre-existing integration test issue)
- **2026-02-14**: Created Phase 6 — TV Series (REQ-100, REQ-101, REQ-102, REQ-102a, REQ-102b)
  - Moved REQ-100–102 from v2 into v1 as new Phase 6
  - Added REQ-102a (series list page) and REQ-102b (series dashboard) to break out UI requirements
  - Inspiration prototype: `inspiration/tv_series/tv_series_episode_list_2/`
- **2026-02-14**: Added Entry List component to Style Guide
  - Notion-style database view with switchable Gallery/List layouts
  - Gallery: responsive poster grid using PosterCard.view with stagger animation
  - List: detailed rows with thumbnail, title, year, genres, and rating badge
  - Segmented layout toggle control (icon + label) with local React state
  - New viewGrid and viewList icons added to Icons module
  - Live interactive demo with 8 mock movie entries
  - Code examples and design decision callouts
- **2026-02-14**: Added catalog picker to movie detail page
  - Glass "Add to Catalog" button next to Play Trailer in hero section
  - Modal with search/filter for catalogs (same pattern as friend picker)
  - Create new catalogs inline from the picker modal
  - Removable ghost pills showing current catalog memberships
  - New `CatalogRef` type and `getCatalogsForMovie` API endpoint
  - Server-side query joins catalog_entries with catalog_list
- **2026-02-14**: Changed card hover animation from lift (translateY) to subtle scale (towards viewer)
- **2026-02-14**: Redesigned content blocks — removed card styling, removed link subtype, inline markdown links
  - Blocks render as plain text on background (no cards, no glass effects)
  - Removed "link" as a separate block type; all blocks are text blocks
  - Inline links via markdown `[text](url)` syntax, rendered as clickable `<a>` tags
  - Smart paste preserved: select text + paste URL creates inline `[text](url)` link
  - Replaced "+" button with always-visible "new block" watermark placeholder
  - Legacy link blocks auto-converted to inline markdown format on display
  - Updated Style Guide section to reflect new design
- **2026-02-14**: Added Content Blocks section to Style Guide
  - Live interactive demo with mock data (add, edit, remove text/link blocks)
  - Block type documentation (text, link, image)
  - API reference and interaction patterns (Enter, Escape, smart paste)
  - Design decision callouts (inline editing, smart paste, glass subtle styling)
- **2026-02-14**: New Phase 5 — Design System (Style Guide) added to plan (REQ-029 through REQ-033)
  - Style Guide page at `/styleguide` as single source of truth for components and design tokens
  - Component catalog with parametrizations and design decision explanations
  - Design token documentation (typography, colors, spacing, shapes, glassmorphism)
  - Extract canonical definitions so app pages consume from Style Guide
  - Skills/agents for propagating Style Guide changes to application pages
- **2026-02-14**: Fix rating dropdown z-index — rendered below Recommended By card
  - Root cause: `backdrop-filter` on `glassCard` creates stacking context, trapping dropdown's `z-50`
  - Fix: render dropdown as sibling to glassCard, wrapped in plain `relative` container (no backdrop-filter)
- **2026-02-13**: Personal rating system (adapted from Cinemarco)
  - Removed TMDB rating badge from movie poster cards on the Movies list page
  - Kept TMDB star rating next to genre badges in movie detail hero section
  - Replaced TMDB "Rating" detail card with interactive personal rating dropdown
  - 5-level personal rating: Waste, Meh, Decent, Entertaining, Outstanding — each with icon and color
  - Full event-sourcing: `Personal_rating_set` event, `Set_personal_rating` command, projection column
  - New API endpoint `setPersonalRating` for persisting ratings
  - Glassmorphism dropdown UI with fadeInDown animation
- **2026-02-12**: Movie detail page design overhaul following CineSocial inspiration
  - Tall hero section (500px lg) with backdrop image, gradient overlay, poster + title overlaid at bottom
  - Two-column content grid (8/4): left = Synopsis, Details cards, Cast; right = social sidebar
  - Glass-effect sidebar cards: Recommended By, Watch With, Watch History (timeline), Notes
  - Section headers with primary accent bar, circular cast images, star rating display
  - Friend avatar initials in recommendation/watch-with cards, timeline dots for watch sessions
  - Mobile-responsive: smaller poster, stacked columns on mobile
- **2026-02-12**: Show friend-related movies on Friend Detail page
  - Three sections: "Recommended", "Want to Watch Together", "Watched Together"
  - New shared type `FriendMovies` with `FriendMovieItem` lists
  - New `getFriendMovies` API endpoint with reverse lookups via JSON LIKE queries
  - Each movie links to its detail page with poster thumbnail
- **2026-02-12**: Redesign Friends UX for direct interaction
  - Friends Overview: entire card is now a single link (no image upload on overview)
  - Friend Detail: click name for inline editing (Enter to confirm, Escape to cancel)
  - Friend Detail: click avatar to upload/replace image
  - Removed Edit/Remove buttons; trash icon in top-right corner instead
- **2026-02-12**: Extract FriendPill component for consistent clickable friend name pills
  - Created `Components/FriendPill.fs` with viewSmall, viewSmallWithRemove, viewLargeWithRemove, viewInline variants
  - All friend name pills now navigate to friend page on click (recommendation modal, watch history, want-to-watch-with)
  - Replaced inline badge code in MovieDetail/Views.fs with shared FriendPill component
- **2026-02-12**: Movie detail layout refinements
  - "Recommended by" pill button in top pills area (below genres); only shows recommendation friends + button
  - Watch sessions as compact inline cards (flex-wrap, auto-width) showing date + friend badges
  - "Want to watch with" shown as a dashed-border card in the watch history area with friend badges + add button
  - When no want-to-watch-with friends, shows a "Want to watch with" pill button instead
- **2026-02-12**: Watch time & movie detail UI refinements
  - Dashboard watch time now calculated from movie runtime (TMDB) × sessions per movie (joins watch_sessions with movie_detail)
  - Watch session duration is always the movie's runtime — removed editable duration field from record session form
  - Removed Duration from RecordWatchSessionRequest; server looks up runtime from movie_detail table
  - "Recommended By" and "Want to Watch With" moved to pill collection below genres, above description, with "+ Add" pill
  - Removed old standalone "Recommended By" and "Want to Watch With" sections from below cast
- **2026-02-08**: Phase 4 (Curation + Dashboards + Admin) fully implemented — REQ-022 through REQ-028
  - Catalog aggregate with events: Catalog_created, Catalog_updated, Catalog_removed, Entry_added, Entry_updated, Entry_removed, Entries_reordered — with duplicate movie prevention per catalog
  - CatalogProjection with catalog_list and catalog_entries tables, JOIN to movie_list for movie metadata
  - Catalog list page with create form (name, description, sorted flag)
  - Catalog detail page with entry management (add movies via dropdown, inline note editing, remove entries)
  - Enhanced Dashboard with DashboardStats (movies, friends, catalogs, watch time) and recent activity feed
  - Event Store Browser with stream and event type filtering, expandable event detail with JSON data
  - IMediathecaApi expanded to 46 endpoints (14 new: 9 catalog + 2 dashboard + 3 event store)
  - 118 tests passing (28 new: 18 catalog domain, 10 catalog serialization)
  - New navigation: Catalogs and Events pages in sidebar and bottom nav

## Active Decisions

- **Game aggregate — MODELED**: RAWG API import + manual creation, GameStatus lifecycle (Backlog/Playing/Completed/Abandoned/OnHold), game store ownership (predefined list), Steam Family ownership tracking (friendSlugs), play sessions with duration + friends, social parity with Movies/Series, content blocks, polymorphic catalog entries (Movie | Series | Game) (decided: 2026-02-15)
- **Game stores are digital storefronts** (where you bought/own the game), not hardware platforms: Steam, Nintendo eShop, GOG, Epic Games Store, PlayStation Store, Xbox Store, Humble Bundle, itch.io — with ability to add custom stores in the future (decided: 2026-02-15)
- **Steam Family ownership**: Games can be marked as owned by a Steam Family member (friendSlug). Configuration in Settings maps Steam IDs to Friends. A game can be both personally owned AND available via family members. Import in Phase 7B, manual tagging in Phase 7A (decided: 2026-02-15)
- **RAWG API for game metadata** (like TMDB for movies): search, import metadata + images. Free tier. API key in Settings (decided: 2026-02-15)
- **Play session duration is user-entered** (unlike Movies where duration = runtime). Games don't have a fixed runtime (decided: 2026-02-15)
- **REQ-102c (TV Series Dashboard) deferred** — skipped for now, focus on Games. Moved to v2 backlog (decided: 2026-02-15)
- **TV Series aggregate — MODELED (revised after Cinemarco analysis)**: Single aggregate per series, fat TMDB import event, named rewatch sessions (default personal + named sessions with friends, each tracking independent episode progress), batch operations (mark season watched, mark episodes up to S02E05), series status from TMDB (Returning/Ended/Canceled/InProduction/Planned), "next up" derived from progress, series-level cast (non-event-sourced), full social parity with Movies, polymorphic catalog entries (Movie | Series) (decided: 2026-02-14)
- Movies-only for v1 MVP — **superseded**: v1 now includes Movies, Series, and Games (decided: 2026-01-30, updated: 2026-02-15)
- SQLite-based event store, single file for events + read models (decided: 2026-01-30)
- Local filesystem for image storage (decided: 2026-01-30)
- F# end-to-end: Fable + Giraffe + Fable.Remoting (decided: 2026-01-30)
- DDD with 7 bounded contexts: Movies, Journal, Friends, Curation, Intelligence, Integration, Administration (decided: 2026-01-30)
- Content blocks use Notion-style flexible blocks (TextBlock, ImageBlock, LinkBlock) (decided: 2026-01-30)
- Serena MCP deferred until codebase is large enough (decided: 2026-01-30)
- Self-hosted fonts (Oswald headings, Inter body) via @fontsource (decided: 2026-01-31)
- All images (posters, backdrops, friend profile images) stored as files on disk; events only contain file path references (decided: 2026-02-01)
- Movies are added exclusively via TMDB import; manual edits are fine-grained events (decided: 2026-02-01)
- Cast is NOT event-sourced — stored in a relational table, shared across movies, garbage-collected on movie removal (decided: 2026-02-01)
- Watch sessions are part of the Movie aggregate, not a separate aggregate (decided: 2026-02-01)
- Rotten Tomatoes ratings deferred (no free API); TMDB rating only for now (decided: 2026-02-01)
- "Want to watch with" is an additive list on Movie aggregate; friends auto-removed when watch session recorded (decided: 2026-02-01, implemented: 2026-02-08)
- Decider pattern: pure `decide: State -> Command -> Result<Event list, string>`, `evolve`, `reconstitute` — no IO in domain (decided: 2026-02-01)
- Slug strategy: `slugify(name)-year` for movies, `slugify(name)` for friends and catalogs; numeric suffix for duplicates (decided: 2026-02-01)
- API is a factory: `Api.create conn httpClient tmdbConfig imageBasePath projectionHandlers` returns `IMediathecaApi` (decided: 2026-02-01)
- Feliz.DaisyUI 5.x API: `Daisy.button.button`, `Daisy.modal.dialog`, `Daisy.modalBox.div`, `modal.open'`, no `input.bordered` or `card.compact` (decided: 2026-02-01)
- Content blocks are a separate aggregate per movie (stream: ContentBlocks-{slug}), not part of Movie aggregate — allows independent evolution (decided: 2026-02-08)
- Content blocks support optional sessionId for session-scoped blocks vs movie-level notes (decided: 2026-02-08)
- ContentBlockEditor uses React.useState hooks for local editing state, not Elmish — keeps main model clean (decided: 2026-02-08)
- "Collection" renamed to "Catalog" throughout the codebase (decided: 2026-02-08)
- Catalogs are a separate aggregate (stream: Catalog-{slug}), entries reference movies by slug (decided: 2026-02-08)
- Catalog entries prevent duplicate movies per catalog (decided: 2026-02-08)
- Design system uses two layers: CSS custom properties for raw tokens (opacities, blur, spacing, radii) in index.css, F# module (DesignSystem.fs) for typed compositions (class combinations like glassCard, sectionHeader) — Option C (decided: 2026-02-14)
- REQ-033 skill is an enforcement/audit tool (`/design-check`): scans client files for hardcoded classes that should use DesignSystem.fs or CSS tokens, verifies new components follow design system rules — not a migration tool (decided: 2026-02-14)

## Domain Model — Phase 6 (TV Series) — Modeled (revised after Cinemarco analysis)

### Series Aggregate (Movies context) — `src/Server/Series.fs`

**Stream:** `Series-{slug}` where slug = `seriesSlug name year` = `slugify(name)-year`

**Events:**

*Import & lifecycle:*
- `Series_added_to_library of SeriesAddedData` — fat import: name, year, overview, genres, status, poster/backdrop refs, tmdbId, tmdbRating, episodeRuntime, seasons (list of SeasonImportData with episodes)
- `Series_removed_from_library`

*Metadata updates:*
- `Series_categorized of string list` — genre update
- `Series_poster_replaced of string` — new poster ref
- `Series_backdrop_replaced of string` — new backdrop ref
- `Series_personal_rating_set of int option` — 1-5 rating

*Social (same pattern as Movies):*
- `Series_recommended_by of string` — friendSlug
- `Series_recommendation_removed of string` — friendSlug
- `Series_want_to_watch_with of string` — friendSlug
- `Series_removed_want_to_watch_with of string` — friendSlug

*Named rewatch sessions (series-level tracking contexts):*
- `Rewatch_session_created of RewatchSessionCreatedData` — rewatchId, name?, friendSlugs (default session auto-created on Series_added)
- `Rewatch_session_removed of string` — rewatchId (cannot remove default)
- `Rewatch_session_friend_added of RewatchSessionFriendData` — rewatchId, friendSlug
- `Rewatch_session_friend_removed of RewatchSessionFriendData` — rewatchId, friendSlug

*Episode watch progress (per rewatch session):*
- `Episode_watched of EpisodeWatchedData` — rewatchId, seasonNumber, episodeNumber, date
- `Episode_unwatched of EpisodeUnwatchedData` — rewatchId, seasonNumber, episodeNumber
- `Season_marked_watched of SeasonMarkedWatchedData` — rewatchId, seasonNumber, date
- `Episodes_watched_up_to of EpisodesWatchedUpToData` — rewatchId, seasonNumber, episodeNumber, date

**State:**
```
SeriesState = Not_created | Active of ActiveSeries | Removed

SeriesStatus = Returning | Ended | Canceled | InProduction | Planned | Unknown

ActiveSeries = {
    Name: string
    Year: int
    Overview: string
    Genres: string list
    Status: SeriesStatus
    PosterRef: string option
    BackdropRef: string option
    TmdbId: int
    TmdbRating: float option
    EpisodeRuntime: int option              // typical runtime in minutes (from TMDB)
    PersonalRating: int option
    Seasons: Map<int, SeasonState>          // seasonNumber → SeasonState
    RecommendedBy: Set<string>              // friendSlugs
    WantToWatchWith: Set<string>            // friendSlugs
    RewatchSessions: Map<string, RewatchSessionState>  // rewatchId → session
}

SeasonState = {
    SeasonNumber: int
    Name: string
    Overview: string
    PosterRef: string option
    AirDate: string option
    Episodes: Map<int, EpisodeState>        // episodeNumber → EpisodeState
}

EpisodeState = {
    EpisodeNumber: int
    Name: string
    Overview: string
    Runtime: int option                      // minutes (episode-specific, falls back to series EpisodeRuntime)
    AirDate: string option
    StillRef: string option                  // episode screenshot/still image
    TmdbRating: float option
}

RewatchSessionState = {
    RewatchId: string
    Name: string option                      // None = default personal session
    IsDefault: bool                          // exactly one per series, auto-created
    Friends: Set<string>                     // friendSlugs
    WatchedEpisodes: Set<int * int>          // (seasonNumber, episodeNumber) pairs
}
```

**Import Data (for fat event):**
```
SeriesAddedData = {
    Name: string; Year: int; Overview: string; Genres: string list
    Status: SeriesStatus
    PosterRef: string option; BackdropRef: string option
    TmdbId: int; TmdbRating: float option
    EpisodeRuntime: int option
    Seasons: SeasonImportData list
}

SeasonImportData = {
    SeasonNumber: int; Name: string; Overview: string
    PosterRef: string option; AirDate: string option
    Episodes: EpisodeImportData list
}

EpisodeImportData = {
    EpisodeNumber: int; Name: string; Overview: string
    Runtime: int option; AirDate: string option
    StillRef: string option; TmdbRating: float option
}
```

**Key behaviors:**
- `Series_added_to_library` also implicitly creates a default rewatch session (IsDefault=true, no name, no friends). The `evolve` function handles this.
- `Episode_watched` marks a single episode as watched in a specific rewatch session.
- `Season_marked_watched` emits a single event that marks all episodes in the season. `evolve` adds all (season, episode) pairs to the session's WatchedEpisodes set.
- `Episodes_watched_up_to` marks episodes 1 through N in a season. `evolve` adds pairs (season, 1)..(season, N).
- **"Next up"** is derived in the projection: first unwatched episode across all seasons in the default session, ordered by (seasonNumber, episodeNumber).
- **Overall progress** = union of WatchedEpisodes across all rewatch sessions. A projection column `watched_episode_count` on `series_list`.
- **Watch status** (NotStarted / InProgress / Completed) derived from overall progress vs total episode count.

**Cast:** Non-event-sourced, same CastStore pattern as Movies. Series-level cast from TMDB `/tv/{id}/credits`. Shared `cast_members` table, `series_cast` junction table (mirrors `movie_cast`). Garbage-collect orphaned cast on series removal.

**Content Blocks:** Separate aggregate `ContentBlocks-{seriesSlug}` (same pattern as movies). Series-level notes.

**Catalog Integration:** Catalog entries become polymorphic — `MediaType` discriminator (Movie | Series). `Entry_added` event gains `mediaType` field alongside existing `movieSlug` (renamed to `mediaSlug`). Existing movie entries default to Movie type.

### Projection Tables (SeriesProjection)

- `series_list`: slug, name, year, poster_ref, genres (JSON), tmdb_rating, status, season_count, episode_count, watched_episode_count, next_up_season, next_up_episode, next_up_title
- `series_detail`: slug, name, year, overview, genres (JSON), poster_ref, backdrop_ref, tmdb_id, tmdb_rating, episode_runtime, status, personal_rating, recommended_by (JSON), want_to_watch_with (JSON)
- `series_seasons`: series_slug, season_number, name, overview, poster_ref, air_date, episode_count
- `series_episodes`: series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating
- `series_rewatch_sessions`: rewatch_id, series_slug, name, is_default, friends (JSON), watched_episodes (JSON array of [season,episode] pairs), watched_count
- `series_episode_progress`: series_slug, rewatch_id, season_number, episode_number, watched_date (one row per watched episode per session)

### TMDB Integration

- `searchTvSeries`: `/search/tv` endpoint → TmdbSearchResult list (reuse existing type with mediaType flag)
- `getTvSeriesDetails`: `/tv/{id}` → series metadata + season list + status
- `getTvSeasonDetails`: `/tv/{id}/season/{n}` → episodes for one season (called per season during import)
- `getTvSeriesCredits`: `/tv/{id}/credits` → series-level cast
- Image downloads: posters → `images/posters/series-{slug}.jpg`, backdrops → `images/backdrops/series-{slug}.jpg`, episode stills → `images/stills/{slug}-s{nn}e{nn}.jpg`

### Shared DTOs

- `SeriesListItem`: Slug, Name, Year, PosterRef, Genres, TmdbRating, Status, SeasonCount, EpisodeCount, WatchedEpisodeCount, NextUp (option of SeasonNum * EpNum * Title)
- `SeriesDetail`: Full aggregate view + Cast + ContentBlocks + RewatchSessions + CatalogRefs
- `SeasonDto`: SeasonNumber, Name, Overview, PosterRef, AirDate, Episodes (EpisodeDto list), WatchedCount (in default session), OverallWatchedCount
- `EpisodeDto`: EpisodeNumber, Name, Overview, Runtime, AirDate, StillRef, TmdbRating, IsWatched (in selected session), WatchedDate option
- `RewatchSessionDto`: RewatchId, Name option, IsDefault, Friends (FriendRef list), WatchedCount, CompletionPercentage
- `WatchStatus` = NotStarted | InProgress | Completed (derived, not stored as event)

### API Endpoints (added to IMediathecaApi)

*Import & lifecycle:*
- `searchTvSeries: string -> Async<TmdbSearchResult list>`
- `addSeries: int -> Async<Result<string, string>>` — tmdbId, imports full series + creates default rewatch session
- `removeSeries: string -> Async<Result<unit, string>>`

*Read:*
- `getSeries: unit -> Async<SeriesListItem list>`
- `getSeriesDetail: string -> Async<SeriesDetail option>`
- `getCatalogsForSeries: string -> Async<CatalogRef list>`

*Metadata & social:*
- `setSeriesPersonalRating: string -> int option -> Async<Result<unit, string>>`
- `addSeriesRecommendation: string -> string -> Async<Result<unit, string>>`
- `removeSeriesRecommendation: string -> string -> Async<Result<unit, string>>`
- `addSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>`
- `removeSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>`

*Rewatch sessions:*
- `createRewatchSession: string -> CreateRewatchSessionRequest -> Async<Result<string, string>>` — seriesSlug, name + friends
- `removeRewatchSession: string -> string -> Async<Result<unit, string>>` — seriesSlug, rewatchId
- `addFriendToRewatchSession: string -> string -> string -> Async<Result<unit, string>>` — seriesSlug, rewatchId, friendSlug
- `removeFriendFromRewatchSession: string -> string -> string -> Async<Result<unit, string>>`

*Episode progress (per rewatch session):*
- `markEpisodeWatched: string -> MarkEpisodeWatchedRequest -> Async<Result<unit, string>>` — seriesSlug, rewatchId + season + episode + date
- `markEpisodeUnwatched: string -> MarkEpisodeUnwatchedRequest -> Async<Result<unit, string>>` — seriesSlug, rewatchId + season + episode
- `markSeasonWatched: string -> MarkSeasonWatchedRequest -> Async<Result<unit, string>>` — seriesSlug, rewatchId + season + date
- `markEpisodesWatchedUpTo: string -> MarkEpisodesUpToRequest -> Async<Result<unit, string>>` — seriesSlug, rewatchId + season + episode + date

### Client Pages

- `Series_list` page at `/series` — poster grid/list with search, season count badges, "next up" on cards, series status badge (Returning/Ended/etc.)
- `Series_detail` page at `/series/{slug}` — hero with backdrop, tab bar (Overview, Cast & Crew, Episodes), season sidebar with progress counts, episode list with thumbnails/watch status/green checkmarks, "next up" highlight, rewatch session selector dropdown, "Mark Season Watched" button
- Navigation: add "TV Series" to sidebar and bottom nav (tv icon)

## Domain Model — Phase 4 (Implemented)

### Catalog Aggregate (Curation context) — `src/Server/Catalogs.fs`

Events:
- `Catalog_created` — name, description, isSorted
- `Catalog_updated` — name, description
- `Catalog_removed`
- `Entry_added` — entryId, movieSlug, note?, position
- `Entry_updated` — entryId, note?
- `Entry_removed` — entryId
- `Entries_reordered` — entryIds

State: `Not_created | Active of ActiveCatalog | Removed`
ActiveCatalog includes: Entries: Map<string, EntryState>
Stream: `Catalog-{slug}`

### Movie Aggregate (Movies context) — `src/Server/Movies.fs`

Events: Movie_added_to_library, Movie_removed_from_library, Movie_categorized, Movie_poster_replaced, Movie_backdrop_replaced, Movie_recommended_by, Recommendation_removed, Want_to_watch_with, Removed_want_to_watch_with, Watch_session_recorded, Watch_session_date_changed, Friend_added_to_watch_session, Friend_removed_from_watch_session, Personal_rating_set

### ContentBlocks Aggregate — `src/Server/ContentBlocks.fs`

Events: Content_block_added, Content_block_updated, Content_block_removed, Content_blocks_reordered

### Friend Aggregate (Friends context) — `src/Server/Friends.fs`

Events: Friend_added, Friend_updated, Friend_removed

## Blockers

- (none)

## Domain Model — Phase 7A (Games) — Modeled

### Game Aggregate — `src/Server/Games.fs`

**Stream:** `Game-{slug}` where slug = `slugify(name)-year`

**Events:**

*Import & lifecycle:*
- `Game_added_to_library of GameAddedData` — fat import (RAWG or manual): name, year, genres, description, coverRef, backdropRef, rawgId, rawgRating
- `Game_removed_from_library`

*Metadata updates:*
- `Game_categorized of string list` — genre update
- `Game_cover_replaced of string` — new cover ref
- `Game_backdrop_replaced of string` — new backdrop ref
- `Game_personal_rating_set of int option` — 1-5 rating
- `Game_status_changed of GameStatus` — Backlog/Playing/Completed/Abandoned/OnHold
- `Game_hltb_hours_set of float option` — manual entry (API import in Phase 7B)

*Store ownership:*
- `Game_store_added of string` — store identifier ("steam", "gog", etc.)
- `Game_store_removed of string`

*Steam Family ownership:*
- `Game_family_owner_added of string` — friendSlug (family member owns this game)
- `Game_family_owner_removed of string` — friendSlug

*Social (same pattern as Movies/Series):*
- `Game_recommended_by of string` — friendSlug
- `Game_recommendation_removed of string` — friendSlug
- `Want_to_play_with of string` — friendSlug
- `Removed_want_to_play_with of string` — friendSlug

*Play sessions:*
- `Play_session_recorded of PlaySessionData` — sessionId, date, durationMinutes, friendSlugs
- `Play_session_date_changed of PlaySessionDateChangedData` — sessionId, newDate
- `Play_session_duration_changed of PlaySessionDurationChangedData` — sessionId, newDurationMinutes
- `Friend_added_to_play_session of PlaySessionFriendData` — sessionId, friendSlug
- `Friend_removed_from_play_session of PlaySessionFriendData` — sessionId, friendSlug

**State:**
```
GameState = Not_created | Active of ActiveGame | Removed

GameStatus = Backlog | Playing | Completed | Abandoned | OnHold

ActiveGame = {
    Name: string
    Year: int
    Genres: string list
    Description: string
    CoverRef: string option
    BackdropRef: string option
    RawgId: int option
    RawgRating: float option
    HowLongToBeatHours: float option
    PersonalRating: int option
    Status: GameStatus
    Stores: Set<string>                     // store identifiers where I own it
    FamilyOwners: Set<string>               // friendSlugs who own it (Steam Family)
    RecommendedBy: Set<string>              // friendSlugs
    WantToPlayWith: Set<string>             // friendSlugs
    PlaySessions: Map<string, PlaySessionState>  // sessionId → session
}

PlaySessionState = {
    SessionId: string
    Date: string
    DurationMinutes: int
    Friends: Set<string>                    // friendSlugs
}
```

**Import Data (for fat event):**
```
GameAddedData = {
    Name: string; Year: int; Genres: string list; Description: string
    CoverRef: string option; BackdropRef: string option
    RawgId: int option; RawgRating: float option
}
```

**Key behaviors:**
- `Game_added_to_library` creates the game with default status `Backlog`
- `Game_status_changed` is the primary lifecycle event — Backlog → Playing → Completed/Abandoned/OnHold
- Play sessions accumulate total play time (sum of all session durations) — projected onto game_list and game_detail
- Store ownership and family ownership are independent: a game can be personally owned on GOG AND available via a family member on Steam
- Content blocks: separate aggregate `ContentBlocks-{gameSlug}` (same pattern as Movies/Series)
- Catalog integration: `MediaType` gains `Game` variant alongside `Movie` and `Series`

**Predefined game stores:** Steam, Nintendo eShop, GOG, Epic Games Store, PlayStation Store, Xbox Store, Humble Bundle, itch.io

### Projection Tables (GameProjection)

- `game_list`: slug, name, year, cover_ref, genres (JSON), status, total_play_time, hltb_hours, personal_rating, rawg_rating
- `game_detail`: slug, name, year, description, cover_ref, backdrop_ref, genres (JSON), status, rawg_id, rawg_rating, hltb_hours, personal_rating, stores (JSON), family_owners (JSON), recommended_by (JSON), want_to_play_with (JSON), total_play_time
- `play_sessions`: session_id (PK), game_slug, date, duration_minutes, friends (JSON)

### RAWG Integration

- `searchGames`: `/games?search=...` endpoint → search results
- `getGameDetails`: `/games/{id}` → game metadata + screenshots
- Image downloads: covers → `images/posters/game-{slug}.jpg`, backgrounds → `images/backdrops/game-{slug}.jpg`
- API key stored in Settings table (like TMDB)

### Shared DTOs

- `GameListItem`: Slug, Name, Year, CoverRef, Genres, Status, TotalPlayTimeMinutes, HltbHours, PersonalRating, RawgRating
- `GameDetail`: Full aggregate view + Stores + FamilyOwners + ContentBlocks + CatalogRefs
- `PlaySessionDto`: SessionId, Date, DurationMinutes, Friends (FriendRef list)
- `GameStatus` = Backlog | Playing | Completed | Abandoned | OnHold
- `GameStoreInfo`: predefined store list for UI dropdowns

### API Endpoints (added to IMediathecaApi)

*Import & lifecycle:*
- `searchRawgGames: string -> Async<RawgSearchResult list>`
- `addGame: AddGameRequest -> Async<Result<string, string>>` — RAWG import or manual creation
- `removeGame: string -> Async<Result<unit, string>>`

*Read:*
- `getGames: unit -> Async<GameListItem list>`
- `getGameDetail: string -> Async<GameDetail option>`
- `getCatalogsForGame: string -> Async<CatalogRef list>`

*Metadata & social:*
- `setGamePersonalRating: string -> int option -> Async<Result<unit, string>>`
- `setGameStatus: string -> GameStatus -> Async<Result<unit, string>>`
- `setGameHltbHours: string -> float option -> Async<Result<unit, string>>`
- `addGameRecommendation: string -> string -> Async<Result<unit, string>>`
- `removeGameRecommendation: string -> string -> Async<Result<unit, string>>`
- `addGameWantToPlayWith: string -> string -> Async<Result<unit, string>>`
- `removeGameWantToPlayWith: string -> string -> Async<Result<unit, string>>`

*Store & family ownership:*
- `addGameStore: string -> string -> Async<Result<unit, string>>` — gameSlug, store
- `removeGameStore: string -> string -> Async<Result<unit, string>>`
- `addGameFamilyOwner: string -> string -> Async<Result<unit, string>>` — gameSlug, friendSlug
- `removeGameFamilyOwner: string -> string -> Async<Result<unit, string>>`

*Play sessions:*
- `recordPlaySession: string -> RecordPlaySessionRequest -> Async<Result<unit, string>>` — gameSlug, date + duration + friends
- `changePlaySessionDate: string -> ChangePlaySessionDateRequest -> Async<Result<unit, string>>`
- `changePlaySessionDuration: string -> ChangePlaySessionDurationRequest -> Async<Result<unit, string>>`
- `addFriendToPlaySession: string -> string -> string -> Async<Result<unit, string>>` — gameSlug, sessionId, friendSlug
- `removeFriendFromPlaySession: string -> string -> string -> Async<Result<unit, string>>`

*Content blocks:*
- Reuse existing content block endpoints with game slugs

*RAWG:*
- `getRawgApiKey: unit -> Async<string option>`
- `setRawgApiKey: string -> Async<unit>`
- `testRawgApiKey: string -> Async<bool>`

### Client Pages

- `Game_list` page at `/games` — poster grid/list with search, GameStatus filter badges (Backlog/Playing/Completed/Abandoned/OnHold)
- `Game_detail` page at `/games/{slug}` — hero with backdrop, info section, play sessions timeline, store badges, family owners (friend pills), social sidebar (same layout pattern as Movies/Series)
- Navigation: add "Games" to sidebar and bottom nav (gamepad icon)
- Settings page: add RAWG API key input (same pattern as TMDB)

### Steam Family Configuration (Phase 7B, but data model designed now)

- Settings table entries: `steam_family_members` → JSON array of `{ steamId: string, steamName: string, friendSlug: string }`
- Settings page UI: configure Steam Family members, link each to a Friend
- Used during Steam import to automatically tag games with family owners

## Full Progress History

- 2026-01-30 Brainstorm completed — PROJECT.md created with full vision
- 2026-01-30 Requirements categorized (v1/v2), 4-phase roadmap approved
- 2026-01-31 Phase 1 (Skeleton) complete — all 8 requirements done
- 2026-02-01 Phase 2 implementation complete — all 8 requirements done (REQ-009 through REQ-015)
- 2026-02-08 Frontend visual refresh, Movies rename, snake_case convention
- 2026-02-08 Phase 3 implementation complete — all 6 requirements done (REQ-016 through REQ-021)
- 2026-02-08 Phase 4 implementation complete — all 7 requirements done (REQ-022 through REQ-028)
- 2026-02-14 Phase 5 (Design System / Style Guide) complete — all 5 requirements done (REQ-029 through REQ-033)
- 2026-02-14 Phase 6 (TV Series) implementation complete — 7 of 8 requirements done (REQ-100 through REQ-102b)
- 2026-02-15 Phase 7 (Games) planning complete — 10 requirements defined (REQ-200 through REQ-209)
- 2026-02-15 Phase 7A (Game Catalog + Play Sessions) implementation complete — all 7 requirements done (REQ-200 through REQ-206)

## Next Actions

1. **REQ-207: Games Dashboard** — currently playing, recent sessions, play time stats, completion vs HowLongToBeat
2. **REQ-208: Steam integration** — import personal + family libraries, Steam Family member configuration
3. **REQ-209: HowLongToBeat integration** — fetch average completion times
4. Polish Game detail page (visual refinements, mobile responsiveness)
5. Fix pre-existing integration test failures (missing friend_list table in test setup)
