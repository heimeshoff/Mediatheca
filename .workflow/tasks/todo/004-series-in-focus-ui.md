# Task: Series In Focus — client UI

**ID:** 004
**Milestone:** M1 - In Focus
**Size:** Small
**Created:** 2026-02-19
**Dependencies:** 003-series-in-focus-backend

## Objective
Users can toggle In Focus on TV series from the detail page and see an indicator on the list page.

## Details

### Series Detail Page (src/Client/Pages/SeriesDetail/Views.fs)
- Add an "In Focus" toggle button in the hero section (same pattern as Movie detail)
- When In Focus: filled/highlighted icon with "In Focus" label
- When not In Focus: outline icon with "Set In Focus" label
- Clicking toggles via `setSeriesInFocus` API call

### Series List Page (src/Client/Pages/Series/Views.fs)
- Show a small "In Focus" badge or icon overlay on poster cards for series with `InFocus = true`
- Same visual treatment as Movies list

### State
- Add `SetInFocus of bool` message to Series detail messages
- Handle API call, update model on success

## Acceptance Criteria
- [ ] Toggle button visible on series detail page
- [ ] Toggle calls API and updates UI state
- [ ] Visual indicator on series list page
- [ ] Consistent with Movie In Focus UI treatment

## Notes
- Use the same icon/styling as Movie In Focus for consistency across media types

## Work Log
<!-- Appended by /work during execution -->
