# Task: TV Series Dashboard Stats & Visualizations

**ID:** 025
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** None
**Research:** research/dashboard-content-research.md

## Objective

Expand the TV Series dashboard tab with per-series progress bars, time remaining estimates, episode activity charts, genre breakdown, ratings distribution, upcoming episode air dates, and a "most watched with" friends section.

## Current State

The TV Series tab currently shows:
- **Stats badges:** Total Series, Episodes Watched, Total Watch Time
- **Next Up:** Full list of series with next episode info
- **Recently Finished:** Finished series with badge
- **Recently Abandoned:** Abandoned series with badge

## Details

### 1. New Stats Badges

Add to the existing badge row:
- **Currently Watching** — count of series with unwatched episodes (active, not abandoned)
- **Average Rating** — mean of all personal series ratings
- **Completion Rate** — percentage of started series that are finished (excluding abandoned)

### 2. Per-Series Progress Bars in Next Up

Enhance each series card in the Next Up list with a visual progress indicator.

- **Progress bar:** Thin bar below the series poster showing `watched_episode_count / episode_count`
- **Label:** "S2E5 / 24 episodes" or "12 of 24 episodes (50%)"
- **Color:** Primary for watched portion, muted for remaining
- **Already available data:** `WatchedEpisodeCount` and `EpisodeCount` already exist on `DashboardSeriesNextUp` DTO

**Implementation:** Client-side only — no backend changes needed. Add a progress bar to the existing Next Up card component.

### 3. Time Remaining Per Series

Show estimated time to complete each active series.

- **Calculation:** `(total_episodes - watched_episodes) * average_episode_runtime`
- **Display:** "~8h remaining" or "~2h 30m remaining" on the Next Up card
- **Requires:** Average episode runtime — may need to add this to the DTO

**Backend:** Add `AverageRuntimeMinutes: int option` to `DashboardSeriesNextUp` DTO:
```sql
SELECT AVG(runtime) as avg_runtime
FROM series_episode
WHERE series_slug = ?
```

Or compute from existing episode data in the projection query.

### 4. Episode Activity Chart

A bar chart similar to the existing 14-day games play activity chart, showing episodes watched per day over the last 14 days (or 30 days).

- **X-axis:** Days
- **Y-axis:** Episode count per day
- **Color-coding:** Different colors per series (matching the games chart style)
- **Tooltip:** Series name, episode details on hover

**Backend:** New query:
```sql
SELECT sep.watched_date, s.name, s.slug, COUNT(*) as episode_count
FROM series_episode_progress sep
JOIN series_list s ON sep.series_slug = s.slug
WHERE sep.watched_date >= date('now', '-14 days')
  AND sep.watched_date IS NOT NULL
GROUP BY sep.watched_date, s.slug
ORDER BY sep.watched_date
```

**New DTO:**
```fsharp
type DashboardEpisodeActivity = {
    Date: string
    SeriesName: string
    SeriesSlug: string
    EpisodeCount: int
}
```

### 5. Ratings Distribution Chart

Bar chart of series ratings (1–10), same pattern as Movies task 024.

**Backend:**
```sql
SELECT personal_rating, COUNT(*) as count
FROM series_detail
WHERE personal_rating IS NOT NULL
GROUP BY personal_rating
ORDER BY personal_rating
```

### 6. Genre Breakdown

Horizontal bar chart of top 10 genres across all series, same pattern as Movies.

**Backend:** Aggregate genres from all series (stored as JSON arrays in series_list/series_detail).

### 7. Monthly Episode Activity

Bar chart showing episodes watched per month over the last 12 months.

**Backend:**
```sql
SELECT strftime('%Y-%m', watched_date) as month, COUNT(*) as episodes
FROM series_episode_progress
WHERE watched_date >= date('now', '-12 months')
  AND watched_date IS NOT NULL
GROUP BY month
ORDER BY month
```

### 8. Binge Detection Highlights

Flag days where 3+ episodes of the same series were watched — these are "binge sessions."

