# Task: Game Trailer Playback

**ID:** 018
**Milestone:** --
**Size:** Medium
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Add "Play Trailer" functionality to game detail pages, matching the existing trailer experience for movies and TV series. Fetch trailers from Steam Store API (primary) with RAWG API fallback, and play them in an HTML5 `<video>` modal overlay.

## Details

### Backend: Trailer Sources

#### 1. Steam Store API (Primary Source)

Games with a `SteamAppId` can fetch trailers from the Steam Store API:

- **Endpoint:** `https://store.steampowered.com/api/appdetails?appids={steamAppId}`
- **Response:** `{steamAppId}.data.movies[]` — array of trailer objects:
  ```json
  {
    "id": 256740022,
    "name": "Launch Trailer",
    "thumbnail": "https://steamcdn-a.akamaihd.net/steam/apps/256740022/movie.293x165.jpg",
    "webm": {
      "480": "http://steamcdn-a.akamaihd.net/steam/apps/256740022/movie480.webm",
      "max": "http://steamcdn-a.akamaihd.net/steam/apps/256740022/movie_max.webm"
    },
    "mp4": {
      "480": "http://steamcdn-a.akamaihd.net/steam/apps/256740022/movie480.mp4",
      "max": "http://steamcdn-a.akamaihd.net/steam/apps/256740022/movie_max.mp4"
    },
    "highlight": true
  }
  ```
- **Strategy:** Pick the first `highlight: true` movie, prefer `mp4.max` URL (best browser compatibility), fall back to `webm.max`
- **No API key required** — this is a public endpoint
- **Rate limit:** ~200 requests per 5 minutes (no concern for single-user app)

#### 2. RAWG API (Fallback Source)

Games without a `SteamAppId` but with a `RawgId` can try RAWG:

- **Endpoint:** `https://api.rawg.io/api/games/{rawgId}/movies?key={apiKey}`
- **Response:** Returns trailer/gameplay video data (exact format TBD — parse at implementation time)
- **Strategy:** Try this only when Steam returns no trailers and the game has a `RawgId`

#### 3. New Server Function (`Steam.fs` or new `GameTrailers.fs`)

Create a function:
```fsharp
getGameTrailerUrl (httpClient: HttpClient) (steamAppId: int option) (rawgId: int option) (rawgConfig: Rawg.RawgConfig) : Async<GameTrailerInfo option>
```

Where `GameTrailerInfo` is a new shared type:
```fsharp
type GameTrailerInfo = {
    VideoUrl: string       // Direct MP4 or WebM URL
    ThumbnailUrl: string option
    Title: string option
}
```

- Try Steam first if `steamAppId` is `Some`
- Fall back to RAWG if `rawgId` is `Some` and Steam returned nothing
- Return `None` if no trailer found from either source

#### 4. API Endpoint

Add to `IMediathecaApi` in Shared.fs:
```fsharp
getGameTrailer: string -> Async<GameTrailerInfo option>  // takes game slug
```

Implementation in `Api.fs`: look up game's `SteamAppId` and `RawgId` from DB, then call the trailer function.

### Frontend: Game Detail Page

#### 5. Types (`GameDetail/Types.fs`)

Add to `Model`:
```fsharp
TrailerInfo: GameTrailerInfo option
ShowTrailer: bool
IsLoadingTrailer: bool
```

Add to `Msg`:
```fsharp
| Load_trailer
| Trailer_loaded of GameTrailerInfo option
| Open_trailer
| Close_trailer
```

#### 6. State (`GameDetail/State.fs`)

- On `Game_loaded`: dispatch `Load_trailer` command (call `api.getGameTrailer slug`)
- `Trailer_loaded`: store result in model
- `Open_trailer` / `Close_trailer`: toggle `ShowTrailer`

#### 7. Views (`GameDetail/Views.fs`)

**Play Trailer button:**
- Add a "Play Trailer" button in the game detail hero/action area, matching the movie detail pattern:
  - Red pill button (`bg-red-600/90 hover:bg-red-600`) with play icon + "Play Trailer" text
  - Only visible when `model.TrailerInfo` is `Some`
  - Clicking dispatches `Open_trailer`

**Video modal overlay:**
- Same layout as movie trailer modal (fixed inset-0 z-50, dark backdrop, centered container)
- Instead of YouTube `<iframe>`, use HTML5 `<video>` element:
  ```fsharp
  Html.video [
      prop.className "w-full h-full rounded-xl"
      prop.controls true
      prop.autoPlay true
      prop.children [
          Html.source [
              prop.src trailerInfo.VideoUrl
              prop.type' "video/mp4"  // or video/webm based on URL
          ]
      ]
  ]
  ```
- Close button (×) in top-right, clicking backdrop also closes
- Loading state: show a spinner or "Loading trailer..." while `IsLoadingTrailer` is true

## Acceptance Criteria

