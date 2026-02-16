# Requirements — Jellyfin Integration

> Branch: gitwork3 | Topic: Jellyfin sync — import watch history from local media server

## Core Principle: Additive-Only Sync

Jellyfin integration is **strictly additive**. Watch sessions and episode progress are only ever *added* to Mediatheca, never removed. Jellyfin does not provide a complete watch history (only last-played date and play count), so existing Mediatheca data is always treated as the richer, authoritative record. No sync operation may delete, overwrite, or downgrade existing watch data.

## v1 — Jellyfin Integration

### Phase 1: Connection & Library Scan

- [x] REQ-300: Jellyfin server module (`Jellyfin.fs`) — config type (ServerUrl, Username, Password), user authentication via `/Users/AuthenticateByName`, library fetch via `/Users/{userId}/Items`, Thoth.Json.Net decoders for BaseItemDto (Name, ProductionYear, Type, RunTimeTicks, Genres, ProviderIds with TMDB ID, UserData with Played/PlayCount/LastPlayedDate, series fields). Follow Steam.fs/Tmdb.fs module pattern. [Phase 1]
- [x] REQ-301: Jellyfin settings management — settings keys (`jellyfin_server_url`, `jellyfin_username`, `jellyfin_password`, `jellyfin_user_id`, `jellyfin_access_token`), config provider in Program.fs, get/set/test API endpoints on IMediathecaApi. Test connection = authenticate + fetch user info. [Phase 1]
- [x] REQ-302: Jellyfin library scan — fetch all Movies and Series from Jellyfin, match to existing Mediatheca items by TMDB ID (from Jellyfin's `ProviderIds.Tmdb`). Return matched/unmatched item lists with Jellyfin metadata preview. Shared types: `JellyfinItem`, `JellyfinScanResult`. [Phase 1]
- [x] REQ-303: Settings UI — Jellyfin integration card in Settings page following existing `integrationCard` pattern. Server URL input, username/password inputs, Test Connection button, Save button, connection status badge. [Phase 1]

### Phase 2: Additive Watch History Sync

- [ ] REQ-304: Movie watch sync (additive-only) — for matched movies where Jellyfin `Played=true`: check if Mediatheca already has a watch session on the same date as `LastPlayedDate`. If no session exists for that date, create a new `Watch_session_recorded` event. If a session already exists on that date, skip (avoid duplicates). Never remove existing sessions. [Phase 2]
- [ ] REQ-305: Series episode watch sync (additive-only) — for matched series, fetch episodes from Jellyfin (`/Shows/{seriesId}/Episodes`), match by season/episode number. For episodes where Jellyfin `Played=true` AND not yet watched in Mediatheca's default rewatch session, emit `Episode_watched` events using `LastPlayedDate`. Never unwatch episodes that Mediatheca already has marked as watched. [Phase 2]
- [ ] REQ-306: Jellyfin sync API endpoints — `scanJellyfinLibrary: unit -> Async<Result<JellyfinScanResult, string>>` (preview what will be added), `importJellyfinWatchHistory: unit -> Async<Result<JellyfinImportResult, string>>` (execute additive sync). Import result includes movies synced, episodes synced, items skipped (already watched / no match). [Phase 2]
- [ ] REQ-307: Jellyfin sync UI — scan button on Settings card, preview table showing what will be *added* (never removed), confirm import action, result summary (added/skipped/errors). [Phase 2]

## v2 (Future)

- [ ] REQ-308: Jellyfin webhook receiver & live playback progress — real-time playback events via jellyfin-plugin-webhook, progress bar on posters, auto-create watch sessions on completion (requires push-based client update, not polling)
- [ ] REQ-320: Auto-import unmatched items — when Jellyfin has movies/series not in Mediatheca, offer to import them via TMDB using the Jellyfin-provided TMDB ID
- [ ] REQ-321: Jellyfin image fallback — use Jellyfin server as image source when TMDB images are unavailable
- [ ] REQ-322: Two-way sync — mark items watched in Mediatheca → mark watched in Jellyfin via `/Users/{userId}/PlayedItems/{itemId}`
- [ ] REQ-323: Progress bar on movie/episode detail page — show resume position, "continue watching" state, time remaining

## Out of Scope

- Jellyfin playback control from Mediatheca (not a media player)
- Multi-user Jellyfin support (Mediatheca is single-user)
- Jellyfin music/audiobook library sync (separate media types)
- Removing watch sessions based on Jellyfin state (violates additive-only principle)
