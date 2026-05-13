# Task: Game Trailer Gallery in Overview

**ID:** 043
**Milestone:** --
**Size:** Small-to-Medium
**Created:** 2026-04-20
**Dependencies:** 018 (game trailer infrastructure)

## Objective

Render all available Steam/RAWG trailers for a game as a scrollable gallery directly inside the game detail page's Overview section — full-width, above the two-column layout. Each item shows a thumbnail; clicking plays the video inline. Stream from the external CDN (no local download). If a video fails to load, hide it gracefully.

## Background

Task 018 added single-trailer fetching (`getGameTrailer`) plus a modal "Play Trailer" button. That infrastructure stays; this task adds a second, richer surface: an always-visible gallery of *every* trailer returned by the APIs.

- Steam returns an array of movies per game (`data.movies[]`) — often 3–8 per title
- RAWG returns an array of videos (`results[]`)
- Both are direct MP4/WebM URLs hosted on Steam's or RAWG's CDN — no auth, no download needed

Streaming was chosen over local download because:
- Trailers total 20–80MB each at max quality; a library of hundreds of games = many GBs of disk
- Content rotates on Steam; streaming stays fresh
- The home server is online nearly 100% of the time — offline playback isn't a real use case
- HTML5 `<video>` fails silently on network error, making graceful degradation trivial

## Details

### Backend

#### 1. Server: return multiple trailers

The server already has `Steam.getSteamStoreTrailer` and `Rawg.getGameTrailers`. Audit them:
- `Steam.getSteamStoreTrailer` currently returns only the first/highlight movie — extend or add `getSteamStoreTrailers` that returns the full list, mapped to `GameTrailerInfo`
- `Rawg.getGameTrailers` already fetches the list but `Api.fs:2959` picks just one — expose the full list

Preserve quality selection logic from task 018: prefer MP4 > WebM, max > 480p, force HTTPS.

#### 2. New API endpoint

Add to `IMediathecaApi` in `src/Shared/Shared.fs`:

```fsharp
getGameTrailers: string -> Async<GameTrailerInfo list>
```

