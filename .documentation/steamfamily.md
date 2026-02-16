# Steam Family Library Import — Technical Reference

How to import a Steam Family's shared game library, including per-game ownership attribution, using Steam's Web API.

## Prerequisites & Credentials

You need three credentials:

| Credential | Purpose | How to obtain |
|---|---|---|
| **Steam Web API Key** | Authenticates calls to `ISteamUser` and `IPlayerService` endpoints | Register at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey) |
| **Your Steam ID** | 64-bit numeric ID (e.g. `76561198012345678`) | Look it up on [steamid.io](https://steamid.io), or resolve a vanity URL via the API (see below) |
| **Family Access Token** | OAuth-style token for `IFamilyGroupsService` endpoints | Manually extracted from the browser (see below) |

### Two different auth mechanisms

- **Web API Key** — passed as `?key={apiKey}` on traditional endpoints (`ISteamUser`, `IPlayerService`).
- **Family Access Token** — passed as `?access_token={token}` on family endpoints (`IFamilyGroupsService`). This is a separate auth mechanism; the Web API Key does not work here.

### How to obtain the Family Access Token

1. Log into [store.steampowered.com](https://store.steampowered.com) in your browser.
2. Open DevTools → Network tab.
3. Navigate to **Store → Your Store → Family**.
4. Filter network requests for `IFamilyGroupsService`.
5. Copy the `access_token=...` query parameter from any matching request.

**Important:** This token expires within ~1 hour. There is no documented refresh mechanism — you must re-extract it when it expires.

---

## API Endpoints

All endpoints use `https://api.steampowered.com` as the base URL and return JSON.

### 1. Resolve Vanity URL (optional)

Convert a custom Steam profile URL to a 64-bit Steam ID.

```
GET /ISteamUser/ResolveVanityURL/v1/?key={apiKey}&vanityurl={vanityUrl}
```

**Response:**
```json
{
  "response": {
    "success": 1,
    "steamid": "76561198012345678"
  }
}
```

`success` = 1 means resolved. Any other value means failure (a `message` field explains why).

---

### 2. Get Family Group for User

Discover which Steam Family group the authenticated user belongs to.

```
GET /IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token={token}
```

**Response:**
```json
{
  "response": {
    "family_groupid": "12345678",
    "members": [
      { "steamid": "76561198012345678" }
    ]
  }
}
```

The `members` list may be incomplete from this endpoint alone. Use the next call to get the full list.

---

### 3. Get Family Group Details

Fetch the complete member list for a family group.

```
GET /IFamilyGroupsService/GetFamilyGroup/v1/?family_groupid={familyGroupId}&access_token={token}
```

**Response:**
```json
{
  "response": {
    "members": [
      { "steamid": "76561198012345678" },
      { "steamid": "76561198087654321" }
    ]
  }
}
```

A Steam Family can have up to ~6 members.

---

### 4. Get Player Summaries

Resolve Steam IDs to display names (persona names). Uses the **Web API Key**, not the family token.

```
GET /ISteamUser/GetPlayerSummaries/v2/?key={apiKey}&steamids={commaSeparatedSteamIds}
```

**Response:**
```json
{
  "response": {
    "players": [
      {
        "steamid": "76561198012345678",
        "personaname": "PlayerOne"
      },
      {
        "steamid": "76561198087654321",
        "personaname": "PlayerTwo"
      }
    ]
  }
}
```

Supports up to 100 Steam IDs per call (more than enough for a family).

---

### 5. Get Shared Library Apps

Fetch all games shared in the family library, with per-game ownership.

```
GET /IFamilyGroupsService/GetSharedLibraryApps/v1/?family_groupid={familyGroupId}&access_token={token}
```

**Response:**
```json
{
  "response": {
    "apps": [
      {
        "appid": 292030,
        "name": "The Witcher 3: Wild Hunt",
        "owner_steamids": ["76561198012345678", "76561198087654321"]
      },
      {
        "appid": 1174180,
        "name": "Red Dead Redemption 2",
        "owner_steamids": ["76561198012345678"]
      }
    ]
  }
}
```

**Known API quirk:** `owner_steamids` may **omit the authenticated user's own Steam ID** even if they own the game. You must work around this (see step 3 in the import flow below).

Some delisted or hidden apps may have an empty `name` — you'll want to skip those.

---

### 6. Get Owned Games (per user)

Fetch all games owned by a specific Steam user. Uses the **Web API Key**.

```
GET /IPlayerService/GetOwnedGames/v1/?key={apiKey}&steamid={steamId}&include_appinfo=true&include_played_free_games=true
```

**Response:**
```json
{
  "response": {
    "game_count": 150,
    "games": [
      {
        "appid": 292030,
        "name": "The Witcher 3: Wild Hunt",
        "playtime_forever": 3600,
        "img_icon_url": "abc123def456"
      }
    ]
  }
}
```

- `playtime_forever` is in **minutes**
- `include_appinfo=true` is required to get game names and icons
- `include_played_free_games=true` includes F2P games that have been launched at least once
- Returns all games in a single response (no pagination)

---

### 7. Cover Art (Steam CDN)

Download game cover art from Steam's public CDN (no authentication needed):

```
https://steamcdn-a.akamaihd.net/steam/apps/{appId}/library_600x900.jpg
```

Not every game has this asset — handle 404s gracefully.

---

## Import Flow

The family import happens in two phases: a **discovery phase** (user maps family members) and an **import phase** (games are fetched and attributed).

### Phase 1: Discover Family Members

```
┌─────────────────────────────────────────────────────┐
│ 1. GetFamilyGroupForUser  →  family_groupid         │
│                              + partial member list   │
│                                                      │
│ 2. GetFamilyGroup(family_groupid)  →  full members  │
│    (fallback to step 1's list if this fails)         │
│                                                      │
│ 3. GetPlayerSummaries(all member steamids)           │
│    →  steamid-to-display-name mapping                │
└─────────────────────────────────────────────────────┘
```

Present the member list to the user so they can map each Steam family member to a person/entity in your app. Persist these mappings for the import phase.

### Phase 2: Import Games with Ownership

```
┌──────────────────────────────────────────────────────┐
│ 1. GetFamilyGroupForUser  →  family_groupid          │
│                                                      │
│ 2. GetSharedLibraryApps(family_groupid)              │
│    →  all shared apps with owner_steamids            │
│                                                      │
│ 3. GetOwnedGames(your own steamid)                   │
│    →  your personal library (to fix the quirk)       │
│                                                      │
│ 4. Enrich: for each shared app, if you own it        │
│    (by appid match) but your steamid is missing      │
│    from owner_steamids, add it                       │
│                                                      │
│ 5. For each app:                                     │
│    a. Match against your existing game database      │
│       (by steam app ID, then by name)                │
│    b. If no match → create new game entry            │
│       - Optionally enrich with metadata from         │
│         another source (e.g. RAWG)                   │
│       - Download cover art from Steam CDN            │
│    c. Set family owners on the game using the        │
│       steamid → person mappings from Phase 1         │
└──────────────────────────────────────────────────────┘
```

### The owner_steamids Quirk (Important!)

`GetSharedLibraryApps` sometimes omits the authenticated user's own Steam ID from `owner_steamids`, even for games they own. The workaround:

1. Call `GetOwnedGames` for your own Steam ID (using the Web API Key).
2. Build a set of app IDs you own.
3. For each app from `GetSharedLibraryApps`, if your Steam ID is not in `owner_steamids` but the app ID is in your owned set, add your Steam ID to that app's owners.

Without this fix, you'll under-report your own ownership across the family library.

---

## Practical Considerations

### No Pagination

None of these endpoints use pagination. `GetOwnedGames` and `GetSharedLibraryApps` return the full dataset in a single response. For a typical family library (hundreds to low thousands of games), this works fine.

### Rate Limiting

Steam's Web API has undocumented rate limits (commonly cited as ~100,000 calls/day for the Web API Key). The family import makes very few API calls itself (5-6 total), but if you're downloading cover art for each game, that's one CDN request per game. Consider throttling cover downloads for large libraries.

### Token Expiration

The family access token expires in ~1 hour with no refresh mechanism. For large imports, the token could expire mid-process. Handle auth failures per-game gracefully so a partial import doesn't lose progress.

### Idempotency

Design your import to be re-runnable. Match existing games by Steam App ID first, then by name as a fallback. This lets users re-run the import to pick up newly purchased games without creating duplicates.

### Streaming Progress

For a good UX, consider streaming progress to the client (e.g. via Server-Sent Events) since processing hundreds of games with metadata enrichment and cover downloads takes time.
