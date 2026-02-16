# Games API Gap Analysis — Steam & RAWG

Analysis of data available from Steam and RAWG APIs that is **not yet stored** in Mediatheca's game data model.

## Current Game Data Model

From `GameDetail` / `ActiveGame` (src/Shared/Shared.fs, src/Server/Games.fs):

| Field | Source |
|-------|--------|
| Name, Year | Manual / RAWG search |
| Description (text) | RAWG `description_raw` |
| CoverRef, BackdropRef | RAWG `background_image` / Steam CDN |
| Genres (string list) | RAWG search results |
| Status (Backlog/Playing/Completed/Abandoned/OnHold) | Manual |
| RawgId, RawgRating | RAWG |
| HltbHours | Manual (HowLongToBeat) |
| PersonalRating | Manual |
| SteamAppId | Steam import |
| TotalPlayTimeMinutes | Steam `GetOwnedGames` |
| Stores (string set) | Manual |
| FamilyOwners, RecommendedBy, WantToPlayWith, PlayedWith | Manual |
| ContentBlocks | Manual (journal-style notes) |

### Current API Usage

**Steam** (src/Server/Steam.fs):
- `IPlayerService/GetOwnedGames/v1` — owned games with playtime
- `ISteamUser/ResolveVanityURL/v1` — vanity URL resolution
- `ISteamUser/GetPlayerSummaries/v2` — player display names
- `IFamilyGroupsService/*` — family library sharing
- Steam CDN `library_600x900.jpg` — cover art

**RAWG** (src/Server/Rawg.fs):
- `GET /api/games?search=` — search (name, year, background_image, rating, genres)
- `GET /api/games/{id}` — details (description, description_raw, background_image, background_image_additional, rating, genres)
- `GET /api/games/{id}/screenshots` — screenshot image URLs

---

## Steam Store API — `appdetails`

**Endpoint:** `GET https://store.steampowered.com/api/appdetails?appids={SteamAppId}`
**Auth:** None required (just the SteamAppId we already store)
**Rate limit:** ~200 requests per 5 minutes

### Available Data NOT in Our System

#### Descriptions
| Field | Type | Notes |
|-------|------|-------|
| `short_description` | string | ~300 char plain-text summary — great for cards/lists |
| `detailed_description` | string (HTML) | Full formatted description, richer than RAWG plain text |
| `about_the_game` | string (HTML) | Game overview (often same as detailed_description) |

#### Visual Media
| Field | Type | Notes |
|-------|------|-------|
| `screenshots[].path_thumbnail` | URL | Thumbnail (600x338) |
| `screenshots[].path_full` | URL | Full resolution (1920x1080) |
| `movies[].name` | string | Trailer title |
| `movies[].thumbnail` | URL | Video thumbnail |
| `movies[].webm.480` | URL | 480p WebM video |
| `movies[].webm.max` | URL | Max resolution WebM video |
| `movies[].highlight` | bool | Whether this is the primary trailer |
| `header_image` | URL | 460x215 header image |
| `capsule_image` | URL | Small capsule (231x87) |
| `background` | URL | Store page background |

#### Metadata
| Field | Type | Notes |
|-------|------|-------|
| `developers` | string[] | Studio names (e.g. "FromSoftware") |
| `publishers` | string[] | Publisher names (e.g. "Bandai Namco") |
| `release_date.date` | string | Full date (e.g. "Feb 25, 2022") |
| `release_date.coming_soon` | bool | Unreleased flag |
| `metacritic.score` | int | 0-100 Metacritic score |
| `metacritic.url` | string | Link to Metacritic page |
| `website` | string | Official game website URL |

#### Categories & Features
| Field | Type | Notes |
|-------|------|-------|
| `categories` | object[] | Feature flags with id + description |

Key category IDs:
- 2 = Single-player, 1 = Multi-player, 9 = Co-op
- 36 = Online Multi-Player, 38 = Online Co-op
- 24 = Local Multi-Player, 37 = Local Co-op
- 44 = Remote Play Together
- 22 = Steam Achievements, 23 = Steam Cloud
- 28 = Full controller support, 51 = Partial Controller Support
- 29 = Steam Trading Cards, 30 = Steam Workshop
- 62 = Family Sharing

