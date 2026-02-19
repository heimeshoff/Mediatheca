# Task: Steam import description backfill

**ID:** 011
**Milestone:** M3 - Steam Description Backfill
**Size:** Small
**Created:** 2026-02-19
**Dependencies:** None

## Objective
During Steam library import, detect existing games with missing descriptions and backfill from Steam Store API.

## Details

### Current Behavior (src/Server/Api.fs — importSteamLibrary)
The current import flow when matching by `steam_app_id`:
1. Updates play time (`Game_play_time_set`)
2. Sets `steam_last_played`
3. Marks as owned
4. Does NOT fetch Steam Store details (descriptions, website, categories)

When matching by name, it DOES fetch Steam Store details. But games already matched by `steam_app_id` on previous imports never get their descriptions updated.

### Required Change
In the `importSteamLibrary` function, after matching a game by `steam_app_id`:
1. Check if the game's description is empty/missing (query `game_detail` table for `description` column)
2. If description is empty AND the game has a `steam_app_id`:
   - Call `Steam.getSteamStoreDetails` with the app ID
   - If successful, emit events to set:
     - `Game_short_description_set` (if short description available)
     - Description update (check if there's an existing event for full description, or if it needs a new event)
     - `Game_website_url_set` (if website available)
     - Play mode categories (if not already set)
3. Log how many games were enriched

### Edge Cases
- Game might have a RAWG description but no Steam description — only backfill if description is truly empty
- Steam Store API might return empty data for some apps (removed/delisted games) — handle gracefully
- Rate limit Steam Store API calls (add small delay between requests)

## Acceptance Criteria
- [ ] Games matched by steam_app_id with empty descriptions get enriched from Steam Store
- [ ] Short description, website URL, and play modes also backfilled if missing
- [ ] Games that already have descriptions are skipped
- [ ] Steam Store API failures handled gracefully (don't fail entire import)
- [ ] Works on re-import (idempotent — doesn't re-fetch for already-enriched games)

## Notes
- This is a targeted fix for the gap in the existing import flow
- The `getSteamStoreDetails` function already exists in Steam.fs — reuse it
- Consider adding a brief delay (e.g., 300ms) between Steam Store API calls to avoid rate limiting

## Work Log
<!-- Appended by /work during execution -->
