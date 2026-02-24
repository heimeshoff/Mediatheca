# Task: Games Tab Overhaul

**ID:** 034
**Milestone:** M5 (Dashboard V3)
**Size:** Large
**Created:** 2026-02-25
**Dependencies:** 033

## Objective

Restructure the Games tab with a new In-Focus Estimate hero card, convert lists to poster scrollers, replace charts with pie and spider/radar visualizations, and add per-game color-coded monthly play time.

## Details

1. **Backlog Estimate → In-Focus Estimate:**
   - Rename and rework the hero card.
   - Show estimated remaining time for **In-Focus games only** to reach main storyline (HLTB "Main Story" time).
   - Calculation: For each In-Focus game, `max(0, hltb_main - played_minutes)`. Clamp negatives to 0 (if played longer than HLTB suggests, don't count negative).
   - Sum all clamped remainders.
   - Display as "Xh remaining" or similar.

2. **Layout restructure (top to bottom):**

   **Row 1:** In-Focus Estimate hero card (full width)

   **Row 2:** Recently Played | Recently Added
   - Both as horizontal poster scrollers (movie-poster style cards).
   - Recently Played **includes finished games**.
   - **Exclude dismissed games** from both lists.

   **Row 3:** Status Distribution (pie chart) | Genre Breakdown (spider/radar graph)
   - `grid grid-cols-1 md:grid-cols-2 gap-4`
   - Status Distribution: convert from stacked bar to **pie chart** (reuse shared Charts.fs component).
   - Genre Breakdown: convert from horizontal bars to **spider/radar chart** (new component in Charts.fs).

   **Row 4:** Monthly Play Time (2/3 left) | Recent Achievements (1/3 right)
   - `grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4`
   - Monthly Play Time: stacked/grouped bar chart showing **individual games color-coded** (not just a single "games" total).
   - Recent Achievements: existing Steam achievements section, moved here.

3. **Monthly Play Time — per-game color coding:**
   - Each game gets its own color in the bar chart.
   - Show which games were played each month as stacked segments.
   - Legend maps colors to game names.
   - May need backend changes to return per-game monthly data instead of aggregated totals.

## Acceptance Criteria

- [ ] In-Focus Estimate shows clamped remaining HLTB time for In-Focus games
- [ ] Recently Played includes finished games, excludes dismissed
- [ ] Recently Added excludes dismissed games
- [ ] Both are horizontal poster scrollers
- [ ] Status Distribution is a pie chart (using shared Charts.fs component)
- [ ] Genre Breakdown is a spider/radar chart (new Charts.fs component)
- [ ] Monthly Play Time shows per-game color-coded bars
- [ ] Recent Achievements is in the right column
- [ ] Monthly Play Time is in the left 2/3
- [ ] All existing tests pass