#### Platforms & Requirements
| Field | Type | Notes |
|-------|------|-------|
| `platforms.windows/mac/linux` | bool | OS support flags |
| `pc_requirements.minimum` | string (HTML) | Minimum PC specs |
| `pc_requirements.recommended` | string (HTML) | Recommended PC specs |
| `controller_support` | string | "partial" or "full" |
| `supported_languages` | string (HTML) | Languages with audio/subtitle distinction |

#### Ratings & Reviews
| Field | Type | Notes |
|-------|------|-------|
| `ratings.esrb` | object | ESRB rating + descriptors |
| `ratings.pegi` | object | PEGI rating + descriptors |
| `ratings.usk` | object | USK (Germany) rating |
| `required_age` | int | Minimum age (0 if none) |
| `content_descriptors` | object | Content warning IDs + notes |
| `recommendations.total` | int | Total user recommendations |

#### Other
| Field | Type | Notes |
|-------|------|-------|
| `is_free` | bool | Free-to-play flag |
| `dlc` | int[] | DLC app IDs |
| `achievements.total` | int | Achievement count |
| `achievements.highlighted` | object[] | Featured achievements with icons |
| `price_overview` | object | Currency, initial/final price, discount % |
| `type` | string | "game", "dlc", "demo", etc. |
| `support_info` | object | Support URL + email |

### Other Steam Endpoints (No Key Needed)

| Endpoint | Data | Notes |
|----------|------|-------|
| `ISteamUserStats/GetNumberOfCurrentPlayers/v1` | Live concurrent players | Fun live stat |
| `ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2` | Per-achievement unlock % | Achievement rarity |
| `ISteamNews/GetNewsForApp/v2` | Game news/patch notes | News feed |
| `store.steampowered.com/appreviews/{appid}?json=1` | Review summary + individual reviews | Community sentiment |

### Steam Endpoints (Key Required — already have key)

| Endpoint | Data | Notes |
|----------|------|-------|
| `ISteamUserStats/GetSchemaForGame/v2` | Full achievement list with names, descriptions, icons | Completion tracking |
| `ISteamUserStats/GetPlayerAchievements/v1` | Per-user achievement unlock status | Personal achievement tracking |

---

## RAWG API — Sub-Endpoints

All require the RAWG API key (already configured). Most need just the RawgId (already stored).
**Rate limit:** 20,000 requests/month (free tier).

### `GET /api/games/{id}` — Additional Fields Not Currently Used

| Field | Type | Notes |
|-------|------|-------|
| `tags` | Tag[] | Community tags: "Open World", "Souls-like", "Co-op", etc. (400+) — much richer than genres |
| `developers` | Developer[] | Studio objects with id/name/slug |
| `publishers` | Publisher[] | Publisher objects with id/name/slug |
| `metacritic` | int | 0-100 Metacritic score |
| `metacritic_platforms` | MetacriticPlatform[] | Per-platform Metacritic scores |
| `esrb_rating` | EsrbRating | Structured ESRB rating (Everyone/Teen/Mature/etc.) |
| `platforms` | PlatformEntry[] | Per-platform release dates + system requirements |
| `stores` | StoreEntry[] | Store objects with actual purchase URLs |
| `website` | string | Official game website |
| `released` | date | Full release date (YYYY-MM-DD) |
| `alternative_names` | string[] | Aliases and localized titles |
| `playtime` | int | Average community playtime in hours |
| `added_by_status` | object | How many users own/beat/dropped/playing |
| `reddit_url` | string | Subreddit URL |
| `clip` | Clip | Short gameplay clip (deprecated for many games) |
| `screenshots_count` | int | Total screenshots available |
| `movies_count` | int | Total trailers available |
| `achievements_count` | int | Total achievements |
| `additions_count` | int | DLC/edition count |
| `game_series_count` | int | Related franchise entries |

