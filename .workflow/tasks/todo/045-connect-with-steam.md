# Task: Connect with Steam (manual button + auto-attach on Add Game)

**ID:** 045
**Milestone:** --
**Size:** Medium
**Created:** 2026-04-20
**Dependencies:** --

## Objective

Enable Steam data (App ID, description, trailers, play modes) for games that were added without a Steam link.

1. **Manual "Connect with Steam" button** on the Game Detail page, shown only when `game.SteamAppId = None`. Clicking it searches Steam for the game, and either attaches the best match automatically or shows a picker when the match is ambiguous.
2. **Auto-attach during Add Game**: After a game is imported via RAWG, immediately attempt a Steam search and attach the result silently when the match is high-confidence. If no confident match is found, leave `SteamAppId = None` — the user can click the Connect button later.

Primary user value: trailers and Steam-sourced descriptions become available for games that RAWG provides but Steam wasn't matched against.

## Background

Current state (as of task 044):
- `Game.SteamAppId` is `Option<int>`; when `None`, the Links section on Game Detail simply renders nothing for Steam (`src/Client/Pages/GameDetail/Views.fs` ~line 1168).
- The `addGame` API handler (`src/Server/Api.fs` ~line 2502) fetches full RAWG details when `RawgId` is present, creates the game via `Games.Add_game`, and downloads images — but never touches Steam.
- `src/Server/Steam.fs` exposes `getSteamStoreDetails` (fetch by App ID) but **no search-by-name** function exists. Steam does not have an official public Store search API.
- Events already in place for attaching Steam data: `Game_steam_app_id_set`, `Game_description_set`, `Game_short_description_set`, `Game_website_url_set`, `Game_play_mode_added`, `Game_steam_last_played_set`. Task 011 ("steam-description-backfill") wires these up when the Steam library import matches games by App ID — the same emission pattern should be reused here.
- Trailers (`getGameTrailers`, task 043) fall back to RAWG when there's no Steam App ID, but RAWG trailer coverage is sparse. Attaching Steam fills this gap.

Steam search strategy chosen: **GetAppList + fuzzy match**. Pull the full public app list once, cache it in memory with a TTL, fuzzy-match by name (and year when available). No new dependency, no HTML scraping.

## Details

### Backend

#### 1. Steam name-search in `src/Server/Steam.fs`

Add a Steam app search capability backed by Valve's `ISteamApps/GetAppList/v2` endpoint (`https://api.steampowered.com/ISteamApps/GetAppList/v2/`, public, no key required). The response is `{ applist: { apps: [{ appid, name }, ...] } }` — ~200k entries, ~10 MB JSON.

- **Fetch + cache:** Load the full app list once into an in-memory `Map<string, (int * string) list>` (normalized-name → list of `(appId, originalName)`). Refresh after a TTL (24 hours is fine — the list is append-mostly). Concurrent callers should share the same fetch (guard with `SemaphoreSlim` or `Lazy<Task<_>>`).
- **Normalize names** for matching: lowercase, strip trademark/registered/™/® symbols, strip punctuation, collapse whitespace, drop edition suffixes (`"Definitive Edition"`, `"Game of the Year Edition"`, `"Deluxe Edition"`, etc. — a small blocklist is enough).
- **Fuzzy match function:** `searchSteamByName : string -> int option -> Async<SteamSearchResult list>` where the second arg is the optional release year. Rank candidates by:
  1. Exact normalized-name match (score 1.0)
  2. Normalized substring match (either direction) with length ratio weighting
  3. Token-set overlap (Jaccard on word tokens) for partial matches
  - When a year is provided, fetch the App's `release_date` from Store details for the top ~3 candidates and boost matches within ±1 year of the provided year. Penalize mismatches.
- Return at most the top 5 candidates as:
  ```fsharp
  type SteamSearchResult = {
      AppId: int
      Name: string
      ReleaseYear: int option
      HeaderImageUrl: string option
      Score: float   // 0.0 .. 1.0
  }
  ```
  Add this type to `src/Shared/Shared.fs` (so the client can render the picker).

