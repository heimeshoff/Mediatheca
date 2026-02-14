# Codebase Snapshot (as of 2026-02-14)

## Domain Model

4 aggregates, all using Event Sourcing + CQRS with `NotCreated → Active → Removed` state machines (except ContentBlocks which is always-active).

| Aggregate | Stream | Events | Key Validation | Cross-Agg Deps |
|-----------|--------|--------|----------------|----------------|
| **Movies** | `Movie-{slug}` | 14 events | Idempotency, session existence, dupes | References Friend slugs |
| **Friends** | `Friend-{slug}` | 3 events | Simple state guards | Referenced by Movies |
| **Catalogs** | `Catalog-{slug}` | 7 events | Unique entries per catalog, auto-position | References Movie slugs |
| **ContentBlocks** | `ContentBlocks-{movieSlug}` | 4 events | Session-scoped positioning | Keyed by Movie slug |

Key behaviors:
- Watch session recording auto-removes friends from `Want_to_watch_with`
- Friend removal cascades cleanup across movie JSON arrays
- Decider pattern: pure `decide: State -> Command -> Result<Event list, string>`

## API Surface

47 methods on `IMediathecaApi` via Fable.Remoting:
- Movies: 11 methods (CRUD + metadata + rating)
- Movie-Friend relations: 4 methods
- Watch Sessions: 5 methods
- Content Blocks: 6 methods
- Friends: 7 methods
- Catalogs: 9 methods
- Dashboard/Admin: 5 methods

All DTOs defined in `src/Shared/Shared.fs`. Slug module provides URL-safe identifiers.

## Storage & Projections

15 tables total across EventStore (2), MovieProjection (3), FriendProjection (1), CatalogProjection (2), ContentBlockProjection (1), CastStore (2), SettingsStore (1) + filesystem (ImageStore).

Key patterns:
- JSON columns for collections (friend lists as JSON arrays)
- Dual movie tables (movie_list for search, movie_detail for full info)
- Projection checkpoint tracking for catch-up
- MovieProjection rebuilt on startup for consistency

## Client Architecture

### Pages & Routes
| Page | Route | Pattern |
|------|-------|---------|
| Dashboard | `/` | Stats cards, recent movies, activity timeline |
| Movie_list | `/movies` | Grid with search/genre filter |
| Movie_detail | `/movies/:slug` | Hero + two-column (content left, social right) |
| Friend_list | `/friends` | Card grid with add form |
| Friend_detail | `/friends/:slug` | Profile with recommended/watched movies |
| Catalog_list | `/catalogs` | Catalog cards with create form |
| Catalog_detail | `/catalogs/:slug` | Entries with management |
| Event_browser | `/events` | Event store explorer with filters |
| Settings | `/settings` | Configuration page |
| Not_found | `/not-found` | 404 |

### Reusable Components
| Component | File | Public Functions |
|-----------|------|-----------------|
| **Icons** | `Icons.fs` | `svgIcon`, `svgIconSm` + named icons (Dashboard, Movie, Friends, etc.) |
| **Layout** | `Layout.fs` | `view currentPage content` |
| **Sidebar** | `Sidebar.fs` | `view currentPage` (desktop nav, glassmorphic) |
| **BottomNav** | `BottomNav.fs` | `view currentPage` (mobile dock) |
| **PageContainer** | `PageContainer.fs` | `view title children` |
| **PosterCard** | `PosterCard.fs` | `view slug name year posterRef ratingBadge`, `thumbnail posterRef alt` |
| **ModalPanel** | `ModalPanel.fs` | `view`, `viewCustom`, `viewWithFooter` (glassmorphic overlay) |
| **SearchModal** | `SearchModal.fs` | Debounced TMDB search + library filter |
| **FriendPill** | `FriendPill.fs` | `view`, `viewWithRemove`, `viewInline` |
| **ContentBlockEditor** | `ContentBlockEditor.fs` | Block CRUD with inline editing |

### Design Tokens (Current — Implicit)
- **Typography:** Oswald (`font-display`, headings, uppercase, tracking 0.05em) / Inter (`font-sans`, body)
- **Theme:** DaisyUI "dim" dark theme — primary (cyan-green oklch 86.1%), secondary (orange oklch 73.4%), accent (magenta oklch 74.2%)
- **Glassmorphism:** `oklch(…/0.55–0.70)` bg, `backdrop-blur-[24px] saturate-[1.2]`, subtle border + inset highlight
- **Spacing:** p-4 mobile, lg:p-6 desktop; gap-2 compact, gap-3 standard; rounded-xl cards, rounded-full avatars
- **Animations:** fade-in (0.3s), fade-in-up (0.4s), scale-in (0.3s), stagger-grid (40ms cascading)
- **Shadows:** Subtle (0 4px 12px -2px), deeper on hover; oklch-based
- **Z-index:** auto (content), relative (cards), z-50 (modals/dropdowns)

## Cross-Cutting Observations
- All design tokens are currently implicit — scattered across Tailwind classes in view functions and index.css
- No centralized component library — components are reusable but design decisions are baked into each file
- Glassmorphism convention documented in CLAUDE.md but not enforced programmatically
- Typography rules (Oswald headings, Inter body) applied ad-hoc per component
- Color usage follows DaisyUI semantic classes but specific opacity/oklch values vary per usage site
