# Jellyfin API Reference — Mediatheca Integration

Technical reference for Jellyfin integration. Covers authentication, library access, watch history, and TMDB ID matching.

## Authentication

### User Authentication (Recommended)

**Endpoint:** `POST {serverUrl}/Users/AuthenticateByName`

**Headers:**
```
Content-Type: application/json
Authorization: MediaBrowser Client="Mediatheca", Device="Server", DeviceId="mediatheca-server", Version="1.0"
```

**Request Body:**
```json
{
  "Username": "admin",
  "Pw": "password"
}
```

**Response:**
```json
{
  "User": {
    "Name": "admin",
    "Id": "a1b2c3d4e5f6...",
    "HasPassword": true,
    "HasConfiguredPassword": true,
    "Policy": { ... }
  },
  "AccessToken": "abc123def456...",
  "ServerId": "xyz789..."
}
```

**Subsequent requests** include the token in the Authorization header:
```
Authorization: MediaBrowser Client="Mediatheca", Device="Server", DeviceId="mediatheca-server", Version="1.0", Token=abc123def456
```

Alternative: `X-Emby-Token: abc123def456` header (simpler, works for most endpoints).

### API Key Auth (Not Recommended for UserData)

API keys can be generated in Jellyfin Dashboard > API Keys. However, some Jellyfin versions don't return `UserData` (watch history) when using API key auth. User auth is more reliable.

---

## Library Endpoints

### Get User Views (Libraries)

**Endpoint:** `GET {serverUrl}/Users/{userId}/Views`

Returns the user's library sections (Movies, TV Shows, Music, etc.).

```json
{
  "Items": [
    {
      "Name": "Movies",
      "Id": "f137a2dd21bbc1b99aa5...",
      "Type": "CollectionFolder",
      "CollectionType": "movies"
    },
    {
      "Name": "TV Shows",
      "Id": "767d2b71ff77...",
      "Type": "CollectionFolder",
      "CollectionType": "tvshows"
    }
  ]
}
```

### Get Library Items

**Endpoint:** `GET {serverUrl}/Users/{userId}/Items`

**Key Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `IncludeItemTypes` | string | Comma-separated: `Movie`, `Series`, `Episode` |
| `Recursive` | bool | `true` to include items in subfolders |
| `Fields` | string | Comma-separated extra fields: `ProviderIds,Overview,Genres,DateCreated,PremiereDate,Path` |
| `enableUserData` | bool | `true` to include watch status (CRITICAL) |
| `Filters` | string | `IsPlayed` for watched items, `IsUnplayed` for unwatched |
| `SortBy` | string | `SortName`, `DatePlayed`, `DateCreated`, `PremiereDate` |
| `SortOrder` | string | `Ascending` or `Descending` |
| `ParentId` | string | Filter to a specific library view |
| `Limit` | int | Max items per page |
| `StartIndex` | int | Pagination offset |

**Example — Fetch all movies with watch status:**
```
GET /Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds,Overview,Genres,PremiereDate&enableUserData=true
```

**Example — Fetch all series:**
```
GET /Users/{userId}/Items?IncludeItemTypes=Series&Recursive=true&Fields=ProviderIds,Overview,Genres,PremiereDate&enableUserData=true
```

### Get Series Episodes

**Endpoint:** `GET {serverUrl}/Shows/{seriesId}/Episodes`

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `userId` | string | Required for UserData |
| `seasonId` | string | Optional: filter to specific season |
| `Fields` | string | `ProviderIds,Overview` |
| `enableUserData` | bool | `true` for watch status |

**Response includes episodes with season/episode numbers:**
```json
{
  "Items": [
    {
      "Name": "Winter Is Coming",
      "Id": "ep123...",
      "Type": "Episode",
      "IndexNumber": 1,
      "ParentIndexNumber": 1,
      "SeriesName": "Game of Thrones",
      "SeriesId": "series456...",
      "UserData": {
        "Played": true,
        "PlayCount": 2,
        "LastPlayedDate": "2024-03-15T20:30:00.0000000Z"
      }
    }
  ]
}
```

---

## BaseItemDto — Relevant Fields

The `BaseItemDto` is Jellyfin's universal item type. Fields relevant to Mediatheca:

### Core Identity