- **Confidence thresholds (used by callers):**
  - `Score >= 0.95` → "high confidence" (auto-attach, single match)
  - `0.7 <= Score < 0.95` → "ambiguous" (return for picker)
  - `Score < 0.7` → filter out

#### 2. Shared API contract — `src/Shared/Shared.fs`

Add two endpoints to `IMediathecaApi`:

```fsharp
searchSteamForGame: string -> Async<SteamSearchResult list>   // by slug
attachSteamToGame: string * int -> Async<Result<unit, string>>   // slug, chosen appId
```

`searchSteamForGame` looks up the game by slug on the server to pull its Name + Year, runs `Steam.searchSteamByName`, and returns the filtered candidate list.

`attachSteamToGame` runs the "attach" workflow (see step 3) for the chosen App ID. Returns `Ok ()` on success, `Error msg` for the rare failure case (Steam API down, App ID returns 404, etc.).

Also add `SteamSearchResult` to `Shared.fs`.

#### 3. Attach workflow — `src/Server/Api.fs`

Factor out a reusable helper:

```fsharp
let attachSteamToGameById (gameId: GameId) (slug: string) (appId: int) : Async<Result<unit, string>>
```

This helper does exactly what task 011's backfill + the Steam library import already do for a single game:

1. Call `Steam.getSteamStoreDetails appId`. If it returns `None`, return `Error "Steam lookup failed"`.
2. Emit `Games.Set_steam_app_id appId` via the command bus.
3. If the current game description is empty, emit `Games.Set_description <full>`.
4. If the current short description is empty, emit `Games.Set_short_description <short>`.
5. If `WebsiteUrl` is `None` and Store has one, emit `Games.Set_website_url <url>`.
6. Emit `Games.Add_play_mode` for each `categories` entry Store returns (co-op, multi-player, etc.) that isn't already present.

All emissions go through the existing event store; projections handle the rest. Trailers are **not** stored — they're fetched on demand by `getGameTrailers`.

Wire the helper into:
- The new `attachSteamToGame` API endpoint (manual path).
- The `addGame` handler (auto-attach path, see step 4).

#### 4. Auto-attach during Add Game — `src/Server/Api.fs`

In `addGame` (~line 2502), after `Games.Add_game` succeeds and RAWG images are downloaded, and **only when the request had a `RawgId`**:

1. Call `Steam.searchSteamByName request.Name (Some request.Year)`.
2. If the top result has `Score >= 0.95` and there is no second result within `0.05` of it (i.e. unambiguous), call `attachSteamToGameById` with that App ID.
3. Otherwise, do nothing — the user can still click Connect later.
4. Failures are swallowed and logged — Steam being down must never break Add Game.

Do this **synchronously** inside the handler so the client gets the enriched game back on the initial response, not asynchronously.

### Frontend

#### 5. Connect button in the Links section — `src/Client/Pages/GameDetail/Views.fs`

In the Links section (~lines 1162–1209), branch on `game.SteamAppId`:

- `Some appId` — existing "Steam Store" link (unchanged, ~lines 1168–1181).
- `None` — render a new "Connect with Steam" button styled to match the other Link buttons (same glassmorphic/outline style as the HLTB button). Icon: Steam logo in muted color to signal "not yet linked."

Clicking the button dispatches `Connect_steam_requested`, which triggers the search → result handling described below.

#### 6. State & Msg — `src/Client/Pages/GameDetail/Types.fs` + `State.fs`

Add to `Model`:
```fsharp
ConnectSteamState: ConnectSteamState

and ConnectSteamState =
    | Idle
    | Searching
    | ShowingCandidates of SteamSearchResult list
    | Attaching of int   // chosen appId
    | Failed of string
```

Add to `Msg`:
```fsharp
| Connect_steam_requested
| Steam_search_completed of SteamSearchResult list
| Steam_candidate_chosen of int
| Steam_attach_completed of Result<unit, string>
| Connect_steam_dismissed
```

`State.fs` handling:
- `Connect_steam_requested` → set `ConnectSteamState = Searching`, dispatch `api.searchSteamForGame slug`.
- `Steam_search_completed candidates`:
  - Empty list → `Failed "No Steam match found"`.
  - Single candidate with `Score >= 0.95` → directly dispatch `Steam_candidate_chosen candidate.AppId`.
  - Otherwise → `ShowingCandidates candidates`.
