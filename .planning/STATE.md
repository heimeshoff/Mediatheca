# Current State

**Last Updated:** 2026-02-08
**Current Phase:** 4 (Curation + Dashboards + Admin) — Complete
**Current Task:** All v1 MVP phases complete

## Recent Progress

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

Events: Movie_added_to_library, Movie_removed_from_library, Movie_categorized, Movie_poster_replaced, Movie_backdrop_replaced, Movie_recommended_by, Recommendation_removed, Want_to_watch_with, Removed_want_to_watch_with, Watch_session_recorded, Watch_session_date_changed, Friend_added_to_watch_session, Friend_removed_from_watch_session

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

## Next Actions

v1 MVP is complete! All 4 phases implemented. Consider:
1. v2 features (TV Series, Games, Books, Integrations)
2. Polish and bug fixes
3. Production deployment testing
