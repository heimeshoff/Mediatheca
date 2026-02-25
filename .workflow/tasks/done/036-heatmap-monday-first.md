# Task: Activity Heatmap Monday-First Weeks

**ID:** 036
**Milestone:** --
**Size:** Small
**Created:** 2026-02-25
**Dependencies:** None

## Objective

Change the activity heatmap (Git-like contribution graph) on the Dashboard "All" tab so that weeks start on Monday (row 0) and end on Sunday (row 6), following the ISO 8601 / European convention. Currently weeks start on Sunday and end on Saturday.

## Current State

- `activityHeatmapContent` in `src/Client/Pages/Dashboard/Views.fs` (line 1417)
- Start date is aligned to Sunday via `int startDate.DayOfWeek` (Sunday = 0 in .NET `DayOfWeek` enum)
- Day-of-week labels show Mon/Wed/Fri at row indices 1/3/5 (because Sunday is row 0)
- Each week column runs Sunday through Saturday

## Details

All changes are in `src/Client/Pages/Dashboard/Views.fs`, function `activityHeatmapContent`:

1. **Adjust start-date alignment (lines 1432-1434):** Change the offset calculation so it snaps to Monday instead of Sunday. In .NET `DayOfWeek`, Monday = 1, Sunday = 0. The adjustment needs to map Monday → 0, Tuesday → 1, ..., Sunday → 6. Formula: `(int dayOfWeek + 6) % 7` gives the Monday-based offset.

2. **Update day-of-week labels (lines 1472-1489):** Since Monday is now row 0, the labels need to shift:
   - Row 0 = Monday → show "Mon" at row 0 (currently at row 1)
   - Row 2 = Wednesday → show "Wed" at row 2 (currently at row 3)
   - Row 4 = Friday → show "Fri" at row 4 (currently at row 5)

3. **No server changes needed** — activity data is per-date, day-of-week grouping is purely client-side.

## Acceptance Criteria

- [ ] Heatmap first row (row 0) is Monday
- [ ] Heatmap last row (row 6) is Sunday
- [ ] Day labels Mon/Wed/Fri are positioned correctly for the new row mapping
- [ ] Month labels still appear correctly at week boundaries
- [ ] `npm run build` compiles successfully

## Files to Modify

- `src/Client/Pages/Dashboard/Views.fs` — `activityHeatmapContent` function only

## Work Log

### 2026-02-25 -- Work Completed

**What was done:**
- Changed start-date alignment from Sunday to Monday using `(int dayOfWeek + 6) % 7` formula
- Updated day-of-week label row indices from 1/3/5 to 0/2/4 (Mon/Wed/Fri now at correct rows)
- Updated comments to reflect Monday-first week convention

**Acceptance criteria status:**
- [x] Heatmap first row (row 0) is Monday -- offset formula `(int dayOfWeek + 6) % 7` maps Monday to 0
- [x] Heatmap last row (row 6) is Sunday -- Sunday maps to 6 in the new formula
- [x] Day labels Mon/Wed/Fri are positioned correctly for the new row mapping -- labels at rows 0/2/4
- [x] Month labels still appear correctly at week boundaries -- month label logic is unchanged, uses first day of each week chunk
- [x] `npm run build` compiles successfully -- verified, built in 31.70s

**Files changed:**
- `src/Client/Pages/Dashboard/Views.fs` -- Changed `activityHeatmapContent`: Monday-first week alignment, updated day label positions, updated comments