Takes the game slug. Returns combined Steam + RAWG trailers (Steam first, then any RAWG ones that aren't duplicates by URL). Returns `[]` if none found or if lookups fail — never an error to the client.

Implementation in `src/Server/Api.fs`: look up `SteamAppId` and `RawgId`, call both sources, concatenate. Keep `getGameTrailer` (singular) for back-compat with the existing "Play Trailer" button.

### Frontend

#### 3. Types (`src/Client/Pages/GameDetail/Types.fs`)

Add to `Model`:
```fsharp
Trailers: GameTrailerInfo list
IsLoadingTrailers: bool
PlayingTrailerUrl: string option   // which trailer is currently expanded/playing inline
FailedTrailerUrls: Set<string>     // trailers that fired <video> error — hide these
```

Add to `Msg`:
```fsharp
| Load_trailers
| Trailers_loaded of GameTrailerInfo list
| Trailers_failed of exn
| Play_trailer_inline of string   // url
| Stop_trailer_inline
| Trailer_errored of string       // url that failed to load
```

#### 4. State (`src/Client/Pages/GameDetail/State.fs`)

- On `Game_loaded`: dispatch `Load_trailers` in addition to the existing `Load_trailer`
- `Trailers_loaded`: store list, clear loading flag
- `Trailers_failed`: treat as empty list (optimistic: swallow the error, just don't render the section)
- `Play_trailer_inline url`: set `PlayingTrailerUrl = Some url`
- `Stop_trailer_inline`: set `PlayingTrailerUrl = None`
- `Trailer_errored url`: add to `FailedTrailerUrls`; if `PlayingTrailerUrl = Some url`, clear it

#### 5. Views (`src/Client/Pages/GameDetail/Views.fs`)

Render a new full-width section in the Overview, **above** the two-column grid (before line ~996).

**When to render:**
- Hide entirely if `Trailers` is empty after loading finishes
- Hide entirely if all trailers are in `FailedTrailerUrls`
- Skip individual trailers that are in `FailedTrailerUrls`
- Optional: show a subtle skeleton while `IsLoadingTrailers` is true; acceptable to show nothing

**Layout:**
- Section heading "Trailers" — use existing typography (Oswald `font-display`)
- Horizontal scrollable gallery (`overflow-x-auto`, flex row, `snap-x snap-mandatory`, gap between items)
- Each card is roughly 16:9, fixed width (e.g. 320px on desktop, 240px on mobile), rounded corners, subtle border
- Card content:
  - Thumbnail (`<img>` with `GameTrailerInfo.ThumbnailUrl`; fall back to a placeholder bg if missing)
  - Centered play-button overlay (existing play icon from `Components/Icons`)
  - Optional trailer title at the bottom (from `GameTrailerInfo.Title`) with a dark gradient for legibility
  - Glassmorphism treatment consistent with the rest of the page: semi-transparent bg, subtle border, inset highlight — see `.glass-card` in `index.css`

**Click-to-play behavior:**
- Click a card → dispatch `Play_trailer_inline url`
- Card swaps its thumbnail for an HTML5 `<video>` element with `controls`, `autoPlay`, `preload="metadata"`
- Clicking outside the playing card (or a small ✕ on the card) → `Stop_trailer_inline`
- `<video>` `onError` → dispatch `Trailer_errored url` (removes it from the gallery)
- Only one trailer plays at a time — starting a new one stops the current

**No autoplay on page load.** Thumbnails only until user clicks.

**Image failures:** if a `ThumbnailUrl` 404s, the card should still be clickable (show a solid-color placeholder with the play icon).

### Keep existing modal

The "Play Trailer" button + modal from task 018 stays. It becomes a shortcut to play the highlight/first trailer. The gallery is additive, not a replacement.

## Acceptance Criteria

- [ ] `getGameTrailers: string -> Async<GameTrailerInfo list>` added to `IMediathecaApi`
- [ ] Server returns all Steam movies (not just the highlight) and all RAWG videos, deduplicated by URL
- [ ] `GameDetail` page loads trailers alongside the singular trailer on `Game_loaded`
- [ ] Gallery section renders full-width above the two-column Overview layout
- [ ] Section has a heading ("Trailers") in display font
- [ ] Cards are horizontally scrollable; trackpad/wheel scroll works; scroll-snap feels natural
- [ ] Each card shows a thumbnail + play icon overlay + optional title
- [ ] Clicking a card plays the video inline in that card (not a modal)
- [ ] Only one trailer plays at a time
- [ ] No autoplay on page load — user must click
- [ ] A trailer whose `<video>` fires `error` is removed from the gallery immediately
- [ ] If no trailers are available at all, the section is not rendered (no empty "Trailers" heading)
- [ ] If the `getGameTrailers` request fails entirely (network, server down), the section is not rendered
- [ ] Styling follows the design system: glassmorphism on cards, Oswald heading, consistent spacing with other Overview sections
- [ ] Existing "Play Trailer" button + modal from task 018 still works
- [ ] `npm run build` succeeds; all existing tests pass

## Notes

- **Do not download videos.** Stream from Steam/RAWG CDN directly via `<video src=...>`.
- Steam CDN URLs come back as `http://` — existing `getSteamStoreTrailer` already upgrades to HTTPS; make sure the multi-trailer version does the same, otherwise browsers will block mixed content.
- Deduplicate Steam + RAWG trailers by `VideoUrl` when concatenating server-side. Steam should win if both sources have the same URL (unlikely in practice).
- Consider `preload="metadata"` on `<video>` so the browser only fetches headers until the user clicks — avoids prefetching MBs for every visible card.
- Future enhancement (not in scope): persist trailer metadata in the game projection to avoid round-tripping to Steam/RAWG on every page load. For now, live-fetch is fine.

## Work Log

### 2026-04-20 12:32 -- Work Completed

**What was done:**
- Added `Steam.getSteamStoreTrailers` returning all Steam movies (highlight first), refactored the singular `getSteamStoreTrailer` to share a `movieToTrailerInfo` helper; thumbnails and video URLs are both upgraded to HTTPS.
- Added `Rawg.getGameTrailersAll` returning every RAWG trailer and refactored `getGameTrailers` to share a `rawgResultToTrailerInfo` helper with HTTPS upgrades.
- Exposed `getGameTrailers: string -> Async<GameTrailerInfo list>` on `IMediathecaApi`; server implementation concatenates Steam + RAWG with URL-based dedup (Steam wins) and swallows all errors to `[]`.
- Extended `GameDetail.Types.Model` with `Trailers`, `IsLoadingTrailers`, `PlayingTrailerUrl`, `FailedTrailerUrls` and added `Load_trailers`, `Trailers_loaded`, `Trailers_failed`, `Play_trailer_inline`, `Stop_trailer_inline`, `Trailer_errored` messages.
- Updated `GameDetail.State` to dispatch `Load_trailers` on `Game_loaded`, store the list, treat network failures as empty, track inline play/stop, and remove a trailer when its `<video>` fires `error`.
- Added a full-width "Trailers" section in `GameDetail.Views` above the two-column Overview grid: horizontal scroll-snap gallery of `glass-card` cards with thumbnail + centered play button + optional title; clicking swaps to an inline `<video>` with `controls`, `autoPlay`, `preload="metadata"`, and a close button. Only one trailer plays at a time. Failed trailers are hidden. The entire section hides when the list is empty.
- Preserved the existing "Play Trailer" button + modal from task 018.

**Acceptance criteria status:**
- [x] `getGameTrailers: string -> Async<GameTrailerInfo list>` added to `IMediathecaApi` — verified via `src/Shared/Shared.fs` and build
- [x] Server returns all Steam movies and all RAWG videos, deduplicated by URL — implemented in `src/Server/Api.fs` with Steam-wins URL set
- [x] `GameDetail` loads trailers alongside singular trailer on `Game_loaded` — `State.fs` batches `Load_trailer` + `Load_trailers`
- [x] Gallery renders full-width above two-column Overview layout — new section inserted before the `grid lg:grid-cols-12` container
- [x] Heading uses display font — reuses `sectionHeader` helper (`font-display`)
- [x] Horizontally scrollable with snap — `overflow-x-auto snap-x snap-mandatory` on the flex row, `snap-start` per card
- [x] Thumbnail + play icon overlay + optional title — `<img>` with gradient overlay, centered play button, bottom title line
- [x] Clicking a card plays inline — `Play_trailer_inline url` dispatched; card swaps to `<video>`, not modal
- [x] Only one trailer plays at a time — `PlayingTrailerUrl` is a single `string option`
- [x] No autoplay on page load — thumbnails only until user click
- [x] Video `error` removes trailer from gallery — `onError` dispatches `Trailer_errored url`, filter excludes it
- [x] Empty trailer list hides whole section — `visibleTrailers` check guards the `Html.section`
- [x] Request failures hide section — `Trailers_failed` sets empty list, same guard applies
- [x] Glassmorphism + Oswald + spacing — `glass-card` class, `sectionHeader` display font, consistent `mb-10`
- [x] Existing "Play Trailer" button + modal still works — untouched
- [x] `npm run build` succeeds; `npm test` passes 233/233

**Files changed:**
- `src/Shared/Shared.fs` — added `getGameTrailers` to `IMediathecaApi`
- `src/Server/Steam.fs` — added `getSteamStoreTrailers`, extracted `movieToTrailerInfo` helper, HTTPS-upgraded thumbnails
- `src/Server/Rawg.fs` — added `getGameTrailersAll`, extracted `rawgResultToTrailerInfo` helper, HTTPS-upgraded URLs
- `src/Server/Api.fs` — implemented `getGameTrailers` endpoint (Steam + RAWG, URL dedup, swallows errors)
- `src/Client/Pages/GameDetail/Types.fs` — added 4 model fields and 6 messages
- `src/Client/Pages/GameDetail/State.fs` — initialize new fields, load trailers on `Game_loaded`, handle all new messages
- `src/Client/Pages/GameDetail/Views.fs` — added trailer gallery section above two-column grid