| Field | Type | Notes |
|-------|------|-------|
| `Id` | string | Jellyfin internal ID (GUID) |
| `Name` | string | Title |
| `OriginalTitle` | string? | Original language title |
| `Type` | string | `Movie`, `Series`, `Season`, `Episode` |
| `ProductionYear` | int? | Release year |
| `PremiereDate` | string? | ISO date of first air/release |
| `Overview` | string? | Synopsis/description |

### Classification

| Field | Type | Notes |
|-------|------|-------|
| `Genres` | string[] | Genre names |
| `OfficialRating` | string? | Content rating (PG-13, TV-MA, etc.) |
| `CommunityRating` | float? | User rating (0-10 scale) |
| `CriticRating` | float? | Critic rating (0-100 scale) |

### External IDs (Critical for Matching)

| Field | Type | Notes |
|-------|------|-------|
| `ProviderIds` | dict | External service IDs |
| `ProviderIds.Tmdb` | string | **TMDB ID** — primary match key for Mediatheca |
| `ProviderIds.Imdb` | string | IMDB ID (e.g. `tt1234567`) |
| `ProviderIds.Tvdb` | string | TheTVDB ID |

### Series/Episode Hierarchy

| Field | Type | Notes |
|-------|------|-------|
| `SeriesName` | string? | Parent series name (on episodes) |
| `SeriesId` | string? | Parent series Jellyfin ID |
| `SeasonName` | string? | Season name (on episodes) |
| `SeasonId` | string? | Season Jellyfin ID |
| `IndexNumber` | int? | Episode number within season |
| `ParentIndexNumber` | int? | Season number |

### Duration

| Field | Type | Notes |
|-------|------|-------|
| `RunTimeTicks` | long? | Duration in ticks (1 tick = 100 nanoseconds, 10M ticks = 1 second) |

**Conversion:** `minutes = RunTimeTicks / 600_000_000`

### User Watch Data

| Field | Type | Notes |
|-------|------|-------|
| `UserData.Played` | bool | Fully watched |
| `UserData.PlayCount` | int | Number of complete watches |
| `UserData.LastPlayedDate` | string? | ISO timestamp of last watch |
| `UserData.PlaybackPositionTicks` | long | Resume position (0 if fully watched) |
| `UserData.IsFavorite` | bool | User favorite flag |

### Images

| Field | Type | Notes |
|-------|------|-------|
| `ImageTags` | dict | Keys: `Primary`, `Backdrop`, `Banner`, `Thumb`, etc. |

**Image URL pattern:** `GET {serverUrl}/Items/{itemId}/Images/{imageType}?tag={imageTag}`

---

## Matching Strategy

### TMDB ID Matching

Jellyfin stores external provider IDs on every item. The `ProviderIds.Tmdb` field maps directly to Mediatheca's `tmdb_id` stored on `movie_detail` and `series_detail` projection tables.

**Match flow:**

1. Fetch Jellyfin movies: `GET /Users/{userId}/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds&enableUserData=true`
2. For each item, extract `ProviderIds.Tmdb`
3. Query: `SELECT slug FROM movie_detail WHERE tmdb_id = ?`
4. If match found → candidate for watch sync
5. If no match → report as unmatched (future: auto-import)

Same for series: query `series_detail.tmdb_id`.

### Episode Matching

Episodes are matched by (seasonNumber, episodeNumber) within a matched series:

1. Find matched series (by TMDB ID)
2. Fetch Jellyfin episodes: `GET /Shows/{jellyfinSeriesId}/Episodes?userId={userId}&enableUserData=true`
3. For each episode: `ParentIndexNumber` = season number, `IndexNumber` = episode number
4. Query Mediatheca's `series_episode_progress` to check if already watched in default session
5. If Jellyfin `Played=true` and not watched in Mediatheca → sync candidate

---

## Webhook Plugin — Real-Time Playback Events

