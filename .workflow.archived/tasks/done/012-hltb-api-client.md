# Task: HowLongToBeat API client

**ID:** 012
**Milestone:** M4 - HowLongToBeat Integration
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** None

## Objective
F# HTTP client that fetches game completion times from HowLongToBeat's internal API.

## Details

### Background
HowLongToBeat has **no official public API**. The community has reverse-engineered the internal API used by the HLTB website (a Next.js app). The endpoint path changes periodically, requiring dynamic discovery.

### API Flow (3 steps)

**Step 1: Discover search endpoint path**
- Fetch `https://howlongtobeat.com/` HTML
- Parse `<script src="/_next/static/...">` tags to find JS bundle URLs
- Fetch each JS bundle and regex-match for the search API path
- The path pattern changes every few months (was `/api/search`, then `/api/search/{hash}`, then `/api/seek/{hash}`, currently `/api/s/{path}`)
- Reference: Python library `howlongtobeatpy` v1.0.20 (Feb 2026) handles this in `HTMLRequests.py`

**Step 2: Obtain auth token**
- `GET https://howlongtobeat.com/api/search/init?t={unix_timestamp_ms}`
- Response JSON contains `{ "token": "..." }`
- Token required in `x-auth-token` header for search requests (added Nov 2025)

**Step 3: Search for game**
- `POST` to discovered search endpoint
- Headers: `Content-Type: application/json`, `User-Agent: <realistic browser UA>`, `Referer: https://howlongtobeat.com/`, `Origin: https://howlongtobeat.com`, `x-auth-token: <token>`
- Body:
```json
{
  "searchType": "games",
  "searchTerms": ["game", "name", "words"],
  "searchPage": 1,
  "size": 20,
  "searchOptions": {
    "games": {
      "userId": 0,
      "platform": "",
      "sortCategory": "popular",
      "rangeCategory": "main",
      "rangeTime": { "min": null, "max": null },
      "gameplay": { "perspective": "", "flow": "", "genre": "", "difficulty": "" },
      "rangeYear": { "min": "", "max": "" },
      "modifier": ""
    },
    "users": { "sortCategory": "postcount" },
    "filter": "",
    "sort": 0,
    "randomizer": 0
  },
  "useCache": true
}
```

### Response Structure
```json
{
  "data": [
    {
      "game_id": 1234,
      "game_name": "The Witcher 3",
      "game_alias": "Wild Hunt",
      "comp_main": 180000,       // seconds (divide by 3600 for hours)
      "comp_plus": 270000,       // main + extras, seconds
      "comp_100": 450000,        // completionist, seconds
      "comp_all": 300000,        // all styles average, seconds
      "comp_main_count": 5000,   // number of submissions
      "comp_plus_count": 3000,
      "comp_100_count": 1000,
      "review_score": 93,
      "release_world": 2015,
      "profile_platform": "PC, PlayStation 4, ..."
    }
  ]
}
```

**Critical: times are in SECONDS, not hours. Divide by 3600.**

### Implementation (src/Server/HowLongToBeat.fs — new file)

```fsharp
module Mediatheca.Server.HowLongToBeat

type HltbResult = {
    GameId: int
    GameName: string
    CompMainSeconds: int      // main story
    CompPlusSeconds: int      // main + extras
    Comp100Seconds: int       // completionist
    CompAllSeconds: int       // all styles
    MainCount: int            // number of user reports
    PlusCount: int
    Comp100Count: int
}

// Convert to hours for display
let toHours (seconds: int) = float seconds / 3600.0

// Search for a game by name, return best match
val searchGame: HttpClient -> string -> Async<HltbResult option>
```

Implementation approach:
1. Use `System.Net.Http.HttpClient` for all HTTP calls
2. Cache the discovered endpoint path + auth token (refresh on 403 error)
3. Use `Thoth.Json.Net` for JSON parsing (consistent with rest of codebase)
4. Name matching: split search terms, compare results using string similarity or exact prefix match
5. Return the best match (highest similarity to search query)

### Integration with Game Aggregate
- Shared type: add `HltbMainHours: float option`, `HltbPlusHours: float option`, `HltbCompletionistHours: float option` to `GameDetail` (currently only has single `HltbHours`)
- Consider whether to expand the single `hltb_hours` field to three fields, or keep it simple with just main story hours
- Store via existing `Game_hltb_hours_set` event (or add new events for the three categories)

