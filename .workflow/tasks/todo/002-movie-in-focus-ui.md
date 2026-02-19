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
- [ ] Toggle button visible on movie detail page
- [ ] Toggle calls API and updates UI state
- [ ] Visual indicator on movie list page for In Focus movies
- [ ] Follows glassmorphism design system

## Notes
- Check DesignSystem.fs for appropriate component patterns
- The toggle should feel like a "pin" or "spotlight" action

## Work Log
<!-- Appended by /work during execution -->