Requires [jellyfin-plugin-webhook](https://github.com/jellyfin/jellyfin-plugin-webhook) installed on the Jellyfin server.

### Setup

1. Install webhook plugin from Jellyfin Plugin Catalog
2. In Jellyfin Dashboard > Plugins > Webhook > Add Generic Destination
3. Set URL to `{mediatheca-url}/api/webhooks/jellyfin`
4. Enable events: **PlaybackStart**, **PlaybackProgress**, **PlaybackStop**
5. Use the Handlebars template below for the request body

### Recommended Handlebars Template

```json
{
  "NotificationType": "{{NotificationType}}",
  "ItemType": "{{ItemType}}",
  "Name": "{{Name}}",
  "SeriesName": "{{SeriesName}}",
  "SeasonNumber": {{#if SeasonNumber}}{{SeasonNumber}}{{else}}null{{/if}},
  "EpisodeNumber": {{#if EpisodeNumber}}{{EpisodeNumber}}{{else}}null{{/if}},
  "Provider_tmdb": "{{Provider_tmdb}}",
  "RunTimeTicks": {{RunTimeTicks}},
  "PlaybackPositionTicks": {{PlaybackPositionTicks}},
  "IsPaused": {{IsPaused}},
  "PlayedToCompletion": {{#if PlayedToCompletion}}{{PlayedToCompletion}}{{else}}false{{/if}},
  "Timestamp": "{{UtcTimestamp}}"
}
```

### Event Types

| Event | When | Key Fields |
|-------|------|------------|
| `PlaybackStart` | User starts playing an item | `ItemType`, `Name`, `Provider_tmdb`, `RunTimeTicks` |
| `PlaybackProgress` | Periodic during playback (~every 10s) | `PlaybackPositionTicks`, `RunTimeTicks`, `IsPaused` |
| `PlaybackStop` | User stops or finishes playback | `PlayedToCompletion`, `PlaybackPositionTicks` |

### Webhook Payload Fields

| Field | Type | Notes |
|-------|------|-------|
| `NotificationType` | string | `PlaybackStart`, `PlaybackProgress`, `PlaybackStop` |
| `ItemType` | string | `Movie`, `Episode` |
| `Name` | string | Item title (movie title or episode title) |
| `SeriesName` | string | Parent series name (episodes only) |
| `SeasonNumber` | int? | Season number (episodes only) |
| `EpisodeNumber` | int? | Episode number (episodes only) |
| `Provider_tmdb` | string | TMDB ID — **primary match key** |
| `RunTimeTicks` | long | Total duration in .NET ticks |
| `PlaybackPositionTicks` | long | Current position in .NET ticks |
| `IsPaused` | bool | Whether playback is paused |
| `PlayedToCompletion` | bool | `true` if item was fully watched (PlaybackStop only) |
| `Timestamp` | string | UTC timestamp of event |

### Additional Available Webhook Variables

These are available in the Handlebars template but not needed for our integration:

**Playback context:** `PlayMethod` (Transcode/DirectStream/DirectPlay), `AudioStreamIndex`, `SubtitleStreamIndex`, `DeviceName`, `ClientName`, `RemoteEndPoint`

**Media metadata:** `Overview`, `Tagline`, `Year`, `Genres`, `RunTime` (hh:mm:ss), `PlaybackPosition` (hh:mm:ss), `Provider_imdb`, `Provider_tvdb`

**User context:** `Username`, `UserId`, `NotificationUsername`

**Server context:** `ServerId`, `ServerName`, `ServerUrl`, `ServerVersion`

### Progress Calculation

```
progressPercent = (PlaybackPositionTicks / RunTimeTicks) * 100
```

**Tick conversion:** 1 second = 10,000,000 ticks. A 2-hour movie = 72,000,000,000 ticks.

### Playback Progress Frequency

`PlaybackProgress` events fire approximately every 10 seconds during active playback. For a 2-hour movie, expect ~720 progress events. The webhook receiver should be lightweight (in-memory state update only, no DB writes per tick).

### Matching Webhooks to Mediatheca Items

Webhook payloads include `Provider_tmdb` which maps to Mediatheca's `tmdb_id`:

- **Movies**: `Provider_tmdb` → query `movie_detail.tmdb_id` → get movie slug
- **Episodes**: `Provider_tmdb` on the *series* (use `SeriesName` context) — match series by TMDB ID, then match episode by `SeasonNumber` + `EpisodeNumber`

**Note:** For episodes, `Provider_tmdb` may refer to the episode's TMDB ID, not the series. The webhook also provides `SeriesName` which can be used as a fallback. During implementation, test which ID is provided and whether it's the series or episode TMDB ID.

---

## Rate Limits & Considerations

- **No explicit rate limit** on Jellyfin API (it's your own server)
- **Pagination**: use `Limit` + `StartIndex` for large libraries (>1000 items)
- **Token expiration**: tokens from `/Users/AuthenticateByName` don't expire by default, but can be invalidated by server restart or admin action
- **`enableUserData=true`** is critical — without it, `UserData` field will be null
- **RunTimeTicks** uses .NET ticks (10M per second), not Unix timestamps
