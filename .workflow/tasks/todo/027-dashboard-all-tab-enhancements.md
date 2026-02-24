# Task: Dashboard All Tab Enhancements

**ID:** 027
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** Soft dependency on 024, 025, 026 (shared chart components)
**Research:** research/dashboard-content-research.md

## Objective

Enhance the Dashboard "All" tab with a cross-media hero stats section, weekly activity summary, GitHub-style activity heatmap, and cross-media monthly breakdown chart. The All tab should answer "what's my media life looking like?" at a glance.

## Current State

The All tab currently shows:
- **Hero Spotlight:** First active series with next episode details
- **Next Up Scroller:** Series next up (6 items)
- **Movies In Focus:** Poster cards (6 items)
- **Games Recently Played:** 14-day stacked bar chart + play time
- **Games In Focus:** Poster cards
- **New Games:** Recently added (10 items)

## Details

### 1. Cross-Media Hero Stats

A prominent stats section at the top showing combined media consumption metrics.

**Display:** Large, visually distinct cards or a hero banner with:
- **Total Media Time** — combined movie watch time + series watch time + game play time, displayed as "X days, Y hours" or a human-friendly format
- **This Year:** Movies watched, episodes watched, games beaten (current year counts)
- **This Month:** Quick counts for the current month
- **Active Now:** Currently watching (series count) + currently playing (games count)

**Backend:** New API endpoint or expanded `getDashboardAllTab`:
```fsharp
type DashboardCrossMediaStats = {
    TotalMovieMinutes: int
    TotalSeriesMinutes: int
    TotalGameMinutes: int
    MoviesWatchedThisYear: int
    EpisodesWatchedThisYear: int
    GamesBeatenThisYear: int
    MoviesWatchedThisMonth: int
    EpisodesWatchedThisMonth: int
    GamesPlayedThisMonth: int
    ActiveSeriesCount: int
    ActiveGamesCount: int
}
```

**Backend queries:**
```sql
-- Total time per media type (existing queries, combined)
-- This year counts
SELECT COUNT(DISTINCT movie_slug) FROM watch_session WHERE date >= strftime('%Y-01-01', 'now')
SELECT COUNT(*) FROM series_episode_progress WHERE watched_date >= strftime('%Y-01-01', 'now')
SELECT COUNT(*) FROM game_detail WHERE status = 'Completed' -- need completion date tracking
-- This month counts
-- similar with date >= strftime('%Y-%m-01', 'now')
-- Active counts
SELECT COUNT(*) FROM series_list WHERE next_up_season IS NOT NULL AND abandoned = 0
SELECT COUNT(*) FROM game_detail WHERE status = 'Playing'
```

### 2. Weekly Activity Summary

A text-based summary like: "This week: 4 episodes, 2 movies, 6h of gaming"

- **Period:** Last 7 days
- **Format:** Natural language, concise
- **Placement:** Below hero stats or as a subtitle
- **Adaptive:** Only mentions media types with activity ("This week: 3 episodes" if no movies or games)

### 3. Activity Heatmap (GitHub-style)

A calendar grid showing media activity per day across all types.

