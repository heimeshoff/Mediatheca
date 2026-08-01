# Context map

Mediatheca is a personal media library + diary + intelligence hub built on event sourcing. The contexts below carve the system along clear language and lifecycle boundaries. Each owns its own event stream; cross-context coordination happens via published events (downstream contexts subscribe to upstream events when projecting).

## Contexts

### Movies
- **Purpose:** Owns the Movie aggregate — a film as a curated library entry with its metadata, posters, ratings, and the watch sessions tied to it.
- **Core language:** Movie, watch session, In Focus, want to watch with, recommendation, personal rating.
- **Classification:** core
- **Key actors:** Single user (library owner).

### Series
- **Purpose:** Owns the Series aggregate — TV shows with seasons, episodes, rewatch sessions, and episode-level watch state.
- **Core language:** Series, season, episode, rewatch session, default rewatch, episode watched, In Focus, Next Up.
- **Classification:** core
- **Key actors:** Single user.

### Games
- **Purpose:** Owns the Game aggregate — video games with lifecycle status, play modes, family ownership, and Steam/HLTB metadata.
- **Core language:** Game, status (Backlog → InFocus → Retired / Abandoned / Dismissed), play session, play time, family owner, played with, HLTB hours.
- **Classification:** core
- **Key actors:** Single user.

### Journal
- **Purpose:** The cross-media diary. Surfaces *when* media was experienced and *with whom*. Aggregates watch sessions (Movies), episode-watched events (Series), and play sessions (Games) into a unified activity timeline that powers heatmaps, "recently watched", and yearly intelligence.
- **Core language:** Activity, session, watch session, play session, episode watched, watched-with, played-with, recent activity, activity day.
- **Classification:** core
- **Key actors:** Single user.
- **Notes:** Read-side, projection-heavy. The write authority for sessions stays inside Movies / Series / Games; Journal consumes their events.

### Friends
- **Purpose:** People you experience media with. Lightweight profile registry — name, image, crop settings — referenced by slug from every other BC.
- **Core language:** Friend, slug, crop settings, watched-with, played-with, recommended by.
- **Classification:** supporting
- **Key actors:** Single user.

### Curation
- **Purpose:** User-created collections that group media across types. Catalogs (ordered lists of MovieRef / SeriesRef / GameRef) and ContentBlocks (free-form notes / sections that decorate a catalog or detail page).
- **Core language:** Catalog, catalog entry, content block, position, reorder.
- **Classification:** supporting
- **Key actors:** Single user.

### Intelligence
- **Purpose:** Derived insights over the journal and library — stats, comparisons (e.g. play time vs HLTB average), monthly breakdowns, dashboard cross-media stats, activity heatmaps. Read-only synthesis; no aggregates of its own.
- **Core language:** Stats, breakdown, heatmap, comparison, monthly play time, watched-with stats, person stats.
- **Classification:** core
- **Key actors:** Single user.
- **Notes:** Mostly projections that read from Movies / Series / Games / Journal event streams. Yearly intelligence reports and friend-level intelligence are v2.

### Integration
- **Purpose:** Adapters to external systems — TMDB, RAWG, Steam, HowLongToBeat, Jellyfin, Cinemarco. Translates external shapes into commands the core BCs accept; scheduled sync jobs pull external state on a cadence.
- **Core language:** Import, sync, refresh, scheduled job, external id (TMDB id, RAWG id, Steam appId), adapter.
- **Classification:** generic
- **Key actors:** External services + single user (triggering manual syncs).

### Administration
- **Purpose:** Settings, event store / projection administration, event browser, image storage. The plumbing that keeps the single-user app running.
- **Core language:** Setting, event, projection, image ref, slug, event browser.
- **Classification:** generic
- **Key actors:** Single user (operator role).

### Infrastructure
- **Purpose:** Globally-true technical concerns — deployment topology (Docker container, Windows/macOS desktop shells), hosting, packaging, runtime configuration, base tooling. The standing home for tech decisions that apply to the system as a whole.
- **Core language:** Deployment target, desktop shell, self-contained publish, data dir, loopback binding.
- **Classification:** generic
- **Key actors:** Single user (developer/operator).

### Design system
- **Purpose:** Cross-cutting visual language — typography, color tokens, paper-overlay rules, Feliz/DaisyUI component patterns, the in-app StyleGuide page. Gates frontend work in every BC.
- **Core language:** Token, theme (dim), paper overlay, velvet card, surface, design-system component.
- **Classification:** supporting
- **Key actors:** Single user (developer).
- **Notes:** All frontend tasks in any BC `depends_on` the styleguide.

## Relationships

- **Friends → Movies / Series / Games / Curation** (upstream, published language).
  Friends emits `Friend_added` / `Friend_updated`. Downstream BCs reference friends by `slug` and copy `name` / `imageRef` into their own projections. Friends never call into the media BCs.

- **Movies / Series / Games → Journal** (upstream, published events).
  Watch sessions, episode-watched events, and play session events are published by the three media BCs. Journal's projections subscribe and assemble the cross-media activity timeline. Journal does not write commands back.

- **Movies / Series / Games + Journal → Intelligence** (upstream).
  Intelligence projections fold all upstream event streams into derived stats. No coupling back upstream.

- **Integration → Movies / Series / Games** (upstream → downstream, anticorruption layer).
  External APIs (TMDB / RAWG / Steam / HLTB / Jellyfin) are wrapped by adapters in Integration. Adapters write through **two output channels** (ADR-0043): commands (`Add_movie`, `Add_game`, `Set_hltb_hours`, `Refresh_series_from_tmdb`, …) for facts that must be replayable domain history, and direct cache writes into projection columns for re-derivable third-party metadata (ratings, artwork, episode/season detail) that a refresh can always re-fetch. Core BCs never see external shapes directly.

- **Curation → Movies / Series / Games** (downstream, conformist).
  Catalogs reference media by `(MediaType, mediaId)` and rely on whatever those BCs expose. Curation conforms to the core BCs' published refs; it doesn't push language back.

- **Administration ↔ everything** (shared kernel: the event store + image store + the metadata cache).
  The append-only event store, the image store, and the metadata cache (projection columns holding re-derivable third-party description — ratings, artwork, episode/season detail; ADR-0043) are infrastructure consumed by every BC. Administration owns the operational surface (event browser, settings, projection rebuild paths).

- **Design system → every frontend-bearing BC** (open host / shared kernel).
  Provides tokens, components, and patterns. Every BC's frontend conforms.

## Notes on classification

- **Core:** Movies, Series, Games, Journal, Intelligence — these are the reason the app exists. Differentiation lives here.
- **Supporting:** Friends, Curation, Design system — necessary, custom-built, but not the heart of the value proposition.
- **Generic:** Integration, Administration, Infrastructure — boring plumbing where boring choices are correct.
