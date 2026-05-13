# Task: Show Finished Series in Next Up for 7 Days

**ID:** 023
**Milestone:** --
**Size:** Small
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Finished TV series (all episodes watched, not abandoned) should remain visible in the dashboard Next Up section for 7 days after the last watch date, even when they are not In Focus. Currently they disappear immediately unless In Focus is set.

## Details

### Current Behavior

The `getDashboardSeriesNextUp` query in `src/Server/SeriesProjection.fs` includes series where:

```sql
WHERE sl.next_up_season IS NOT NULL OR sl.in_focus = 1 OR sl.abandoned = 1
```

A finished series without InFocus has `next_up_season = NULL`, `in_focus = 0`, `abandoned = 0` — so it's excluded entirely.

### Change

Extend the `WHERE` clause to also include recently-finished series:

```sql
WHERE sl.next_up_season IS NOT NULL
   OR sl.in_focus = 1
   OR sl.abandoned = 1
   OR (sl.episode_count > 0
       AND sl.watched_episode_count >= sl.episode_count
       AND sl.abandoned = 0
       AND (SELECT MAX(watched_date) FROM series_episode_progress
            WHERE series_slug = sl.slug) >= date('now', '-7 days'))
```

This single SQL change is the entire implementation. No shared types, client code, or API changes needed — the `DashboardSeriesNextUp` DTO already has `IsFinished` and `LastWatchedDate`, and the client already renders a green "Finished" badge for finished series.

### Files Changed

1. **`src/Server/SeriesProjection.fs`** — Update `getDashboardSeriesNextUp` WHERE clause

### Behavior After Change

| Series state | In Focus | Last watched | Shown in Next Up? |
|---|---|---|---|
| Has unwatched episodes | any | any | Yes (unchanged) |
| Finished | Yes | any | Yes (unchanged — InFocus keeps it visible) |
| Finished | No | ≤7 days ago | **Yes (NEW)** |
| Finished | No | >7 days ago | No |
| Abandoned | any | any | Yes (unchanged) |

## Acceptance Criteria

- [x] Finished series appear in dashboard Next Up for 7 days after last watch date
- [x] After 7 days, finished non-InFocus series drop off the Next Up list
- [x] InFocus finished series still appear indefinitely (no regression)
- [x] Abandoned series still appear regardless of watch date (no regression)
- [x] Series with unwatched episodes still appear as before (no regression)
- [x] All existing tests pass

## Work Log

**2026-02-24** — Extended `getDashboardSeriesNextUp` WHERE clause in `src/Server/SeriesProjection.fs` (line 1050) to include finished, non-abandoned series whose last watched episode is within the past 7 days. The new OR condition checks `episode_count > 0 AND watched_episode_count >= episode_count AND abandoned = 0` plus a subquery on `series_episode_progress.watched_date >= date('now', '-7 days')`. All 233 existing tests pass. Client build succeeds.
