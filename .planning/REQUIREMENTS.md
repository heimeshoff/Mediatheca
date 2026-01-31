# Requirements

## v1 (MVP) — Movies-Only Personal Media Tracker

### Phase 1: Skeleton

- [x] REQ-001: F# solution structure with Shared, Server, and Client projects using Fable, Giraffe, and Fable.Remoting [Phase 1]
- [x] REQ-002: Vite + concurrently dev pipeline for simultaneous frontend and backend development [Phase 1]
- [x] REQ-003: TailwindCSS + DaisyUI integration with Fable/Feliz frontend [Phase 1]
- [x] REQ-004: SQLite-based append-only event store with event serialization and stream support [Phase 1]
- [x] REQ-005: Projection engine that replays events into SQLite read model tables [Phase 1]
- [ ] REQ-006: App shell with mobile-first responsive layout and navigation (sidebar/bottom nav) [Phase 1]
- [x] REQ-007: Dockerfile for production deployment on Linux [Phase 1]
- [x] REQ-008: Expecto test project with build integration [Phase 1]

### Phase 2: Catalog + Friends

- [ ] REQ-009: Create and edit a Movie with title, year, and genres [Phase 2]
- [ ] REQ-010: Import movie metadata from TMDB API (search + import) [Phase 2]
- [ ] REQ-011: Movie detail page showing metadata, trailer link, and associated data [Phase 2]
- [ ] REQ-012: Movie list view with search and filtering [Phase 2]
- [ ] REQ-013: Create and edit a Friend with name and profile image [Phase 2]
- [ ] REQ-014: Friend list view [Phase 2]
- [ ] REQ-015: Associate a Friend as "recommended by" on a Movie [Phase 2]

### Phase 3: Journal + Content Blocks

- [ ] REQ-016: Record a WatchSession for a Movie with date, optional duration, and friends present [Phase 3]
- [ ] REQ-017: View watch history for a Movie (list of sessions with dates and friends) [Phase 3]
- [ ] REQ-018: Content blocks (TextBlock, ImageBlock, LinkBlock) attachable to a Movie [Phase 3]
- [ ] REQ-019: Content blocks attachable to a WatchSession [Phase 3]
- [ ] REQ-020: Image upload and storage on local filesystem for ImageBlocks [Phase 3]
- [ ] REQ-021: Inline content block editor (add, edit, reorder, delete blocks) [Phase 3]

### Phase 4: Curation + Dashboards + Admin

- [ ] REQ-022: Create a Collection with name, description, and sorted/unsorted flag [Phase 4]
- [ ] REQ-023: Add Entries (movies) to a Collection with optional position and per-item note [Phase 4]
- [ ] REQ-024: Collection detail view showing entries in order with notes [Phase 4]
- [ ] REQ-025: Collection list view [Phase 4]
- [ ] REQ-026: Main Dashboard — overview of recent watch sessions, recently added movies, and quick stats [Phase 4]
- [ ] REQ-027: Movies Dashboard — movie-specific stats (total watched, watch time, top friends watched with) [Phase 4]
- [ ] REQ-028: Event Store Browser — view and search events by stream, type, and date [Phase 4]

## v2 (Future)

- [ ] REQ-100: TV Series support (Series > Season > Episode hierarchy) with TMDB import
- [ ] REQ-101: Episode-level watch session tracking with per-session friend tracking
- [ ] REQ-102: TV Series Dashboard with episode progress and next-to-watch
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
