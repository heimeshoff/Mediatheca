# Roadmap

## Current Milestone: v1 — Personal Media Tracker

| Phase | Name | Status | Requirements |
|-------|------|--------|--------------|
| 1 | Skeleton | :white_check_mark: Done | REQ-001, REQ-002, REQ-003, REQ-004, REQ-005, REQ-006, REQ-007, REQ-008 |
| 2 | Catalog + Friends | :white_check_mark: Done | REQ-009, REQ-010, REQ-010a, REQ-011, REQ-012, REQ-013, REQ-014, REQ-015 |
| 3 | Journal + Content Blocks | :white_check_mark: Done | REQ-016, REQ-017, REQ-018, REQ-019, REQ-020, REQ-021 |
| 4 | Curation + Dashboards + Admin | :white_check_mark: Done | REQ-022, REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-028 |
| 5 | Design System (Style Guide) | :white_check_mark: Done | REQ-029, REQ-030, REQ-031, REQ-032, REQ-033 |
| 6 | TV Series | :white_check_mark: Done | REQ-100, REQ-101, REQ-101a, REQ-101b, REQ-102, REQ-102a, REQ-102b, REQ-102c |

### Phase 1: Skeleton

Deliverable: A running F# web app with empty pages, working build pipeline, event store infrastructure, and Docker deployment.

- F# solution: Shared, Server (Giraffe), Client (Fable/Feliz)
- Fable.Remoting RPC between client and server
- Vite dev server with concurrently (frontend + backend)
- TailwindCSS + DaisyUI configured
- SQLite event store (append-only, stream-based)
- Projection engine (event replay into read model tables)
- App shell with navigation (mobile bottom nav, desktop sidebar)
- Dockerfile for Linux deployment
- Expecto test project

### Phase 2: Catalog + Friends

Deliverable: Users can add movies via TMDB import, browse their library, manage friends, and track recommendations and watch-with intentions.

- Catalog bounded context: Movie aggregate with granular events (MovieAddedToLibrary, MovieRemovedFromLibrary, MovieCategorized, MoviePosterReplaced, MovieBackdropReplaced, MovieRecommendedBy, RecommendationRemoved, WantToWatchWith, RemovedWantToWatchWith)
- TMDB API integration: search movies, import metadata + images to local filesystem
- Cast table (non-event-sourced): shared across movies, many-to-many, orphan cleanup on movie removal
- Movie list page with search/filter
- Movie detail page (metadata, cast, genres, recommendations, want-to-watch-with)
- Friends bounded context: Friend aggregate (FriendAdded, FriendUpdated, FriendRemoved) — name + image reference
- Friend list page with add/edit
- "Recommended by" and "Want to watch with" on movie detail page

### Phase 3: Journal + Content Blocks

Deliverable: Users can log watch sessions with friends and attach rich content blocks to movies and sessions.

- Journal bounded context: WatchSession aggregate with events
- Record watch session (date, duration, friends present)
- Watch history view on movie detail page
- Content block system (TextBlock, ImageBlock, LinkBlock)
- Content block editor (add, edit, reorder, delete)
- Image upload to local filesystem
- Attach content blocks to movies and to individual sessions

### Phase 4: Curation + Dashboards + Admin

Deliverable: Users can organize movies into catalogs, view dashboards with stats, and inspect the event store.

- Curation bounded context: Catalog aggregate with events (Catalog_created, Catalog_updated, Catalog_removed, Entry_added, Entry_updated, Entry_removed, Entries_reordered)
- Create sorted/unsorted catalogs with descriptions
- Add entries (movies) with position and per-item notes
- Catalog list and detail pages
- Main dashboard (recent activity, quick stats: movies, friends, catalogs, watch time)
- Administration: Event store browser (view/search/filter events by stream and type)

### Phase 5: Design System (Style Guide)

Deliverable: A living style guide page that serves as the single source of truth for all design decisions, component definitions, and design tokens. Changes to the design system are made in the Style Guide first and then propagated to application pages via dedicated skills/agents.

- Style Guide page at `/styleguide` (hidden route, no nav entry)
- Component catalog: every reusable component with all parametrizations and design decision explanations
- Design token documentation: typography, colors, spacing, shapes, glassmorphism, with rationale
- Extract components and tokens so Style Guide is the canonical definition, app pages consume from it
- Skills/agents for propagating Style Guide changes to all application pages

### Phase 6: TV Series

Deliverable: Users can add TV series via TMDB import, browse seasons and episodes, track episode-level watch progress with friends, and view a series dashboard with progress and next-to-watch.

- TV Series aggregate: Series > Season > Episode hierarchy, fat TMDB import, SeriesStatus tracking
- TMDB API integration: search TV series, import series/season/episode metadata + images (posters, backdrops, episode stills)
- Named rewatch sessions: default personal + named sessions with friends, independent episode progress per session
- Batch operations: mark season watched, mark episodes watched up to S##E##
- Social features: recommended_by, want_to_watch_with, personal_rating, content_blocks (full parity with Movies)
- Series detail page: hero with backdrop, tab bar, season sidebar with progress, episode list with watch status, "next up", rewatch session selector
- Series list page with search/filtering, poster grid/list, series status badges
- Polymorphic catalog entries: catalogs can hold Movies and Series
- TV Series dashboard: episode progress, next-to-watch across all series

**Status: Domain model complete. Ready for implementation.**

## Completed Milestones

- Phase 1: Skeleton (2026-01-31)
- Phase 2: Catalog + Friends (2026-02-01)
- Phase 3: Journal + Content Blocks (2026-02-08)
- Phase 4: Curation + Dashboards + Admin (2026-02-08)
