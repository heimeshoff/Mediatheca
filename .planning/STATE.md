# Current State

**Last Updated:** 2026-02-08
**Current Phase:** 2 (Movies + Friends) — Complete
**Current Task:** Phase 2 fully implemented, ready for Phase 3

## Recent Progress

- **2026-02-08**: Frontend visual refresh — sidebar with active indicator bar + gradient logo, movie cards with hover-lift animation + rating badge overlay + poster hover overlay, friend cards with ring-on-hover avatars, dashboard redesigned with hero gradient section + stat cards (movie/friend counts) + recent movies list, staggered fade-in animations on grids, gradient text on page headings, search icon in movies search bar, improved empty states with large icons.
- **2026-02-08**: Renamed "Catalog" bounded context to "Movies". Renamed all DU cases (events, commands, messages, page routes) from PascalCase to Snake_case convention (e.g., `MovieRemovedFromLibrary` → `Movie_removed_from_library`). Updated serialization strings, test files, projections, and all client references. Note: existing `mediatheca.db` must be recreated due to serialization string changes.

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
- Watch sessions deferred to Phase 3; content blocks / commenting also Phase 3 (decided: 2026-02-01)
- Rotten Tomatoes ratings deferred (no free API); TMDB rating only for now (decided: 2026-02-01)
- "Want to watch with" is an additive list on Movie aggregate; friends auto-removed when watch session recorded in Phase 3 (decided: 2026-02-01)
- Decider pattern: pure `decide: State -> Command -> Result<Event list, string>`, `evolve`, `reconstitute` — no IO in domain (decided: 2026-02-01)
- Slug strategy: `slugify(name)-year` for movies, `slugify(name)` for friends; numeric suffix for duplicates (decided: 2026-02-01)
- API is a factory: `Api.create conn httpClient tmdbConfig imageBasePath projectionHandlers` returns `IMediathecaApi` (decided: 2026-02-01)
- Feliz.DaisyUI 5.x API: `Daisy.button.button`, `Daisy.modal.dialog`, `Daisy.modalBox.div`, `modal.open'`, no `input.bordered` or `card.compact` (decided: 2026-02-01)

## Domain Model — Phase 2 (Implemented)

### Movie Aggregate (Catalog context) — `src/Server/Catalog.fs`

Events:
- `MovieAddedToLibrary` — fat event: name, year, runtime, overview, genres, posterRef, backdropRef, tmdbId, tmdbRating
- `MovieRemovedFromLibrary` — triggers cast cleanup (delete orphaned cast members + their images)
- `MovieCategorized` — genre override (idempotent: no event if genres unchanged)
- `MoviePosterReplaced` — new posterRef
- `MovieBackdropReplaced` — new backdropRef
- `MovieRecommendedBy` — friendSlug (additive, idempotent)
- `RecommendationRemoved` — friendSlug (no-op if not present)
- `WantToWatchWith` — friendSlug (additive, idempotent)
- `RemovedWantToWatchWith` — friendSlug (no-op if not present)

State: `NotCreated | Active of ActiveMovie | Removed`

Non-event-sourced:
- Cast tables (`cast_members`, `movie_cast`) in `src/Server/CastStore.fs`
- Orphaned cast members deleted along with their image files

### Friend Aggregate (Friends context) — `src/Server/Friends.fs`

Events:
- `FriendAdded` — name, imageRef
- `FriendUpdated` — name, imageRef
- `FriendRemoved`

State: `NotCreated | Active of ActiveFriend | Removed`

### Phase 3 additions (planned, not implemented yet)
- `WatchSessionRecorded` — date, list of FriendIds (part of Movie aggregate)
- `WatchSessionDateChanged`
- `FriendAddedToWatchSession`
- `FriendRemovedFromWatchSession`
- Auto-remove friends from "want to watch with" list when watch session recorded

## Blockers

- (none)

## Recent Progress

- 2026-01-30 Brainstorm completed — PROJECT.md created with full vision
- 2026-01-30 Requirements categorized (v1/v2), 4-phase roadmap approved
- 2026-01-30 Added Audible as book integration source alongside Goodreads (REQ-121)
- 2026-01-31 Updated REQUIREMENTS.md — marked Phase 1 completions (REQ-001..005, 007, 008 done)
- 2026-01-31 REQ-006 completed — app shell with sidebar/bottom nav, routing, 5 pages + NotFound, dim theme
- 2026-01-31 Phase 1 (Skeleton) complete — all 8 requirements done
- 2026-02-01 Phase 2 domain modeling session — Movie aggregate, Friend aggregate, TMDB import, cast table, image storage, want-to-watch-with, recommended-by
- 2026-02-01 Phase 2 implementation complete — all 8 requirements done (REQ-009 through REQ-015)
  - Shared types: Slug module, DTOs, expanded IMediathecaApi (18 endpoints)
  - Server: Catalog.fs, Friends.fs (pure decider pattern), Tmdb.fs, ImageStore.fs, CastStore.fs, MovieProjection.fs, FriendProjection.fs, Api.fs (factory), Program.fs (wiring)
  - Client: Router with MovieList/MovieDetail/FriendList/FriendDetail, TmdbSearchModal, movie/friend list+detail pages, Sidebar/BottomNav active-page matching
  - Tests: 57 passing (9 slug, 22 catalog domain, 9 friends domain, 8 serialization, 5 catalog integration, 4 friend integration)
  - Config: Vite /images proxy, Dockerfile image volume + TMDB_API_KEY

## Next Actions

1. REQ-016: WatchSession events on Movie aggregate — define event types, command handlers
2. REQ-017: Watch history view on movie detail page
3. REQ-018: Content block system (TextBlock, ImageBlock, LinkBlock) — domain model
4. REQ-019: Content blocks attachable to watch sessions
5. REQ-020: Image upload for content blocks
6. REQ-021: Inline content block editor UI
