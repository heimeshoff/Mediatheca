# Current State

**Last Updated:** 2026-02-14
**Current Phase:** 5 (Design System / Style Guide) — Complete
**Current Task:** All v1 phases + Phase 5 complete

## Recent Progress

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

- Movies-only for v1 MVP (decided: 2026-01-30)
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

## Full Progress History

- 2026-01-30 Brainstorm completed — PROJECT.md created with full vision
- 2026-01-30 Requirements categorized (v1/v2), 4-phase roadmap approved
- 2026-01-31 Phase 1 (Skeleton) complete — all 8 requirements done
- 2026-02-01 Phase 2 implementation complete — all 8 requirements done (REQ-009 through REQ-015)
- 2026-02-08 Frontend visual refresh, Movies rename, snake_case convention
- 2026-02-08 Phase 3 implementation complete — all 6 requirements done (REQ-016 through REQ-021)
- 2026-02-08 Phase 4 implementation complete — all 7 requirements done (REQ-022 through REQ-028)
- 2026-02-14 Phase 5 (Design System / Style Guide) complete — all 5 requirements done (REQ-029 through REQ-033)

## Next Actions

Phase 5 complete! Consider:
1. Use `/design-check` to audit design system compliance
2. Visit `/styleguide` to review and refine the design system
3. v2 features (TV Series, Games, Books, Integrations)
4. Polish and bug fixes
