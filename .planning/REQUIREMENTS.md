# Requirements

## v1 — Personal Media Tracker

### Phase 1: Skeleton

- [x] REQ-001: F# solution structure with Shared, Server, and Client projects using Fable, Giraffe, and Fable.Remoting [Phase 1]
- [x] REQ-002: Vite + concurrently dev pipeline for simultaneous frontend and backend development [Phase 1]
- [x] REQ-003: TailwindCSS + DaisyUI integration with Fable/Feliz frontend [Phase 1]
- [x] REQ-004: SQLite-based append-only event store with event serialization and stream support [Phase 1]
- [x] REQ-005: Projection engine that replays events into SQLite read model tables [Phase 1]
- [x] REQ-006: App shell with mobile-first responsive layout and navigation (sidebar/bottom nav) [Phase 1]
- [x] REQ-007: Dockerfile for production deployment on Linux [Phase 1]
- [x] REQ-008: Expecto test project with build integration [Phase 1]

### Phase 2: Catalog + Friends

- [x] REQ-009: Movie aggregate with events: MovieAddedToLibrary (fat TMDB import event), MovieRemovedFromLibrary, MovieCategorized, MoviePosterReplaced, MovieBackdropReplaced, MovieRecommendedBy, RecommendationRemoved, WantToWatchWith, RemovedWantToWatchWith [Phase 2]
- [x] REQ-010: TMDB API integration — search movies, import metadata (name, year, runtime, overview, genres, poster, backdrop, TMDB rating), download images to local filesystem [Phase 2]
- [x] REQ-010a: Cast table (non-event-sourced) — store top-billed and full cast per movie, shared across movies, garbage-collect orphaned cast members and their images on movie removal [Phase 2]
- [x] REQ-011: Movie detail page showing metadata, cast, genres, recommendations, and "want to watch with" list [Phase 2]
- [x] REQ-012: Movie list view with search and filtering [Phase 2]
- [x] REQ-013: Friend aggregate with events: FriendAdded, FriendUpdated, FriendRemoved — name and image reference only [Phase 2]
- [x] REQ-014: Friend list view with add/edit [Phase 2]
- [x] REQ-015: "Recommended by" and "Want to watch with" associations on Movie aggregate, manageable from movie detail page [Phase 2]

### Phase 3: Journal + Content Blocks

- [x] REQ-016: Record a WatchSession for a Movie with date, optional duration, and friends present [Phase 3]
- [x] REQ-017: View watch history for a Movie (list of sessions with dates and friends) [Phase 3]
- [x] REQ-018: Content blocks (TextBlock, ImageBlock, LinkBlock) attachable to a Movie [Phase 3]
- [x] REQ-019: Content blocks attachable to a WatchSession [Phase 3]
- [x] REQ-020: Image upload and storage on local filesystem for ImageBlocks [Phase 3]
- [x] REQ-021: Inline content block editor (add, edit, reorder, delete blocks) [Phase 3]

### Phase 4: Curation + Dashboards + Admin

- [x] REQ-022: Create a Catalog with name, description, and sorted/unsorted flag [Phase 4]
- [x] REQ-023: Add Entries (movies) to a Catalog with optional position and per-item note [Phase 4]
- [x] REQ-024: Catalog detail view showing entries in order with notes [Phase 4]
- [x] REQ-025: Catalog list view [Phase 4]
- [x] REQ-026: Main Dashboard — overview of recent activity, recently added movies, and quick stats (movies, friends, catalogs, watch time) [Phase 4]
- [x] REQ-027: Dashboard — stats (total watched, watch time, catalog count) [Phase 4]
- [x] REQ-028: Event Store Browser — view and search events by stream, type, and date [Phase 4]

### Phase 5: Design System (Style Guide)

