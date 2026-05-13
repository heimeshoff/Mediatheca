# Task: Games Dashboard Stats & Visualizations

**ID:** 026
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** None
**Research:** research/dashboard-content-research.md

## Objective

Expand the Games dashboard tab with a status distribution chart, backlog time estimate, completion rate, genre/platform breakdown, ratings distribution, monthly play time trend, and HLTB comparison visualization.

## Current State

The Games tab currently shows:
- **Stats badges:** Total Games, Total Play Time, Games Completed, Games In Progress
- **Recently Added:** 10 most recently added games (cover, name, year)
- **Recently Played:** 10 games with recent play sessions (cover, playtime, last played, HLTB hours)
- **Steam Achievements:** Recent Steam achievements (lazy-loaded)

## Details

### 1. New Stats Badges

Add to the existing badge row:
- **Backlog Size** — count of games with Backlog status
- **Completion Rate** — percentage of non-Backlog games that are Completed (motivating metric)
- **Average Rating** — mean of all personal game ratings
- **Total Backlog Time** — sum of HLTB "Main Story" hours for all Backlog + InFocus games (answers "how long to clear my backlog?")

### 2. Status Distribution Chart

A donut/ring chart or horizontal stacked bar showing games per status.

- **Statuses:** Backlog, InFocus, Playing, Completed, Abandoned, OnHold, Dismissed
- **Color-coding:** Match existing badge colors per status
- **Label:** Status name + count + percentage
- **Interactive:** Hover/tap shows exact count
- **Style:** Either donut chart (like Backloggd) or stacked horizontal bar (simpler to implement with existing SVG pattern)

**Backend:**
```sql
SELECT status, COUNT(*) as count
FROM game_detail
GROUP BY status
```

**New DTO field:**
```fsharp
StatusDistribution: (string * int) list  // (status, count) pairs
```

### 3. Ratings Distribution Chart

Bar chart of game ratings (1–10), same pattern as Movies/Series tasks.

**Backend:**
```sql
SELECT personal_rating, COUNT(*) as count
FROM game_detail
WHERE personal_rating IS NOT NULL
GROUP BY personal_rating
ORDER BY personal_rating
```

### 4. Genre Breakdown

Horizontal bar chart of top 10 genres across all games.

**Backend:** Aggregate genres from game_detail (stored as JSON arrays).

### 5. Platform/Store Breakdown

Horizontal bar chart showing games per store/platform.

- **Source:** Game store data (Steam, Nintendo eShop, GOG, Epic, etc.)
- **Display:** Store name + count, sorted by frequency

**Backend:**
```sql
SELECT store, COUNT(*) as count
FROM game_store
GROUP BY store
ORDER BY count DESC
```

**New DTO field:**
```fsharp
StoreDistribution: (string * int) list
```

### 6. Monthly Play Time Trend

Bar chart showing total play time (hours) per month for the last 12 months.

**Backend:**
```sql
SELECT strftime('%Y-%m', date) as month, SUM(duration_minutes) as total_minutes
FROM play_session
WHERE date >= date('now', '-12 months')
GROUP BY month
ORDER BY month
```

**New DTO field:**
```fsharp
MonthlyPlayTime: {| Month: string; TotalMinutes: int |} list
```

### 7. HLTB Comparison Scatter/Bar Chart

For completed games, show your play time vs. HLTB average as a comparative visualization.

- **Format:** Grouped bar chart — each completed game shows two bars (your time vs. HLTB Main Story time)
- **Color:** Your time in primary, HLTB average in a muted tone
- **Sort:** By difference (most over/under HLTB first) or by game name
- **Limit:** Top 10 most recently completed games with HLTB data
- **Insight label:** "You played 20% faster/slower than average"

**Data available:** Play time per game (sum of play sessions) and HLTB hours (already fetched and displayed on game detail pages).

**Backend:** Query completed games with both play time and HLTB data:
```sql
SELECT g.slug, g.name, g.cover_ref,
       (SELECT SUM(duration_minutes) FROM play_session WHERE game_slug = g.slug) as play_minutes,
       g.hltb_main_story_hours
FROM game_detail g
WHERE g.status = 'Completed'
  AND g.hltb_main_story_hours IS NOT NULL
ORDER BY (SELECT MAX(date) FROM play_session WHERE game_slug = g.slug) DESC
LIMIT 10
```

**New DTO:**
```fsharp
type DashboardHltbComparison = {
    Slug: string
    Name: string
    CoverRef: string option
    PlayMinutes: int
    HltbMainHours: float
}
```

### 8. Backlog Time Estimate

A prominent display showing total estimated time to clear the backlog.

- **Calculation:** Sum HLTB "Main Story" hours for games with status = Backlog or InFocus
- **Display:** Large number like "~340 hours" or "~14 days" in a highlight card
- **Sub-detail:** "across N games (M without HLTB data)"
- **Placement:** Near the top, as a motivating/informational hero stat

### 9. Games Completed Per Year

Bar chart showing how many games you completed each year.

- **X-axis:** Years
- **Y-axis:** Games completed count
- **Source:** Completion date (from status change event or last play session date for completed games)

**Backend:** This may require the date when status changed to Completed. If not stored, approximate using the last play session date for completed games.

### 10. Layout

