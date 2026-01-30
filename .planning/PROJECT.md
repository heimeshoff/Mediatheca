# Mediatheca

> Your personal media library for tracking movies, series, games, and books.

## Vision

Mediatheca is a **personal media diary and intelligence hub** — not just a catalog, but a social and temporal record of media experiences. It tracks *what* you consumed, *when*, *with whom*, and captures your thoughts, screenshots, and stories along the way.

Designed for a single user, deployed as a Docker container, accessed via a mobile-first responsive web app.

## Problem Statement

- Losing track of what you've watched, played, and read across multiple platforms
- No unified way to record *who* you experienced media with and *when*
- Wanting rich personal commentary (notes, screenshots, links) attached to media at every level (movie, season, episode, game session)
- Needing curated collections organized by people, themes, or custom concepts
- Wanting aggregated intelligence: total watch time, play hours vs. average completion, yearly summaries

## Media Types

| Type      | Granularity                          | External Sources                       |
|-----------|--------------------------------------|----------------------------------------|
| Movie     | Single item                          | TMDB, Rotten Tomatoes, Trakt.tv, Jellyfin, YouTube (trailers) |
| TV Series | Series > Season > Episode            | TMDB, Trakt.tv, Jellyfin              |
| Game      | Single item                          | Steam, HowLongToBeat                  |
| Book      | Single item                          | Goodreads                              |

## Bounded Contexts

### 1. Catalog

Media identity and metadata. The canonical representation of a movie, series, season, episode, game, or book.

- **MediaItem**: The root identity for any piece of media
- **Movie**: A film with title, year, genres, Rotten Tomatoes score, trailer link
- **Series**: A TV series containing Seasons
- **Season**: A season within a Series, containing Episodes
- **Episode**: An individual episode within a Season
- **Game**: A game with title, platform, trailer link, average completion time (HowLongToBeat)
- **Book**: A book with title, author, page count

Responsible for importing metadata from external sources (TMDB, Steam store, Goodreads).

### 2. Journal

Personal experience and sessions. The temporal, personal record — the "diary" of Mediatheca.

- **WatchSession**: A specific instance of watching a movie or episode(s), with date, duration, and friends present
- **PlaySession**: A specific instance of playing a game, with date, duration, and friends present
- **ReadSession**: A specific instance of reading a book, with date, pages/chapters covered
- **ContentBlock**: A flexible Notion-style block attached to any media item, session, season, episode, or game. Types:
  - **TextBlock**: Rich text notes, stories, commentary
  - **ImageBlock**: Screenshots, photos (stored on local filesystem)
  - **LinkBlock**: External URLs (articles, wikis, guides, etc.)

Content blocks can be attached at any level: movie, series, season, episode, game, book, or individual session.

### 3. Friends

People in your media life. A first-class bounded context because friends participate across many parts of the system.

- **Friend**: A person with a name and an image
- Associations with other contexts:
  - **Journal**: Watched with, played with, read with (session participants)
  - **Curation**: Collections organized by friend ("Watch with Maria")
  - **Catalog**: Recommended by a friend
  - **Journal (planned)**: Future sessions planned with a friend

### 4. Curation

Collections and lists. Organizing media items into meaningful groupings.

- **Collection**: A named grouping of entries with a description/note
  - Can be **sorted** (with a consumption order) or **unsorted**
  - Organized by friend, theme, or any custom concept (e.g., "Horror Movies", "Watch with Maria", "2024 Favorites")
- **Entry**: A media item placed in a collection
  - Has an optional **position** (if collection is sorted)
  - Has a **per-item note** explaining its inclusion or position
- An Entry can reference any media granularity: movie, series, season, episode, game, or book

### 5. Intelligence

Stats, aggregations, and dashboards. Read models and projections computed from events.

- Total watch time (per year, lifetime)
- Total play time vs. average completion time (from HowLongToBeat)
- Play session count per game
- Media consumed per friend
- Progress tracking (episodes watched, books read)

**Dashboards:**

1. **Main Dashboard** (landing page): Cross-media overview — next episode to watch, next movie, current games, book queue. A "what's next" view.
2. **Movies Dashboard**: Movie-specific views, recent watches, stats
3. **TV Series Dashboard**: Series progress, episode tracking, next to watch
4. **Games Dashboard**: Current games, play sessions, completion progress vs. average
5. **Books Dashboard**: Reading list, progress, stats

### 6. Integration

External system adapters. The bridge between Mediatheca and third-party services.

- **TMDB**: Import movie and TV series metadata
- **Trakt.tv**: Sync watch history
- **Jellyfin**: Sync watch history from local media server
- **Steam**: Import play hours and game library
- **Goodreads**: Import book library and reading status
- **Rotten Tomatoes**: Fetch critic/audience scores
- **HowLongToBeat**: Fetch average game completion times
- **YouTube / Trailers**: Link to trailers for movies and games

### 7. Administration

System maintenance and observability.

- **Event Store Browser**: View, search, and inspect events in the append-only event store
- **SQLite Inspector**: Browse read model tables, projections, and their current state
- Single SQLite database file for all structured data (events + read models)

## Architecture

### Event Sourcing

- **Append-only event store** in SQLite as the single source of truth
- **Projections / Read Models** in the same SQLite database
- CQRS pattern: commands produce events, events are projected into read models for queries

### Tech Stack

| Layer       | Technology                              |
|-------------|----------------------------------------|
| Frontend    | F# with Fable, MVU architecture, Feliz |
| UI          | TailwindCSS + DaisyUI                  |
| Backend     | F# with Giraffe                        |
| RPC         | Fable.Remoting (type-safe)             |
| Database    | SQLite (single file)                   |
| Images      | Local filesystem (Docker volume)       |
| Testing     | Expecto                                |
| Dev Server  | Vite + concurrently                    |
| Deployment  | Docker container on Linux              |
| Development | Windows                                |

### Design Principles

- **Domain-Driven Design**: Clear bounded contexts, ubiquitous language, explicit namespaces per context
- **Event Sourcing**: All state changes captured as immutable events
- **Mobile-first**: Responsive design, beautiful on both mobile and desktop
- **Single-user**: No authentication system, personal use only
- **Self-hosted**: Docker container, single SQLite file, local filesystem for images

## Out of Scope

- Multi-user accounts or authentication
- Social features (sharing, public profiles)
- Native mobile apps (web only)
- Cloud hosting or managed services
- Real-time collaboration

## MVP Strategy

Feature details will be provided incrementally as MVPs are built. The project will be developed iteratively, with detailed specifications for each feature provided at implementation time.