### `GET /api/games/{id}/movies` — Trailers

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | Trailer ID |
| `name` | string | Trailer title |
| `preview` | URL | Thumbnail image |
| `data.480` | URL | 480p MP4 video |
| `data.max` | URL | Max resolution MP4 video |

### `GET /api/games/{id}/achievements` — Achievements

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Achievement title |
| `description` | string | Unlock criteria |
| `image` | URL | Achievement icon |
| `percent` | string | Community unlock percentage |

### `GET /api/games/{id}/additions` — DLC & Editions

Returns full Game objects for each DLC/expansion. Same fields as the game list.

### `GET /api/games/{id}/game-series` — Franchise

Returns full Game objects for all games in the same series/franchise.

### `GET /api/games/{id}/development-team` — Individual Creators

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Person's name |
| `image` | URL | Photo |
| `positions` | Position[] | Roles: director, artist, writer, composer, etc. |

### `GET /api/games/{id}/stores` — Store Links

| Field | Type | Notes |
|-------|------|-------|
| `store_id` | int | 1=Steam, 5=GOG, 11=Epic, 3=PS Store, etc. |
| `url` | URL | Direct purchase/store page link |

### `GET /api/games/{id}/screenshots` — Screenshots (already used, but more metadata available)

| Field | Type | Notes |
|-------|------|-------|
| `width` | int | Image width |
| `height` | int | Image height |

---

## Priority-Ranked Gaps

Data not yet in the system, ranked by value for a personal media library:

### Tier 1 — High Value
1. **Trailers/Videos** — Both APIs provide direct video URLs. Steam has WebM/DASH; RAWG has MP4s.
2. **Screenshots gallery** — Both APIs. Already fetching from RAWG for image picker, but not storing/displaying as a gallery.
3. **Developers/Publishers** — Both APIs. Completely missing from game model.
4. **Tags** (RAWG) — Far richer than genres. "Souls-like", "Metroidvania", "Open World", etc.
5. **Metacritic score** — Both APIs. Well-known benchmark alongside RAWG rating.
6. **Multiplayer/Co-op categories** (Steam) — Critical for a social app tracking "PlayedWith" and "WantToPlayWith".

### Tier 2 — Medium Value
7. **Store links with URLs** (RAWG) — Currently stores as plain strings; could have actual purchase links.
8. **Game series/franchise** (RAWG) — Browse related games.
9. **DLC list** — Both APIs.
10. **Achievements + unlock %** — Both APIs. Completion tracking potential.
11. **ESRB/PEGI ratings** — Steam has all agencies; RAWG has ESRB.
12. **Full release date** — Both APIs. Currently only store year.
13. **Controller support** (Steam) — Useful for choosing input method.

### Tier 3 — Nice to Have
14. **System requirements** — Both APIs.
15. **Current player count** (Steam) — Fun live stat, no key needed.
16. **Game news/patch notes** (Steam) — News feed, no key needed.
17. **Supported languages** (Steam) — Localization info.
18. **Community playtime** (RAWG) — Benchmark against personal playtime.
19. **Reddit/community links** (RAWG) — External community links.
20. **Similar/suggested games** (RAWG) — Discovery feature.

---

## Key Implementation Notes

- **Steam `appdetails`** is the single highest-value endpoint to add. No API key needed, just the `SteamAppId` already stored on games. One call per game returns descriptions, trailers, screenshots, Metacritic, developers, categories, achievements, ratings, and more.
- **RAWG game details** already has a decoder (`decodeGameDetails` in Rawg.fs) but only extracts `description`, `description_raw`, `background_image`, `background_image_additional`, `rating`, and `genres`. The response actually contains tags, developers, publishers, metacritic, platforms, stores, and much more.
- **Rate limits:** Steam `appdetails` allows ~200 requests/5 minutes (no key). RAWG free tier is 20,000/month. Both are fine for a personal library app.
- **No additional API keys needed** for Steam Store API enrichment — it's completely public.
