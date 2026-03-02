# Task 038: IPTorrents Search Button

**Status:** Done
**Size:** Small
**Milestone:** --
**Dependencies:** None

## Description

When a movie or TV series does NOT have a Jellyfin play button (i.e., `JellyfinId` is `None`), show an "IPTorrents" search button that opens the IPTorrents search page in a new tab with the title pre-filled and categories pre-filtered.

This is purely client-side — no server changes, no scraping, no API integration. Just a smart link.

## Acceptance Criteria

- [x] **Movie detail page:** In the hero action buttons row (line ~985 of MovieDetail/Views.fs), when `JellyfinId` is `None`, show a "Search on IPTorrents" button styled as a rounded pill (matching the existing action button style)
- [x] **Series detail page:** In the hero action buttons row (line ~1658 of SeriesDetail/Views.fs), add a "Search on IPTorrents" button (series detail has no Jellyfin button today, so always show it unless a Jellyfin integration is added later)
- [x] **URL format:** `https://iptorrents.com/t?q={url-encoded name};o=seeders#torrents` — pre-sorted by seeders descending, anchored to the torrents table
- [x] Button opens in a new tab (`target="_blank"`, `rel="noopener noreferrer"`)
- [x] Button uses a download/search icon (not the play icon) to visually distinguish from Jellyfin
- [x] Button style: secondary/muted pill style (e.g., `bg-base-content/10 hover:bg-base-content/20`) to differentiate from the primary Jellyfin play button — it's a fallback action, not the primary one

## Technical Notes

### URL Construction

```fsharp
// Same URL for both movies and series — no category filtering, sorted by seeders
let iptorrentsSearchUrl name = $"https://iptorrents.com/t?q={encodeURIComponent name};o=seeders#torrents"
```

### Placement Logic (Movie Detail)

```fsharp
// Current: only Jellyfin button
match model.JellyfinServerUrl, movie.JellyfinId with
| Some serverUrl, Some jellyfinId -> // Jellyfin button
| _ -> ()

// New: Jellyfin OR IPTorrents
match model.JellyfinServerUrl, movie.JellyfinId with
| Some serverUrl, Some jellyfinId -> // Jellyfin play button (unchanged)
| _ -> // IPTorrents search button (new)
```

### Placement Logic (Series Detail)

Series detail currently has no Jellyfin button. Add the IPTorrents button in the action buttons row alongside "Play Trailer" and "In Focus" toggle.

### Files to Modify

- `src/Client/Pages/MovieDetail/Views.fs` — add IPTorrents button in the `| _ ->` branch of the Jellyfin match
- `src/Client/Pages/SeriesDetail/Views.fs` — add IPTorrents button in the action buttons row

### No Server Changes

This is a pure client-side feature. The movie/series name is already available in the detail model. URL encoding can use `Fable.Core.JS.encodeURIComponent` or `System.Uri.EscapeDataString`.

## Future Considerations

- Could later add qBittorrent integration to send .torrent files directly from Mediatheca
- Could add Prowlarr/Jackett middleware for in-app search results
- Series detail may eventually get Jellyfin integration — at that point, add the same conditional logic as movies

## Work Log

**2026-03-02:** Implemented IPTorrents search button on movie and series detail pages.

### Changes Made

1. **`src/Client/Components/Icons.fs`** -- Added `magnifyingGlass` icon (Heroicons magnifying glass, small variant) to visually distinguish from the Jellyfin play icon.

2. **`src/Client/Pages/MovieDetail/Views.fs`** -- Replaced the `| _ -> ()` branch of the Jellyfin match with an IPTorrents search button. When a movie has no JellyfinId, the button appears as a muted pill linking to `https://iptorrents.com/t?q={url-encoded name};o=seeders#torrents`. Uses `System.Uri.EscapeDataString` for URL encoding (consistent with existing GameDetail pattern), `target="_blank"`, `rel="noopener noreferrer"`.

3. **`src/Client/Pages/SeriesDetail/Views.fs`** -- Added an IPTorrents search button in the action buttons row, between the trailer button and the In Focus toggle. Always visible since series detail has no Jellyfin integration yet.

### Acceptance Criteria Status
All 6 acceptance criteria met. Build verified with `npm run build` -- compiles successfully.

### Files Changed
- `src/Client/Components/Icons.fs`
- `src/Client/Pages/MovieDetail/Views.fs`
- `src/Client/Pages/SeriesDetail/Views.fs`