- [x] REQ-029: Style Guide page at `/styleguide` route — hidden from navigation, not discoverable, accessible only by direct URL [Phase 5]
- [x] REQ-030: Component catalog — every reusable frontend component displayed with all used parametrizations, each with explanation of design decisions (what was chosen, what was rejected, and why) [Phase 5]
- [x] REQ-031: Design token documentation — typography (Oswald/Inter usage), colors (theme palette), spacing, shapes, glassmorphism conventions, and all other design decisions with rationale for each choice [Phase 5]
- [x] REQ-032: Design system as single source of truth — extract component definitions and design tokens so the Style Guide IS the canonical definition and application pages consume from it [Phase 5]
- [x] REQ-033: Skills and/or agents for Style Guide workflow — when a component or design token is changed in the Style Guide, provide guided propagation to update all usages across application pages [Phase 5]

### Phase 6: TV Series

- [x] REQ-100: TV Series aggregate with Series > Season > Episode hierarchy, fat TMDB import (series + all seasons + all episodes), SeriesStatus (Returning/Ended/Canceled/InProduction/Planned), series-level cast (non-event-sourced CastStore), local image storage (posters, backdrops, episode stills) [Phase 6]
- [x] REQ-101: Named rewatch sessions — default personal session (auto-created on import) + named rewatch sessions with friends; episode progress tracked per session; overall progress = union across all sessions [Phase 6]
- [x] REQ-101a: Batch episode operations — mark season watched, mark episodes watched up to S##E##; essential for importing already-seen series [Phase 6]
- [x] REQ-101b: Social features on Series — recommended_by, want_to_watch_with, personal_rating, content_blocks (full parity with Movies) [Phase 6]
- [x] REQ-102: TV Series detail page with hero backdrop, tab bar (Overview, Cast & Crew, Episodes), season sidebar with progress counts, episode list with thumbnails/watch status/green checkmarks, "next up" highlight, rewatch session selector dropdown [Phase 6]
- [x] REQ-102a: TV Series list page with search/filtering, poster grid/list, season count badges, "next up" preview, series status badges [Phase 6]
- [x] REQ-102b: Polymorphic catalog entries — catalogs can hold both Movies and Series via MediaType discriminator [Phase 6]
- [ ] REQ-102c: TV Series Dashboard with episode progress, next-to-watch across all series, watch time stats [Phase 6]

## v2 (Future)

- [ ] REQ-103: Game support with manual creation and metadata
- [ ] REQ-104: Steam integration — import game library and play hours
- [ ] REQ-105: HowLongToBeat integration — average completion time per game
- [ ] REQ-106: PlaySession tracking for games with friends
- [ ] REQ-107: Game journal pages — rich content blocks with screenshots, stories, and links
- [ ] REQ-108: Games Dashboard with play sessions, completion progress vs. average
- [ ] REQ-109: Book support with manual creation
- [ ] REQ-110: Goodreads integration — import book library and reading status
- [ ] REQ-121: Audible integration — import audiobook library and listening progress
- [ ] REQ-111: ReadSession tracking for books (date, pages/chapters)
- [ ] REQ-112: Books Dashboard with reading stats
- [ ] REQ-113: Trakt.tv sync — import/export watch history
- [ ] REQ-114: Jellyfin sync — import watch history from local media server
- [ ] REQ-115: Rotten Tomatoes score display on movies
- [ ] REQ-116: Trailer playback (YouTube embeds for movies, Steam trailers for games)
- [ ] REQ-117: SQLite table inspector in Administration module
- [ ] REQ-118: Yearly intelligence reports (total watch/play/read time)
- [ ] REQ-119: Friend-level intelligence (media consumed per friend)
- [ ] REQ-120: Planned future sessions with friends

## Out of Scope

- Multi-user accounts or authentication — single-user app, no login required
- Social features (sharing, public profiles) — personal use only
- Native mobile apps — web only, mobile-first responsive design
- Cloud hosting or managed services — self-hosted Docker only
- Real-time collaboration — single user, no concurrent editing
- Serena MCP — revisit once codebase is large enough to benefit from LSP-based navigation
