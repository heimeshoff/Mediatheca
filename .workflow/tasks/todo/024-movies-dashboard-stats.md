# Task: Movies Dashboard Stats & Visualizations

**ID:** 024
**Milestone:** --
**Size:** Large
**Created:** 2026-02-24
**Dependencies:** None
**Research:** research/dashboard-content-research.md

## Objective

Expand the Movies dashboard tab from its current minimal state (3 stat badges + recently added list) into a rich analytics view with ratings distribution, genre breakdown, monthly activity, actor/director affinity, recently watched, and a world map of movie origins.

## Current State

The Movies tab currently shows:
- **Stats badges:** Total Movies, Total Watch Sessions, Total Watch Time
- **Recently Added:** 10 most recently added movies (poster, name, year)

## Details

### 1. New Stats Badges

Add to the existing badge row:
- **Average Rating** — mean of all personal ratings (movies with ratings only)
- **Watchlist** — count of movies not yet watched (no watch sessions)

### 2. Ratings Distribution Bar Chart

A bar chart showing how many movies you rated at each score (1–10).

- **X-axis:** Rating values 1 through 10
- **Y-axis:** Count of movies with that rating
- **Style:** Follow the existing 14-day play activity chart pattern (custom SVG/Feliz)
- **Interaction:** Hover shows tooltip with exact count
- **Empty state:** "No ratings yet" message if no movies have been rated
- **Placement:** Below stats badges, above genre breakdown

**Backend:** New query in `MovieProjection.fs`:
```sql
SELECT personal_rating, COUNT(*) as count
FROM movie_detail
WHERE personal_rating IS NOT NULL
GROUP BY personal_rating
ORDER BY personal_rating
```

**New DTO field** on `DashboardMovieStats` (or new DTO):
```fsharp
RatingDistribution: (int * int) list  // (rating, count) pairs
```

### 3. Genre Breakdown Horizontal Bars

Horizontal bar chart showing movie count per genre, sorted by frequency.

- **Y-axis:** Genre names (top 10)
- **X-axis:** Count of movies in that genre
- **Style:** Horizontal bars with genre label left-aligned, count right-aligned
- **Color:** Primary theme color with opacity gradient
- **Placement:** Below ratings distribution

**Backend:** Since genres are stored as JSON arrays, query all movies and aggregate in F#:
```fsharp
// Read all genre arrays, flatten, count occurrences
let genreDistribution: (string * int) list
```

**New DTO field:**
```fsharp
GenreDistribution: (string * int) list  // (genre, count) pairs, sorted desc
```

### 4. Recently Watched Section

Replace or supplement "Recently Added" with movies that have recent watch sessions — this answers "what have I been watching?" rather than "what did I add?"

- Show movies with the most recent watch session date, sorted by recency
- Display: poster, title, year, watch date, friends present
- Limit: 10 movies

**Backend:** New query joining movie_detail with watch sessions:
```sql
SELECT m.slug, m.name, m.year, m.poster_ref, ws.date, ws.friends
FROM movie_detail m
JOIN watch_session ws ON m.slug = ws.movie_slug
ORDER BY ws.date DESC
LIMIT 10
```

**New DTO:**
```fsharp
type DashboardRecentlyWatched = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    WatchDate: string
    Friends: string list
}
```

### 5. Monthly Watch Activity Chart

Bar chart showing movies watched per month for the current year.

- **X-axis:** Month names (Jan–Dec, or current month back 12 months)
- **Y-axis:** Count of movies watched (based on watch session dates)
- **Secondary metric:** Optional line or overlay showing total watch hours per month
- **Style:** Follow existing chart pattern

**Backend:**
```sql
SELECT strftime('%Y-%m', date) as month, COUNT(DISTINCT movie_slug) as movies, SUM(duration_minutes) as minutes
FROM watch_session
WHERE date >= date('now', '-12 months')
GROUP BY month
ORDER BY month
```

**New DTO field:**
```fsharp
MonthlyActivity: {| Month: string; MovieCount: int; TotalMinutes: int |} list
```

### 6. Most Watched Actors & Directors

Top 5 actors and top 5 directors by number of movies watched.

- **Display:** Name, count of movies, optional photo thumbnail
- **Actors:** From cast table (role-based)
- **Directors:** From crew table where department = "Directing"
- **"Watched" = has at least one watch session** (not just "in library")