### Resilience
- HLTB endpoint changes every few months — log warnings when discovery fails
- Auth token expires — refresh on 403
- Graceful degradation: if HLTB is unreachable, features that depend on it just show "no data"
- Add reasonable timeout (5s per request)
- Consider a simple local cache (SQLite table) to avoid re-fetching for games already looked up

### Gotchas
- User-Agent must look like a real browser (e.g., Chrome on Windows)
- The JS bundle URL discovery regex will need maintenance when HLTB changes their build
- Rate limiting: add 500ms+ delay between requests when fetching for multiple games
- Some games won't have HLTB data (indie, very new, etc.)

## Acceptance Criteria
- [x] `HowLongToBeat.fs` module created with `searchGame` function
- [x] Dynamic endpoint discovery from HLTB homepage JS bundles
- [x] Auth token fetch and header inclusion
- [x] Search returns parsed results with completion times in seconds
- [x] Best match selection by game name similarity
- [x] Graceful failure handling (returns None on any error)
- [x] Times correctly converted from seconds to hours
- [ ] Tested manually against a few known games (deferred — requires running server with live HLTB access)

## Notes
- Reference implementations:
  - Python: `howlongtobeatpy` v1.0.20 (https://github.com/ScrappyCocco/HowLongToBeat-PythonAPI) — most actively maintained
  - TypeScript: `howlongtobeat` npm package (https://github.com/ckatzorke/howlongtobeat)
  - Go: `forbiddencoding/howlongtobeat` (https://github.com/forbiddencoding/howlongtobeat)
- The Python library is the best reference for current endpoint discovery logic
- This is inherently fragile — design for graceful degradation, not reliability

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete

**Files created:**
- `src/Server/HowLongToBeat.fs` — New module with full HLTB API client

**Files modified:**
- `src/Server/Server.fsproj` — Added `HowLongToBeat.fs` to compile order (after `Steam.fs`, before `Jellyfin.fs`)

**Implementation details:**

1. **Types:** `HltbResult` record with `GameId`, `GameName`, `CompMainSeconds`, `CompPlusSeconds`, `Comp100Seconds`, `CompAllSeconds`, `MainCount`, `PlusCount`, `Comp100Count`. Plus `toHours` helper.

2. **Endpoint discovery (Step 1):** Fetches HLTB homepage HTML, extracts Next.js `/_next/static/.../*.js` script URLs via regex, fetches each JS bundle and looks for `fetch("/api/...")` POST patterns to discover the current search endpoint path. Falls back to `/api/finder` if discovery fails.

3. **Auth token (Step 2):** GET to `https://howlongtobeat.com/api/search/init?t={unix_ms}`, parses JSON `{"token": "..."}` with Thoth.Json.Net.

4. **Search (Step 3):** POST to discovered endpoint with JSON body containing search terms (split by whitespace), full searchOptions structure. Headers include User-Agent (Chrome 131), Referer, Origin, and x-auth-token.

5. **Caching:** `ApiDataCache` module caches discovered endpoint + auth token for 30 minutes. Cache is invalidated on 403 responses (token expiry), triggering automatic retry.

6. **Best match selection:** Jaccard similarity between search query and each result's `game_name`/`game_alias`. Minimum 0.2 threshold to avoid unrelated matches.

7. **Resilience:** All errors caught and logged with `[HLTB]` prefix. Returns `None` on any failure. 5-second timeout per HTTP request. Fallback endpoint when JS bundle discovery fails.

8. **Reference implementations studied:** Python `howlongtobeatpy` (HTMLRequests.py, JSONResultParser.py, HowLongToBeatEntry.py) and Go `forbiddencoding/howlongtobeat` (client.go, constants.go, parser.go, search.go, similarity.go).

**Verification:**
- `npm run build` — Success (Vite + Fable client build)
- `dotnet build src/Server/Server.fsproj` — 0 errors, 0 warnings
- `npm test` — 218 tests passed, 0 failed

**Note on manual testing:** The `searchGame` function requires a running server making live HTTP calls to howlongtobeat.com. Manual testing against known games should be done when the API is integrated in task 013.
