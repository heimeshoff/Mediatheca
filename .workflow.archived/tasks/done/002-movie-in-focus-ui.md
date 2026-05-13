# Task: Movie In Focus — client UI

**ID:** 002
**Milestone:** M1 - In Focus
**Size:** Small
**Created:** 2026-02-19
**Dependencies:** 001-movie-in-focus-backend

## Objective
Users can toggle In Focus on movies from the detail page and see an indicator on the list page.

## Details

### Movie Detail Page (src/Client/Pages/MovieDetail/Views.fs)
- Add an "In Focus" toggle button in the hero section (near the existing action buttons like Play Trailer, Add to Catalog)
- When In Focus: filled/highlighted icon with "In Focus" label
- When not In Focus: outline icon with "Set In Focus" label
- Clicking toggles via `setMovieInFocus` API call
- Use a star, spotlight, or crosshair icon — pick what fits the glassmorphism design

### Movie List Page (src/Client/Pages/Movies/Views.fs)
- Show a small "In Focus" badge or icon overlay on poster cards for movies that have `InFocus = true`
- Should be subtle but visible (e.g., small icon in corner of poster)

### State (src/Client/Pages/MovieDetail/Types.fs, State.fs)
- Add `SetInFocus of bool` message
- Handle API call in State.fs, update model on success

## Acceptance Criteria
- [x] Toggle button visible on movie detail page
- [x] Toggle calls API and updates UI state
- [x] Visual indicator on movie list page for In Focus movies
- [x] Follows glassmorphism design system

## Notes
- Check DesignSystem.fs for appropriate component patterns
- The toggle should feel like a "pin" or "spotlight" action

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete
**Files modified:**
- `src/Client/Components/Icons.fs` — Added `crosshairFilled`, `crosshairOutline`, and `crosshairSmFilled` icon functions for the In Focus feature
- `src/Client/Pages/MovieDetail/Types.fs` — Added `Set_in_focus of bool` and `In_focus_result of Result<unit, string>` messages to `Msg` union
- `src/Client/Pages/MovieDetail/State.fs` — Added handler for `Set_in_focus` (calls `api.setMovieInFocus`) and `In_focus_result` (reloads movie on success, sets error on failure)
- `src/Client/Pages/MovieDetail/Views.fs` — Added In Focus toggle button in hero section action buttons row, next to Play Trailer. Uses filled crosshair with primary styling when active ("In Focus"), outline crosshair with subtle glassmorphism styling when inactive ("Set In Focus")
- `src/Client/Pages/Movies/Views.fs` — Added `inFocusBadge` overlay (small primary-colored crosshair icon in top-right corner of poster card) rendered when `movie.InFocus = true`

**Build:** `npm run build` passed with no errors.

**Design decisions:**
- Used crosshair icon (spotlight/target metaphor) to convey "In Focus" concept
- Hero toggle uses rounded pill buttons matching existing Play Trailer style
- Active state: `bg-primary/90` with filled crosshair icon and "In Focus" label
- Inactive state: `bg-base-content/10` with `backdrop-blur-sm` and outline crosshair icon and "Set In Focus" label
- List page badge: small 24px circle with `bg-primary/80 backdrop-blur-sm` and `border-primary/30` — follows glassmorphism conventions
- Reused existing `ratingBadge` slot on PosterCard to avoid modifying the shared component