**Backend:** Join cast/crew tables with watch_session to count only watched movies:
```sql
-- Actors
SELECT c.name, c.image_ref, COUNT(DISTINCT ws.movie_slug) as movie_count
FROM cast_member c
JOIN watch_session ws ON c.movie_slug = ws.movie_slug
GROUP BY c.tmdb_id
ORDER BY movie_count DESC
LIMIT 5

-- Directors
SELECT cr.name, COUNT(DISTINCT ws.movie_slug) as movie_count
FROM crew_member cr
JOIN watch_session ws ON cr.movie_slug = ws.movie_slug
WHERE cr.department = 'Directing'
GROUP BY cr.name
ORDER BY movie_count DESC
LIMIT 5
```

**New DTO:**
```fsharp
type DashboardPersonStats = {
    Name: string
    ImageRef: string option
    MovieCount: int
}
```

**New DTO fields:**
```fsharp
TopActors: DashboardPersonStats list
TopDirectors: DashboardPersonStats list
```

### 7. Most Watched With (Friends)

Top friends you watch movies with, by session count.

- **Display:** Friend name, friend image, count of shared sessions
- **Limit:** Top 5

**Backend:** Aggregate friend slugs from watch sessions, join with friends table for display names.

### 8. World Map — Movie Origins by Country

A choropleth-style visualization showing which countries' films you've watched.

**Requires new data storage:**

1. **Add `production_countries` field** to `movie_detail` table:
   ```sql
   ALTER TABLE movie_detail ADD COLUMN production_countries TEXT NOT NULL DEFAULT '[]'
   ```

2. **Populate from TMDB** during movie import — TMDB's `/movie/{id}` response includes `production_countries` array with `iso_3166_1` codes and names.

3. **Backfill existing movies** — one-time migration or background job to fetch production countries for all existing movies via TMDB API.

**Visualization approach:**
- Simple approach: A grid/list of countries with bar widths showing count (avoids needing a full SVG map)
- Advanced approach: Inline SVG world map with country paths colored by count (can use a simplified world map SVG)
- **Recommendation:** Start with the bar/list approach; upgrade to SVG map later if desired

**New DTO field:**
```fsharp
CountryDistribution: (string * int) list  // (country name, movie count), sorted desc
```

### 9. Layout

Suggested layout order for the Movies tab:
1. Stats badges row (existing + new: avg rating, watchlist count)
2. Ratings distribution chart
3. Genre breakdown bars
4. Monthly watch activity chart
5. Most watched actors | Most watched directors (side by side on desktop)
6. Most watched with (friends)
7. Country distribution / world map
8. Recently watched
9. Recently added (existing, keep as fallback for empty libraries)

## Files Changed

1. **`src/Shared/Shared.fs`** — New/expanded DTOs for movie dashboard stats
2. **`src/Server/MovieProjection.fs`** — New queries for ratings, genres, monthly activity, actors, directors, countries
3. **`src/Server/Api.fs`** — Update `getDashboardMoviesTab` to include new data
4. **`src/Client/Pages/Dashboard/Views.fs`** — New chart components and layout for Movies tab
5. **`src/Client/Pages/Dashboard/Types.fs`** — Model updates if lazy-loading sections
6. **`src/Client/Pages/Dashboard/State.fs`** — Any new Msg variants for lazy loading

## Acceptance Criteria

- [ ] Stats badges show Average Rating and Watchlist count alongside existing stats
- [ ] Ratings distribution bar chart displays correctly (1–10 scale, counts)
- [ ] Genre breakdown shows top 10 genres as horizontal bars
- [ ] Recently Watched section shows movies with recent watch sessions
- [ ] Monthly activity chart shows movies/hours per month for the last 12 months
- [ ] Most Watched Actors shows top 5 with names and movie counts
- [ ] Most Watched Directors shows top 5 with names and movie counts
- [ ] Most Watched With shows top 5 friends by shared session count
- [ ] Country distribution shows production countries of watched movies
- [ ] Production countries are stored and backfilled from TMDB
- [ ] All charts follow existing SVG/Feliz pattern (no new charting library)
- [ ] All sections gracefully handle empty state (no data yet)
- [ ] Mobile layout stacks sections vertically
- [ ] All existing tests pass
