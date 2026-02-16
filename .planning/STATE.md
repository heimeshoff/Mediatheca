# Current State — Jellyfin Integration

**Last Updated:** 2026-02-16
**Branch:** gitwork3
**Current Phase:** 1 (Connection & Library Scan) — Complete
**Current Task:** Phase 1 implemented, ready for Phase 2

## Context

This worktree implements Jellyfin integration (originally REQ-114 in main project's v2 backlog). The integration follows established patterns from Steam.fs/Tmdb.fs for external API modules.

**Base state:** Main branch at Phase 7A complete (Game Catalog + Play Sessions). All prior infrastructure (event store, projections, settings, integration patterns) is available.

## Recent Progress

- **2026-02-16**: Phase 1 (Connection & Library Scan) implemented
  - `Jellyfin.fs`: Server module with config type, user auth via `/Users/AuthenticateByName`, library fetch (movies/series/episodes), Thoth.Json.Net decoders for BaseItemDto subset
  - Settings management: `jellyfin_server_url`, `jellyfin_username`, `jellyfin_password`, `jellyfin_user_id`, `jellyfin_access_token` in SettingsStore, config provider in Program.fs
  - Library scan: `scanJellyfinLibrary` endpoint fetches Jellyfin movies/series, matches by TMDB ID against `movie_detail.tmdb_id` and `series_detail.tmdb_id`, returns matched/unmatched items
  - Settings UI: Jellyfin integration card with server URL, username/password inputs, "Test & Save" button (authenticates and persists token)
  - Shared types: `JellyfinItem`, `JellyfinMatchedItem`, `JellyfinScanResult`, `JellyfinItemType`
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

1. **REQ-304**: Movie watch sync (additive-only, dedup by date)
2. **REQ-305**: Series episode watch sync (additive-only)
3. **REQ-306**: Jellyfin sync API endpoints (scan preview + import)
4. **REQ-307**: Jellyfin sync UI (scan preview, confirm, result summary)