- `Steam_candidate_chosen appId` → set `Attaching appId`, dispatch `api.attachSteamToGame (slug, appId)`.
- `Steam_attach_completed (Ok ())` → re-dispatch `Load_game slug` to refresh the detail page (new description, Steam link, trailers all appear).
- `Steam_attach_completed (Error msg)` → `Failed msg`.
- `Connect_steam_dismissed` → back to `Idle`.

#### 7. Candidate picker UI — `src/Client/Pages/GameDetail/Views.fs`

When `ConnectSteamState = ShowingCandidates results`, render a glassmorphic dropdown/popover anchored to the Connect button (use the same glassmorphism recipe the CLAUDE.md mandates: `/0.55`–`/0.70` opacity, `backdrop-filter: blur(24px) saturate(1.2)`, border, top highlight — mirror `.rating-dropdown` / `.glass-card`). Each row shows:
- Header image thumbnail (if present)
- Game name
- Release year (if known)
- Small "Choose" button

The popover must render as a sibling to any blurred parent, not a child (see CLAUDE.md gotcha on nested `backdrop-filter`).

Show a small spinner while `Searching` / `Attaching`. For `Failed msg`, show an inline error under the button with a "Dismiss" affordance.

## Acceptance Criteria

- [ ] Game detail page with `SteamAppId = None` shows a "Connect with Steam" button in the Links section, styled consistently with the other link buttons
- [ ] Clicking Connect with a clear single match attaches Steam data and refreshes the page to show the Steam Store link, description (if was empty), website link (if was empty), and play modes
- [ ] Clicking Connect with multiple candidates opens a glassmorphic picker listing up to 5 candidates with name, year, and header image
- [ ] Clicking Connect with no match shows an inline "No Steam match found" message that can be dismissed
- [ ] After attaching Steam, the trailer gallery populates with Steam trailers (verify with a game known to have Steam trailers)
- [ ] Adding a new game via Add Game with a RAWG ID, where the game has a clear Steam match, results in `SteamAppId` populated on the created game — no second click required
- [ ] Adding a new game where no confident Steam match exists still succeeds; `SteamAppId` stays `None`; no error surfaces to the user
- [ ] Adding a game when the Steam API is unreachable still succeeds; the auto-attach failure is logged but swallowed
- [ ] `searchSteamByName` caches the Steam app list and does not re-fetch it on every call (verify via logs — only the first call fetches)
- [ ] `attachSteamToGame` emits the same events as the Steam library import path (`Game_steam_app_id_set`, and `Game_description_set` / `Game_short_description_set` / `Game_website_url_set` / `Game_play_mode_added` only when the current value is missing)
- [ ] `npm run build` succeeds
- [ ] `npm test` passes all tests
- [ ] Design check: Connect button + picker follow the design system (glassmorphism on the picker, DesignSystem.fs tokens, no nested `backdrop-filter`)

## Notes

- Steam's `GetAppList` is large (~10 MB) — fetch with streaming JSON if the naïve read blocks startup; otherwise a lazy first-call fetch is fine.
- Fuzzy matching is where this feature lives or dies. Start with a simple normalizer + exact/substring/token-set match before reaching for Levenshtein — the match quality is more about strong normalization than clever distance metrics.
- The year boost needs a second API call per top candidate (`getSteamStoreDetails` for `release_date`). Cap at the top 3 candidates to avoid fan-out. This call's result can be cached per App ID within the session.
- Watch for non-game AppIDs in the app list — Steam tools, soundtracks, dedicated servers, and DLC all live in the same namespace. Filter by fetching the Store details for the top candidate and checking `type = "game"` before auto-attaching. Ambiguous picker results can skip this check since the user visually disambiguates.
- The auto-attach during Add Game must not delay the handler meaningfully — cap the Steam search + attach round-trip, and if it takes too long, fail open (skip the attach, return the created game).
- Watch for games with identical names across different years (remakes, reboots). The year boost is the main defense; without a year in the AddGameRequest, fall back to ambiguous → user-picks on manual Connect.
