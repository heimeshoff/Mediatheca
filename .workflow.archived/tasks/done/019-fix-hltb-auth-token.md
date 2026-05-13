# Task: Fix HLTB auth token endpoint (404)

**ID:** 019
**Milestone:** M4 - HowLongToBeat Integration
**Size:** Small
**Created:** 2026-02-24
**Dependencies:** None

## Objective
Fix the broken How Long To Beat integration — the auth token endpoint is hardcoded to `/api/search/init` which now returns 404. Make it relative to the discovered search endpoint.

## Problem
Every game shows "No HLTB data available" because the auth token fetch fails:
```
[HLTB] Discovering search endpoint...
[HLTB] Discovered search endpoint: /api/finder
[HLTB] Fetching auth token from https://howlongtobeat.com/api/search/init?t=...
[HLTB] Failed to get API data: Auth token fetch failed: HTTP 404
```

The search endpoint is correctly discovered as `/api/finder`, but the token URL ignores it and always calls `/api/search/init`.

## Root Cause
In `src/Server/HowLongToBeat.fs`, the `fetchAuthToken` function (line 145) hardcodes the token URL:
```fsharp
let url = sprintf "%s/api/search/init?t=%d" baseUrl timestamp
```

The Python reference library (`howlongtobeatpy`) derives the token URL from the search endpoint:
```
{base_url}{search_endpoint}/init?t={timestamp}
```

So if the search endpoint is `/api/finder`, the token endpoint should be `/api/finder/init?t=...`.

## Fix

### 1. Make `fetchAuthToken` accept the search endpoint
Change the signature from:
```fsharp
let private fetchAuthToken (httpClient: HttpClient) : Async<Result<string, string>>
```
to:
```fsharp
let private fetchAuthToken (httpClient: HttpClient) (searchEndpoint: string) : Async<Result<string, string>>
```

And build the URL as:
```fsharp
let url = sprintf "%s%s/init?t=%d" baseUrl searchEndpoint timestamp
```

### 2. Update `getApiData` call order
Currently `getApiData` discovers the endpoint first, then fetches the token separately. Update it to pass the discovered endpoint (or fallback) to `fetchAuthToken`.

### 3. Update fallback endpoint
The Python library's current fallback is `api/s/` (not `api/finder`). Consider updating the fallback, but only if `/api/finder` also stops working — it currently discovers fine.

### 4. Verify endpoint discovery regex still works
The endpoint discovery currently finds `/api/finder`. Verify the regex patterns still match HLTB's current JS bundle structure. If the Python library has updated its patterns, mirror those changes.

## Acceptance Criteria
- [x] Auth token URL derived from discovered search endpoint, not hardcoded
- [ ] HLTB data fetches successfully for known games (e.g., "The Witcher 3", "Hades")
- [x] Fallback path still works if endpoint discovery fails
- [x] 403 retry (token expiry) still works correctly
- [ ] Server logs show successful token fetch: `[HLTB] Auth token obtained successfully`
- [x] `npm run build` succeeds
- [x] `npm test` passes

## Files to Modify
- `src/Server/HowLongToBeat.fs` — Primary fix (auth token URL + function signature)

## Reference
- Python library: [howlongtobeatpy HTMLRequests.py](https://github.com/ScrappyCocco/HowLongToBeat-PythonAPI/blob/master/howlongtobeatpy/howlongtobeatpy/HTMLRequests.py)
- Original implementation: Task 012 (`tasks/done/012-hltb-api-client.md`)
- TypeScript library: [ckatzorke/howlongtobeat](https://github.com/ckatzorke/howlongtobeat)

---

### 2026-02-24 16:12 -- Work Completed

**What was done:**
- Changed `fetchAuthToken` signature to accept `searchEndpoint: string` parameter
- Changed auth token URL from hardcoded `/api/search/init?t=...` to `{searchEndpoint}/init?t=...`, deriving it from the discovered (or fallback) search endpoint
- Simplified `getApiData` to resolve the endpoint first (discovered or fallback), then pass it to `fetchAuthToken` in a single code path, eliminating duplicated token-fetch logic

**Acceptance criteria status:**
- [x] Auth token URL derived from discovered search endpoint, not hardcoded -- URL is now `sprintf "%s%s/init?t=%d" baseUrl searchEndpoint timestamp`
- [ ] HLTB data fetches successfully for known games -- requires live server test against HLTB (not testable in CI)
- [x] Fallback path still works if endpoint discovery fails -- `getApiData` falls back to `/api/finder` and passes it to `fetchAuthToken`
- [x] 403 retry (token expiry) still works correctly -- `searchGame` 403 handler calls `ApiDataCache.invalidate()` then `getApiData` which re-discovers and re-fetches token
- [ ] Server logs show successful token fetch -- requires live server test against HLTB
- [x] `npm run build` succeeds -- verified, build completed successfully
- [x] `npm test` passes -- verified, 232 tests passed, 0 failed

**Files changed:**
- `src/Server/HowLongToBeat.fs` -- Changed `fetchAuthToken` to accept search endpoint parameter; updated token URL construction to use `{searchEndpoint}/init` instead of hardcoded `/api/search/init`; simplified `getApiData` to resolve endpoint once then pass to `fetchAuthToken`
