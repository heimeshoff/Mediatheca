# Roadmap — Jellyfin Integration

> Branch: gitwork3 | Builds on top of main (Phase 7A complete)

## Core Principle: Additive-Only

All sync operations only *add* watch data to Mediatheca. Existing watch sessions and episode progress are never removed, overwritten, or downgraded by Jellyfin data.

## Phases

| Phase | Name | Status | Requirements |
|-------|------|--------|--------------|
| 1 | Connection & Library Scan | :white_check_mark: Done | REQ-300, REQ-301, REQ-302, REQ-303 |
| 2 | Additive Watch History Sync | Planned | REQ-304, REQ-305, REQ-306, REQ-307 |

### Phase 1: Connection & Library Scan

Deliverable: Users can configure their Jellyfin server in Settings, test the connection, and scan the Jellyfin library to see which items match existing Mediatheca movies and series (via TMDB ID).

- `Jellyfin.fs` server module: config type, user authentication (`/Users/AuthenticateByName`), library items fetch (`/Users/{userId}/Items`), Thoth.Json.Net decoders for BaseItemDto subset
- Settings keys in SettingsStore: `jellyfin_server_url`, `jellyfin_username`, `jellyfin_password`, `jellyfin_user_id`, `jellyfin_access_token`
- Config provider function in Program.fs (`getJellyfinConfig`)
- IMediathecaApi endpoints: `getJellyfinServerUrl`, `setJellyfinServerUrl`, `getJellyfinUsername`, `setJellyfinCredentials`, `testJellyfinConnection`
- Library scan: fetch Movies + Series from Jellyfin, extract TMDB IDs from `ProviderIds`, match against `movie_detail.tmdb_id` and `series_detail.tmdb_id`
- Shared types: `JellyfinItem` (name, year, type, tmdbId, jellyfinId, played, playCount, lastPlayedDate), `JellyfinScanResult` (matched movies/series with Mediatheca slugs, unmatched items)
- Settings page: Jellyfin integration card with server URL, username/password, Test Connection, Save, status badge, webhook URL display

### Phase 2: Additive Watch History Sync

Deliverable: Users can import watch history from Jellyfin into Mediatheca. Played movies get watch sessions, played episodes get marked as watched. **Strictly additive** — existing Mediatheca data is never modified or removed.

- Movie sync: for each matched movie where Jellyfin `Played=true` → check if Mediatheca has a watch session on the same date as `LastPlayedDate`. If no session on that date → create new `Watch_session_recorded` event. If session already exists on that date → skip (deduplicate by date). Never remove existing sessions
- Series sync: fetch per-episode status from Jellyfin `/Shows/{seriesId}/Episodes`, match by (seasonNumber, episodeNumber), emit `Episode_watched` for episodes where Jellyfin `Played=true` AND not yet watched in Mediatheca's default session. Never unwatch already-watched episodes
- API: `scanJellyfinLibrary` (dry-run preview of what will be *added*), `importJellyfinWatchHistory` (execute additive sync)
- Shared types: `JellyfinImportResult` (moviesAdded, episodesAdded, itemsSkipped, errors)
- UI: scan preview table showing additions only, confirm button, result summary

## Key Technical Notes

### Additive-Only Rules (Enforced Everywhere)

| Scenario | Action |
|----------|--------|
| Jellyfin `Played=true`, no Mediatheca session on that date | Add watch session |
| Jellyfin `Played=true`, Mediatheca already has session on same date | Skip (deduplicate) |
| Jellyfin `Played=false`, Mediatheca has watch sessions | **No action** (never remove) |
| Episode watched in Jellyfin, not in Mediatheca | Mark watched |
| Episode watched in Mediatheca, not in Jellyfin | **No action** (never unwatch) |

### Jellyfin API Details

- **Auth**: POST `/Users/AuthenticateByName` with `{"Username", "Pw"}` → `AccessToken` + `User.Id`
- **Auth header**: `Authorization: MediaBrowser Client="Mediatheca", Device="Server", DeviceId="mediatheca-server", Version="1.0", Token={token}`
- **Library items**: GET `/Users/{userId}/Items?IncludeItemTypes=Movie,Series&Recursive=true&Fields=ProviderIds,Overview,Genres,PremiereDate&enableUserData=true`
- **Episodes**: GET `/Shows/{seriesId}/Episodes?userId={userId}&Fields=ProviderIds&enableUserData=true`
- **TMDB matching**: `ProviderIds.Tmdb` (API) / `Provider_tmdb` (webhook) → Mediatheca's `tmdb_id`

### UserData Fields (per item)

| Field | Type | Meaning |
|-------|------|---------|
| `Played` | bool | Fully watched |
| `PlayCount` | int | Number of complete watches |
| `LastPlayedDate` | string (ISO) | Most recent play date |
| `PlaybackPositionTicks` | long | Resume position (10M ticks/sec) |

### Matching Strategy

1. Fetch Movies/Series from Jellyfin with `ProviderIds`
2. Extract `ProviderIds.Tmdb` (TMDB ID) from each item
3. Query Mediatheca's `movie_detail` / `series_detail` tables by `tmdb_id`
4. Matched items → candidates for watch sync
5. Unmatched items → reported only (auto-import is v2)

### Limitations

- Jellyfin stores only last played date, not full history. PlayCount > 1 means multiple watches but we only know the date of the most recent one.
- For movies: we create a watch session for `LastPlayedDate` if no session already exists on that date. Running sync multiple times is safe — same date won't be added twice.
