# Current State — Jellyfin Integration

**Last Updated:** 2026-02-16
**Branch:** gitwork3
**Current Phase:** 2 (Additive Watch History Sync) — Complete
**Current Task:** All phases complete

## Context

This worktree implements Jellyfin integration (originally REQ-114 in main project's v2 backlog). The integration follows established patterns from Steam.fs/Tmdb.fs for external API modules.

**Base state:** Main branch at Phase 7A complete (Game Catalog + Play Sessions). All prior infrastructure (event store, projections, settings, integration patterns) is available.

## Recent Progress

- **2026-02-16**: Collapsible sections on Friend Detail page
  - Added chevron up/down indicators to Recommended, Pending, and Watched Together section headers
  - Clicking a section header toggles collapse/expand
  - Entry count displayed inline next to section title (not below)
  - State tracked via `CollapsedSections: Set<string>` in Model

- **2026-02-16**: Phase 1 (Connection & Library Scan) implemented
  - `Jellyfin.fs`: Server module with config type, user auth via `/Users/AuthenticateByName`, library fetch (movies/series/episodes), Thoth.Json.Net decoders for BaseItemDto subset
  - Settings management: `jellyfin_server_url`, `jellyfin_username`, `jellyfin_password`, `jellyfin_user_id`, `jellyfin_access_token` in SettingsStore, config provider in Program.fs
  - Library scan: `scanJellyfinLibrary` endpoint fetches Jellyfin movies/series, matches by TMDB ID against `movie_detail.tmdb_id` and `series_detail.tmdb_id`, returns matched/unmatched items
  - Settings UI: Jellyfin integration card with server URL, username/password inputs, "Test & Save" button (authenticates and persists token)
  - Shared types: `JellyfinItem`, `JellyfinMatchedItem`, `JellyfinScanResult`, `JellyfinItemType`

- **2026-02-16**: Phase 2 (Additive Watch History Sync) implemented
  - `JellyfinImportResult` shared type with MoviesAdded, EpisodesAdded, ItemsSkipped, Errors
  - `importJellyfinWatchHistory` API endpoint in IMediathecaApi
  - Movie watch sync (REQ-304): For each matched movie where Jellyfin `Played=true`, creates `Watch_session_recorded` event using `LastPlayedDate`, deduplicates by date against `watch_sessions` table
  - Series episode sync (REQ-305): For each matched series, fetches episodes from Jellyfin, matches by (seasonNumber, episodeNumber), emits `Episode_watched` to default rewatch session for unwatched episodes
  - Sync API (REQ-306): `scanJellyfinLibrary` (preview) + `importJellyfinWatchHistory` (execute) endpoints
  - Sync UI (REQ-307): "Scan Library" button shows matched/unmatched counts with preview list, "Import Watch History" button with result summary showing movies added, episodes marked, items skipped, errors
  - Build passes, 184 tests pass (2 pre-existing errors unrelated to Jellyfin)

## Active Decisions

- **Additive-only sync**: Watch sessions and episode progress are only ever *added* to Mediatheca, never removed. Existing Mediatheca data is always the authoritative record. This is a core principle, not negotiable.
- **Authentication method**: Username/password auth via `/Users/AuthenticateByName` — API key auth has known issues with UserData not being returned in some Jellyfin versions
- **Matching strategy**: TMDB ID matching — Jellyfin `ProviderIds.Tmdb` maps to Mediatheca's `tmdb_id`
- **Watch session dedup by date**: Create a session for `LastPlayedDate` only if no session already exists on that date. Existing sessions on other dates are preserved. Re-running sync is idempotent
- **Episode sync**: Per-episode `Played` status synced to default rewatch session. Never unwatch episodes
- **No auto-import in v1**: Only sync watch status for items already in Mediatheca library

## Blockers

- (none)

## Next Actions

- All v1 Jellyfin integration requirements complete (REQ-300 through REQ-307)
- v2 features (REQ-308, 320-323) available for future work
