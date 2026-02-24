# Task: TV Series Tab Overhaul

**ID:** 033
**Milestone:** M5 (Dashboard V3)
**Size:** Medium
**Created:** 2026-02-25
**Dependencies:** 032

## Objective

Restructure the TV Series tab by removing Episode Activity, converting Next Up to a poster scroller, rearranging the layout, and replacing the genre bar chart with a pie chart (reusing the shared component from 032).

## Details

1. **Remove Episode Activity card** — Delete entirely.

2. **Layout restructure (top to bottom):**

   **Row 1:** Next Up — full-width horizontal poster scroller
   - Poster cards with series poster, extra info below (episode label like S2E5, progress, companions).
   - **Exclude abandoned series.**
   - Show generous count (10+).

   **Row 2:** Recently Finished (50%) | Recently Abandoned (50%)
   - `grid grid-cols-1 md:grid-cols-2 gap-4`
   - Both sorted by **last watch date** (most recent first).

   **Row 3:** Monthly Activity | Ratings Distribution | Genre Breakdown (pie chart)
   - `grid grid-cols-1 md:grid-cols-3 gap-4` or stacked as appropriate.
   - Genre Breakdown as **pie chart** (reuse shared component from Charts.fs).

3. **Recently Finished and Recently Abandoned:**
   - Keep existing list format but ensure sorted by last watch date.
   - May need backend query adjustment if currently sorted differently.

## Acceptance Criteria

- [ ] Episode Activity card is removed
- [ ] Next Up is a full-width horizontal poster scroller with info below
- [ ] Abandoned series excluded from Next Up
- [ ] Recently Finished and Recently Abandoned are side-by-side
- [ ] Both sorted by last watch date
- [ ] Monthly Activity, Ratings Distribution, Genre Breakdown in a row
- [ ] Genre Breakdown is a pie chart (using shared Charts.fs component)
- [ ] All existing tests pass