- [ ] Steam Store API integration fetches trailer MP4/WebM URLs for games with a SteamAppId
- [ ] RAWG API fallback fetches trailers for games without SteamAppId but with RawgId
- [ ] `getGameTrailer` endpoint added to `IMediathecaApi` and implemented in `Api.fs`
- [ ] Game detail page loads trailer info automatically when a game is loaded
- [ ] "Play Trailer" button appears only when a trailer is available
- [ ] "Play Trailer" button does NOT appear when no trailer was found
- [ ] Clicking "Play Trailer" opens a full-screen video modal overlay
- [ ] Video plays in an HTML5 `<video>` element with native browser controls
- [ ] Video autoplays when the modal opens
- [ ] Clicking the backdrop or × button closes the modal
- [ ] Button styling matches existing movie "Play Trailer" button (red pill with play icon)
- [ ] Modal styling matches existing movie trailer modal (dark backdrop, centered, rounded)
- [ ] Loading state shown while trailer is being fetched
- [ ] `npm run build` succeeds and all existing tests pass

## Notes

- Steam Store API is public (no API key needed), unlike the Steam Web API used for library/ownership
- Prefer `mp4.max` over `webm.max` for broadest browser support
- The `highlight: true` flag on Steam movies typically marks the main/official trailer
- Movies use YouTube iframes; games use HTML5 `<video>` — this is intentional since Steam provides direct video URLs
- Future enhancement: if a game has multiple trailers, add a trailer picker or carousel

### 2026-02-24 16:04 -- Work Completed

**What was done:**
- Added `GameTrailerInfo` shared type (`VideoUrl`, `ThumbnailUrl`, `Title`) to `Shared.fs`
- Added `getGameTrailer: string -> Async<GameTrailerInfo option>` to `IMediathecaApi`
- Implemented Steam Store API trailer fetching in `Steam.fs` (`getSteamStoreTrailer`) — parses `movies[]` from appdetails endpoint, prefers `highlight: true` movie, selects `mp4.max` > `mp4.480` > `webm.max` > `webm.480`, ensures HTTPS URLs
- Implemented RAWG API trailer fallback in `Rawg.fs` (`getGameTrailers`) — fetches from `/api/games/{rawgId}/movies` endpoint, parses video data with `max`/`480` quality options
- Implemented `getGameTrailer` endpoint in `Api.fs` — looks up game by slug, tries Steam first if `SteamAppId` is present, falls back to RAWG if `RawgId` is present
- Added `TrailerInfo`, `ShowTrailer`, `IsLoadingTrailer` to GameDetail Model
- Added `Load_trailer`, `Trailer_loaded`, `Open_trailer`, `Close_trailer` messages
- On `Game_loaded`, automatically dispatches `Load_trailer` to fetch trailer info
- Added "Play Trailer" red pill button in game detail hero section (only visible when trailer is available)
- Added loading state indicator while trailer is being fetched
- Added HTML5 `<video>` modal overlay with dark backdrop, close button, autoplay, native controls

**Acceptance criteria status:**
- [x] Steam Store API integration fetches trailer MP4/WebM URLs for games with a SteamAppId -- implemented in `Steam.getSteamStoreTrailer`
- [x] RAWG API fallback fetches trailers for games without SteamAppId but with RawgId -- implemented in `Rawg.getGameTrailers`
- [x] `getGameTrailer` endpoint added to `IMediathecaApi` and implemented in `Api.fs` -- wired up with Steam-first, RAWG-fallback logic
- [x] Game detail page loads trailer info automatically when a game is loaded -- `Game_loaded` dispatches `Load_trailer`
- [x] "Play Trailer" button appears only when a trailer is available -- conditional on `model.TrailerInfo` being `Some`
- [x] "Play Trailer" button does NOT appear when no trailer was found -- `None` case shows nothing (or loading indicator)
- [x] Clicking "Play Trailer" opens a full-screen video modal overlay -- dispatches `Open_trailer`, renders fixed overlay
- [x] Video plays in an HTML5 `<video>` element with native browser controls -- `prop.controls true`
- [x] Video autoplays when the modal opens -- `prop.autoPlay true`
- [x] Clicking the backdrop or x button closes the modal -- both have `Close_trailer` onClick handlers
- [x] Button styling matches existing movie "Play Trailer" button (red pill with play icon) -- identical CSS classes
- [x] Modal styling matches existing movie trailer modal (dark backdrop, centered, rounded) -- same layout structure
- [x] Loading state shown while trailer is being fetched -- spinner + "Loading trailer..." text during `IsLoadingTrailer`
- [x] `npm run build` succeeds and all existing tests pass -- 232 tests passed

**Files changed:**
- `src/Shared/Shared.fs` -- Added `GameTrailerInfo` type and `getGameTrailer` to `IMediathecaApi`
- `src/Server/Steam.fs` -- Added `SteamMovie`/`SteamMovieUrls` types, decoders, and `getSteamStoreTrailer` function
- `src/Server/Rawg.fs` -- Added `RawgTrailerResult`/`RawgTrailerData`/`RawgTrailersResponse` types, decoders, and `getGameTrailers` function
- `src/Server/Api.fs` -- Implemented `getGameTrailer` endpoint with Steam-first, RAWG-fallback logic
- `src/Client/Pages/GameDetail/Types.fs` -- Added `TrailerInfo`, `ShowTrailer`, `IsLoadingTrailer` to Model; added trailer Msg variants
- `src/Client/Pages/GameDetail/State.fs` -- Added trailer fields to init; `Game_loaded` dispatches `Load_trailer`; added trailer message handlers
- `src/Client/Pages/GameDetail/Views.fs` -- Added "Play Trailer" button in hero section; added HTML5 video modal overlay
