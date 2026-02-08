# Current State

**Last Updated:** 2026-02-08
**Current Phase:** 3 (Journal + Content Blocks) — Complete
**Current Task:** Phase 3 fully implemented, ready for Phase 4

## Recent Progress

- **2026-02-08**: Phase 3 (Journal + Content Blocks) fully implemented — REQ-016 through REQ-021
  - WatchSession events on Movie aggregate: Watch_session_recorded, Watch_session_date_changed, Friend_added_to_watch_session, Friend_removed_from_watch_session — with auto-removal of friends from "want to watch with" when session recorded
  - ContentBlock system as separate aggregate (ContentBlocks-{movieSlug} stream): Content_block_added, Content_block_updated, Content_block_removed, Content_blocks_reordered — with session scoping via optional sessionId
  - ContentBlockProjection with content_blocks table, movie/session indexes
  - Watch history UI on movie detail page with session cards and "Record Session" modal (date picker, duration, friend multi-select)
  - Content block editor component (Notes section) with inline add/edit/delete for text, image, and link blocks
  - Image upload API for content block images stored at images/content/
  - IMediathecaApi expanded to 32 endpoints (11 new: 5 watch session + 6 content block)
  - 90 tests passing (33 new: 12 watch session domain, 5 watch session serialization, 1 watch session integration, 15 content block domain + serialization)

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
- Slug strategy: `slugify(name)-year` for movies, `slugify(name)` for friends; numeric suffix for duplicates (decided: 2026-02-01)
- API is a factory: `Api.create conn httpClient tmdbConfig imageBasePath projectionHandlers` returns `IMediathecaApi` (decided: 2026-02-01)
- Feliz.DaisyUI 5.x API: `Daisy.button.button`, `Daisy.modal.dialog`, `Daisy.modalBox.div`, `modal.open'`, no `input.bordered` or `card.compact` (decided: 2026-02-01)
- Content blocks are a separate aggregate per movie (stream: ContentBlocks-{slug}), not part of Movie aggregate — allows independent evolution (decided: 2026-02-08)
- Content blocks support optional sessionId for session-scoped blocks vs movie-level notes (decided: 2026-02-08)
- ContentBlockEditor uses React.useState hooks for local editing state, not Elmish — keeps main model clean (decided: 2026-02-08)

## Domain Model — Phase 3 (Implemented)

### Movie Aggregate (Movies context) — `src/Server/Movies.fs`

Events:
- `Movie_added_to_library` — fat event: name, year, runtime, overview, genres, posterRef, backdropRef, tmdbId, tmdbRating
- `Movie_removed_from_library` — triggers cast cleanup
- `Movie_categorized` — genre override (idempotent)
- `Movie_poster_replaced` — new posterRef
- `Movie_backdrop_replaced` — new backdropRef
- `Movie_recommended_by` — friendSlug (additive, idempotent)
- `Recommendation_removed` — friendSlug
- `Want_to_watch_with` — friendSlug (additive, idempotent)
- `Removed_want_to_watch_with` — friendSlug
- `Watch_session_recorded` — sessionId, date, duration?, friendSlugs (auto-removes friends from want_to_watch_with)
- `Watch_session_date_changed` — sessionId, date
- `Friend_added_to_watch_session` — sessionId, friendSlug (also removes from want_to_watch_with)
- `Friend_removed_from_watch_session` — sessionId, friendSlug

State: `Not_created | Active of ActiveMovie | Removed`
ActiveMovie includes: WatchSessions: Map<string, WatchSessionState>

### ContentBlocks Aggregate — `src/Server/ContentBlocks.fs`

Events:
- `Content_block_added` — blockData, position, sessionId?
- `Content_block_updated` — blockId, content, imageRef?, url?, caption?
- `Content_block_removed` — blockId
- `Content_blocks_reordered` — blockIds, sessionId?

State: `ContentBlocksState` with `Blocks: Map<string, BlockState>`
Stream: `ContentBlocks-{movieSlug}` (separate from Movie stream)

### Friend Aggregate (Friends context) — `src/Server/Friends.fs`

Events: Friend_added, Friend_updated, Friend_removed
State: `Not_created | Active of ActiveFriend | Removed`

## Blockers

- (none)

## Full Progress History

- 2026-01-30 Brainstorm completed — PROJECT.md created with full vision
- 2026-01-30 Requirements categorized (v1/v2), 4-phase roadmap approved
- 2026-01-31 Phase 1 (Skeleton) complete — all 8 requirements done
- 2026-02-01 Phase 2 implementation complete — all 8 requirements done (REQ-009 through REQ-015)
- 2026-02-08 Frontend visual refresh, Movies rename, snake_case convention
- 2026-02-08 Phase 3 implementation complete — all 6 requirements done (REQ-016 through REQ-021)

## Next Actions

1. REQ-022: Collection aggregate with events — create, update, remove collections
2. REQ-023: Add entries (movies) to collections with position and per-item notes
3. REQ-024: Collection detail view showing entries in order with notes
4. REQ-025: Collection list view
5. REQ-026: Main Dashboard — recent activity, recently added movies, quick stats
6. REQ-027: Movies Dashboard — movie-specific stats
7. REQ-028: Event Store Browser — view and search events