**Display:**
- **Grid:** 52 weeks x 7 days (or last 365 days), similar to GitHub contribution graph
- **Color intensity:** Based on total activity count per day (0 = empty, 1 = light, 2-3 = medium, 4+ = dark)
- **Activity sources per day:**
  - Movies: count of watch sessions
  - TV: count of episodes watched
  - Games: count of play sessions (or binary: played/didn't play)
- **Tooltip on hover:** "Feb 15: 2 episodes, 1 movie"
- **Legend:** Color scale from no activity to high activity
- **Day labels:** Mon, Wed, Fri on Y-axis
- **Month labels:** Jan–Dec on X-axis

**Color approach options:**
- **Single color with intensity** (GitHub-style: primary color at different opacities)
- **Multi-color segments** per media type (more complex, shows composition)
- **Recommendation:** Start with single-color intensity for simplicity

**Backend:** New query aggregating all activity by date:
```sql
-- Movies
SELECT date, COUNT(*) as count FROM watch_session
WHERE date >= date('now', '-365 days')
GROUP BY date

-- Episodes
SELECT watched_date as date, COUNT(*) as count FROM series_episode_progress
WHERE watched_date >= date('now', '-365 days') AND watched_date IS NOT NULL
GROUP BY watched_date

-- Games
SELECT date, COUNT(DISTINCT game_slug) as count FROM play_session
WHERE date >= date('now', '-365 days')
GROUP BY date
```

Merge and sum on the client or server side.

**New DTO:**
```fsharp
type DashboardActivityDay = {
    Date: string
    MovieSessions: int
    EpisodesWatched: int
    GameSessions: int
}
```

**SVG Implementation:**
- Each day is a small rect (e.g., 12x12px with 2px gap)
- 7 rows (days of week) x ~53 columns (weeks)
- Total size: ~700x100px, scales well on mobile
- Follow existing custom SVG pattern with Feliz

### 4. Cross-Media Monthly Stacked Bar Chart

A stacked bar chart showing monthly activity across all media types.

- **X-axis:** Last 12 months
- **Y-axis:** Hours consumed
- **Stacks:** Movies (one color), TV episodes (another), Games (third)
- **Each stack segment:** Shows hours for that media type in that month
- **Tooltip:** Breakdown per media type on hover

**Backend:** Combine monthly data from all three media types (movies by watch session hours, series by episode count * avg runtime, games by play session minutes).

**New DTO:**
```fsharp
type DashboardMonthlyBreakdown = {
    Month: string
    MovieMinutes: int
    SeriesMinutes: int
    GameMinutes: int
}
```

### 5. Layout

Suggested layout order for the All tab:
1. **Cross-media hero stats** (total time, this year/month counts, active now)
2. **Weekly activity summary** (natural language)
3. **Activity heatmap** (365 days)
4. **Hero Series Spotlight** (existing, keep)
5. **Series Next Up Scroller** (existing, keep)
6. **Movies In Focus** (existing, keep)
7. **Cross-media monthly chart** (stacked bars)
8. **Games play activity chart** (existing 14-day chart, keep)
9. **Games In Focus** (existing, keep)
10. **New Games** (existing, keep)

The new sections slot in at the top and middle, preserving all existing content below.

### 6. Shared Chart Components

Since tasks 024, 025, 026, and 027 all build charts with SVG/Feliz, consider extracting shared helpers:

- `renderBarChart` — configurable bar chart component
- `renderHorizontalBars` — for genre/platform breakdowns
- `renderDonutChart` — for status distribution
- `renderHeatmap` — for the activity calendar

These could live in a new `src/Client/Components/Charts.fs` module to avoid duplicating chart rendering logic across dashboard views.

**Note:** This is a recommendation, not a hard requirement. If each chart is sufficiently different, inline rendering in Views.fs is fine.

## Files Changed

1. **`src/Shared/Shared.fs`** — New DTOs for cross-media stats, activity data, monthly breakdown
2. **`src/Server/Api.fs`** — Expand `getDashboardAllTab` or add new endpoint
3. **`src/Server/MovieProjection.fs`** — Monthly movie activity query
4. **`src/Server/SeriesProjection.fs`** — Monthly episode activity query
5. **`src/Server/GameProjection.fs`** — Monthly game activity query (may already exist for 14-day chart)
6. **`src/Client/Pages/Dashboard/Views.fs`** — New hero stats, heatmap, stacked chart, weekly summary
7. **`src/Client/Pages/Dashboard/Types.fs`** — Model updates for new data
8. **`src/Client/Pages/Dashboard/State.fs`** — Load new data on tab init
9. **`src/Client/Components/Charts.fs`** — Optional shared chart helpers

## Acceptance Criteria

- [ ] Cross-media hero stats show total time, this-year counts, this-month counts, active counts
- [ ] Weekly activity summary shows natural language recap of last 7 days
- [ ] Activity heatmap displays 365 days of cross-media activity as a calendar grid
- [ ] Heatmap tooltip shows per-day breakdown by media type
- [ ] Cross-media monthly stacked bar chart shows 12 months of movies + TV + games hours
- [ ] All existing All tab sections (hero spotlight, next up, in focus, play chart, new games) remain intact
- [ ] New sections are positioned above existing content (hero stats, heatmap at top)
- [ ] Heatmap renders correctly on mobile (horizontally scrollable if needed)
- [ ] All charts follow existing SVG/Feliz pattern
- [ ] Empty states handled gracefully (new users with no activity)
- [ ] All existing tests pass