Suggested layout order for the Games tab:
1. Stats badges row (existing + new: backlog size, completion rate, avg rating)
2. Backlog time estimate hero card
3. Status distribution chart (donut or stacked bar)
4. Monthly play time trend chart
5. HLTB comparison chart (your time vs. average)
6. Genre breakdown bars
7. Platform/store breakdown bars
8. Ratings distribution chart
9. Games completed per year trend
10. Recently Played (existing, enhanced)
11. Recently Added (existing)
12. Steam Achievements (existing)

## Files Changed

1. **`src/Shared/Shared.fs`** — New/expanded DTOs
2. **`src/Server/GameProjection.fs`** — New queries for status distribution, genres, stores, monthly activity, HLTB comparison
3. **`src/Server/Api.fs`** — Update `getDashboardGamesTab` with new data
4. **`src/Client/Pages/Dashboard/Views.fs`** — New chart components and layout for Games tab
5. **`src/Client/Pages/Dashboard/Types.fs`** — Model updates
6. **`src/Client/Pages/Dashboard/State.fs`** — New Msg variants if lazy loading

## Acceptance Criteria

- [ ] Stats badges show Backlog Size, Completion Rate, and Average Rating
- [ ] Status distribution chart shows games per status with color-coding
- [ ] Backlog time estimate shows total HLTB hours for Backlog + InFocus games
- [ ] Ratings distribution bar chart displays correctly (1–10 scale)
- [ ] Genre breakdown shows top 10 genres as horizontal bars
- [ ] Platform/store breakdown shows games per store
- [ ] Monthly play time trend shows hours per month (last 12 months)
- [ ] HLTB comparison chart shows your time vs. average for completed games
- [ ] Games completed per year chart shows yearly completion trend
- [ ] All charts follow existing SVG/Feliz pattern (no new library)
- [ ] All sections handle empty state gracefully
- [ ] Mobile layout stacks vertically
- [ ] All existing tests pass

---

### 2026-02-24 18:13 -- Work Completed

**What was done:**
- Expanded `DashboardGameStats` DTO with BacklogSize, CompletionRate, AverageRating, BacklogTimeHours, BacklogGameCount, BacklogGamesWithoutHltb, StatusDistribution, RatingDistribution, GenreDistribution, MonthlyPlayTime, CompletedPerYear
- Added `DashboardHltbComparison` DTO and `HltbComparisons` field to `DashboardGamesTab`
- Added 9 new query functions to `GameProjection.fs`: getGameStatusDistribution, getAverageGameRating, getGameCompletionRate, getBacklogStats, getGameRatingDistribution, getGameGenreDistribution, getMonthlyPlayTime, getHltbComparisons, getGamesCompletedPerYear
- Updated `Api.fs` getDashboardGamesTab to call all new queries and populate expanded DTO
- Added 7 new chart/visualization components to `Views.fs`: backlogTimeEstimateCard, gameStatusDistributionChart (stacked horizontal bar), gameRatingsDistributionChart, gameGenreBreakdownBars, monthlyPlayTimeChart, hltbComparisonChart (grouped dual bars), gamesCompletedPerYearChart
- Updated gameStatsRow to show Backlog, Completion Rate, and Average Rating badges
- Reorganized gamesTabView layout: stats badges, backlog estimate hero, status distribution, monthly play time, HLTB comparison, genre/ratings side-by-side, completed per year, recently played, recently added, achievements
- Note: Platform/store breakdown skipped since there is no `game_store` table in the database schema. Games are associated with stores only implicitly through Steam integration. This can be added later if a store tracking feature is implemented.

**Acceptance criteria status:**
- [x] Stats badges show Backlog Size, Completion Rate, and Average Rating -- Added to gameStatsRow with conditional rendering
- [x] Status distribution chart shows games per status with color-coding -- Stacked horizontal bar with per-status colors and legend
- [x] Backlog time estimate shows total HLTB hours for Backlog + InFocus games -- Hero card with hours/days display and game count
- [x] Ratings distribution bar chart displays correctly (1-10 scale) -- Full 1-10 range with accent color bars
- [x] Genre breakdown shows top 10 genres as horizontal bars -- Horizontal bars with fade opacity
- [~] Platform/store breakdown shows games per store -- Skipped: no game_store table exists in schema
- [x] Monthly play time trend shows hours per month (last 12 months) -- Bar chart from game_play_session data
- [x] HLTB comparison chart shows your time vs average for completed games -- Grouped dual bars with diff labels
- [x] Games completed per year chart shows yearly completion trend -- Bar chart using last play session/steam dates
- [x] All charts follow existing SVG/Feliz pattern (no new library) -- Pure Feliz/HTML/CSS, same as movies/series
- [x] All sections handle empty state gracefully -- All charts show empty state message when no data
- [x] Mobile layout stacks vertically -- Genre/ratings use grid-cols-1 md:grid-cols-2, all else stacks
- [x] All existing tests pass -- 233 tests pass, npm run build succeeds

**Files changed:**
- `src/Shared/Shared.fs` -- Expanded DashboardGameStats with 11 new fields, added DashboardHltbComparison DTO, added HltbComparisons to DashboardGamesTab
- `src/Server/GameProjection.fs` -- Added 9 new dashboard query functions
- `src/Server/Api.fs` -- Updated getDashboardGamesTab to populate all new stats and HLTB comparisons
- `src/Client/Pages/Dashboard/Views.fs` -- Added 7 new chart components, updated gameStatsRow and gamesTabView layout
