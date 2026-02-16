# Codebase Snapshot (as of 2026-02-15)

## Domain Model

6 aggregates, all using Event Sourcing + CQRS with `NotCreated → Active → Removed` state machines (except ContentBlocks which is always-active).

| Aggregate | Stream | Events | Key Validation | Cross-Agg Deps |
|-----------|--------|--------|----------------|----------------|
| **Movies** | `Movie-{slug}` | 15 events | Idempotency, session existence, dupes | References Friend slugs |
| **Series** | `Series-{slug}` | 16+ events | Session/season existence, idempotency | References Friend slugs |
| **Games** | `Game-{slug}` | 17 events | Idempotency, status guards | References Friend slugs |
| **Friends** | `Friend-{slug}` | 3 events | Simple state guards | Referenced by Movies, Series, Games |
| **Catalogs** | `Catalog-{slug}` | 7 events | Unique entries per catalog, auto-position | References media slugs (polymorphic) |
| **ContentBlocks** | `ContentBlocks-{slug}` | 5 events | Session-scoped positioning | Keyed by parent slug |

Key behaviors:
- Watch session recording auto-removes friends from `Want_to_watch_with`
- Friend removal cascades cleanup across movie/series/game JSON arrays
- Decider pattern: pure `decide: State -> Command -> Result<Event list, string>`
- Games: `played_with` is a flat set (not session-based); no play session events in aggregate
- Series: default rewatch session auto-created on import; episode progress per session

## API Surface

137 methods on `IMediathecaApi` via Fable.Remoting:
- Movies: 21 methods (CRUD, social, watch sessions, content blocks)
- Series: 31 methods (CRUD, social, rewatch sessions, episode progress, content blocks)
- Games: 29 methods (CRUD, social, stores, family owners, played-with, content blocks)
- Friends: 7 methods
- Catalogs: 10 methods
- Dashboard: 3 methods (stats, recent series, activity)
- Settings: 7 methods (TMDB/RAWG keys, trailers)
- Event Store: 3 methods
- Search: 2 methods (library, TMDB)
- Import: 1 method (Cinemarco)

All DTOs in `src/Shared/Shared.fs`. Slug module provides URL-safe identifiers (`movieSlug`, `seriesSlug`, `gameSlug`, `friendSlug`, `catalogSlug`).

## Storage & Projections

### Event Store
- SQLite append-only `events` table (global_position, stream_id, stream_position, event_type, data JSON, timestamp)
- `projection_checkpoints` for incremental processing (100-event batches)
- Stream IDs: `{Type}-{slug}` convention

### Projections (6 handlers)
| Projection | Tables | Key Features |
|-----------|--------|-------------|
| **MovieProjection** | movie_list, movie_detail, watch_sessions | Dual summary/detail; JSON friend arrays |
| **SeriesProjection** | series_list, series_detail, series_seasons, series_episodes, series_rewatch_sessions, series_episode_progress | Progress recalculation; next-up computation |
| **GameProjection** | game_list, game_detail | Status tracking; JSON arrays for stores/family/social |
| **FriendProjection** | friend_list | Cascade scrubs JSON arrays on removal |
| **CatalogProjection** | catalog_list, catalog_entries | Polymorphic entries (Movie/Series/Game) |
| **ContentBlockProjection** | content_blocks | Session-scoped blocks |

### Non-Event-Sourced Stores
- **CastStore**: cast_members + movie_cast + series_cast (orphan cleanup)
- **ImageStore**: File-system operations
- **SettingsStore**: key-value table (API keys)

## Client Architecture

### Pages (10 routes)
| Page | Route | Pattern |
|------|-------|---------|
| Dashboard | `/` | Stats cards, recent items, activity timeline |
| Movie_list / Movie_detail | `/movies`, `/movies/:slug` | Grid with search; hero + two-column detail |
| Series_list / Series_detail | `/series`, `/series/:slug` | Grid with search; hero + tabs + season sidebar |
| Game_list / Game_detail | `/games`, `/games/:slug` | Grid with status filters; hero + stores/social |
| Friend_list / Friend_detail | `/friends`, `/friends/:slug` | Card grid; profile + media associations |
| Catalog_list / Catalog_detail | `/catalogs`, `/catalogs/:slug` | Catalog cards; entries with management |
| Event_browser | `/events` | Event store explorer with filters |
| Settings | `/settings` | API keys (TMDB, RAWG), Cinemarco import |
| Styleguide | `/styleguide` | Design system documentation |

### MVU Pattern
- Root Types.fs/State.fs/Views.fs delegates to child pages via Cmd.map
- Each page: Types.fs (Model + Msg), State.fs (init + update), Views.fs (render)
- SearchModal: global overlay (Ctrl+K), tabbed (Library, Movies, Series, Games)

### Components
Layout, Sidebar, BottomNav, PosterCard, ModalPanel, EntryList, SearchModal, FriendPill, ContentBlockEditor, PageContainer, Icons, DesignSystem

### Design System (DesignSystem.fs + index.css)
- Glassmorphism: glassCard, glassOverlay, glassSubtle, glassDropdown
- Typography: Oswald (headings), Inter (body)
- Theme: custom "dim" DaisyUI dark theme
- Animations: fade-in, fade-in-up, scale-in, stagger-grid

## Cross-Cutting Observations

- Games aggregate has `played_with` as a flat set but no play session events — differs from STATE.md domain model which describes full play sessions with events
- No `play_sessions` projection table — total_play_time is on game_list/game_detail but no per-session tracking
- Steam integration (REQ-208) will need: Steam Web API client, Settings entries for Steam API key + family member config, import logic to match Steam games to existing library entries
- Dashboard stats already include TotalPlayTimeMinutes and GameCount
- SeriesProjection is rebuilt on startup for data consistency