- **Display:** Small "binge" badge or flame icon on the activity chart for binge days
- **Threshold:** 3+ episodes of the same series in one day
- **Source:** Same episode activity query, post-processed client-side

### 9. Most Watched With (Friends)

Top friends from rewatch sessions, by shared episode count.

- **Display:** Friend name, friend image, shared episode count
- **Limit:** Top 5
- **Source:** Rewatch session data with friend associations

### 10. Upcoming Episodes

A section showing the next air dates for in-progress series.

**Requires new data:**

1. **Store air dates from TMDB** — When importing series, persist `air_date` for future episodes. TMDB provides `air_date` on each episode object.

2. **Periodic refresh** — Either:
   - Refresh air dates when the user views the series detail page (lazy)
   - Background job that checks TMDB for tracked series periodically
   - **Recommendation:** Lazy refresh on series detail view + cache for dashboard

3. **Add `next_episode_air_date` to series data:**
   ```sql
   -- During TMDB import/refresh, store air dates on episodes
   ALTER TABLE series_episode ADD COLUMN air_date TEXT
   ```

4. **Dashboard query:**
   ```sql
   SELECT s.slug, s.name, s.poster_ref, e.season_number, e.episode_number, e.name as ep_name, e.air_date
   FROM series_list s
   JOIN series_episode e ON e.series_slug = s.slug
   WHERE e.air_date >= date('now')
     AND e.air_date <= date('now', '+30 days')
     AND s.abandoned = 0
   ORDER BY e.air_date ASC
   LIMIT 10
   ```

**Display:**
- Series poster, name, episode identifier (S03E05)
- Air date with countdown ("in 3 days", "tomorrow", "today")
- Grouped by date

**New DTO:**
```fsharp
type DashboardUpcomingEpisode = {
    SeriesSlug: string
    SeriesName: string
    PosterRef: string option
    SeasonNumber: int
    EpisodeNumber: int
    EpisodeName: string
    AirDate: string
}
```

### 11. Layout

Suggested layout order for the TV Series tab:
1. Stats badges row (existing + new: currently watching, avg rating, completion rate)
2. Episode activity chart (14-day)
3. Per-series Next Up with progress bars and time remaining (existing section, enhanced)
4. Upcoming episodes (next 30 days)
5. Monthly episode activity chart
6. Ratings distribution chart
7. Genre breakdown bars
8. Most watched with (friends)
9. Binge highlights (integrated into activity chart)
10. Recently Finished (existing)
11. Recently Abandoned (existing)

## Files Changed

1. **`src/Shared/Shared.fs`** — New/expanded DTOs
2. **`src/Server/SeriesProjection.fs`** — New queries, air date storage, episode activity
3. **`src/Server/Tmdb.fs`** — Persist episode air dates during import
4. **`src/Server/Api.fs`** — Update `getDashboardSeriesTab` with new data
5. **`src/Client/Pages/Dashboard/Views.fs`** — New chart components and enhanced Next Up cards
6. **`src/Client/Pages/Dashboard/Types.fs`** — Model updates
7. **`src/Client/Pages/Dashboard/State.fs`** — New Msg variants

## Acceptance Criteria

- [ ] Stats badges show Currently Watching, Average Rating, and Completion Rate
- [ ] Next Up cards display progress bars (watched / total episodes)
- [ ] Next Up cards show estimated time remaining
- [ ] Episode activity chart shows episodes per day for last 14 days (color-coded by series)
- [ ] Ratings distribution bar chart displays correctly (1–10 scale)
- [ ] Genre breakdown shows top 10 genres as horizontal bars
- [ ] Monthly activity chart shows episodes watched per month (last 12 months)
- [ ] Binge days (3+ episodes of same series) are highlighted
- [ ] Most Watched With shows top 5 friends from rewatch sessions
- [ ] Upcoming episodes section shows next air dates for tracked series
- [ ] Episode air dates are stored from TMDB and refreshable
- [ ] All charts follow existing SVG/Feliz pattern
- [ ] All sections handle empty state gracefully
- [ ] Mobile layout stacks vertically
- [ ] All existing tests pass
