# Research: Dashboard Content — What Should Movie, TV Series, and Games Dashboards Show?

**Date:** 2026-02-24
**Status:** Complete
**Relevance:** Unified Dashboard (vision), expanding individual tabs with stats and intelligence

## Summary

Research across 18+ media tracking applications (Letterboxd, Trakt, Serializd, TV Time, Simkl, Backloggd, HLTB, Steam, PlayStation, Xbox, and more) reveals a clear industry consensus on what users value in media dashboards. The core pattern is: **quick-glance stats at the top, actionable "what's next" queues in the middle, and deeper analytics for engagement below.** Every successful tracker prominently displays total consumption time, completion counts, and some form of ratings/genre analysis.

Mediatheca's current dashboards cover the essentials well — stats badges, next-up queues, recently added/played lists, and the play activity chart. The biggest opportunities lie in **time-based analytics** (viewing patterns by day/hour/month), **genre/director/actor affinity breakdowns**, **progress visualizations** (completion rings, progress bars), and **year-in-review summaries**. These are consistently the most engaging features across competitors and the most requested by users in community discussions.

A key differentiator for Mediatheca is its **cross-media unified dashboard** — almost no competitor tracks movies, TV series, AND games in one app. This is the single most requested feature across all community discussions.

## What Mediatheca Currently Has

| Tab | Stats | Sections |
|-----|-------|----------|
| **All** | None (mixed media) | Hero Series Spotlight, Next Up Scroller (6), Movies In Focus (6), 14-Day Play Activity Chart, Games In Focus, New Games (10) |
| **Movies** | Total count, Watch sessions, Watch time | Recently Added (10) |
| **TV Series** | Total count, Episodes watched, Watch time | Next Up (all), Recently Finished, Recently Abandoned |
| **Games** | Total count, Play time, Completed, In Progress | Recently Added (10), Recently Played (10), Steam Achievements |

## Key Findings

### 1. Universal Stats That Every Tracker Shows

Every successful media tracker prominently displays these stats, regardless of media type:

| Stat | Movies (Letterboxd/Trakt) | TV (Trakt/Simkl/TV Time) | Games (Backloggd/HLTB/Steam) |
|------|---------------------------|--------------------------|------------------------------|
| **Total consumed** | Films watched | Episodes watched | Games played/beaten |
| **Total time** | Hours watched | Hours watched | Hours played |
| **Completion count** | — | Shows completed | Games completed |
| **Average rating** | Your avg rating | Your avg rating | Your avg rating |
| **In progress** | — | Currently watching count | Currently playing count |
| **Backlog size** | Watchlist count | Plan to Watch count | Backlog count |

**Gap in Mediatheca:** Average rating, backlog/watchlist counts, and "currently watching/playing" counts are not on the dashboard stats badges.

### 2. Ratings Distribution — The Most Engaging Personal Stat

Every major tracker (Letterboxd, Trakt, IMDb, Backloggd) prominently features a **ratings distribution chart** — a bar graph showing how many items you rated at each score. Users love seeing whether they're a "generous rater" or "harsh critic." This is consistently one of the most viewed profile sections.

- Letterboxd: half-star increments (0.5–5.0)
- Trakt/IMDb: 1–10 scale
- Backloggd: half-star (0.5–5.0)

**Opportunity:** Mediatheca has ratings data for all three media types. A simple bar chart per media tab would be high-impact and low-effort.

### 3. Genre/Category Breakdown — Understanding Your Taste

Present in Letterboxd (Pro), Trakt (YIR), Backloggd, Steam Replay:

- **Bar or pie chart** of genres consumed, sorted by count or by average rating
- Often clickable to filter the library by that genre
- Some show "top genre per year" trends

**Opportunity:** Genre data exists for movies (TMDB), series (TMDB), and games (RAWG). A horizontal bar chart showing genre distribution per media tab.

### 4. Time-Based Analytics — When Do You Watch/Play?

The most engaging visualizations across competitors:

| Visualization | Used By | Description |
|---------------|---------|-------------|
| **Activity heatmap** | Trakt, Letterboxd | GitHub-style calendar grid showing activity per day |
| **Day-of-week chart** | Trakt, Simkl | Bar chart: which days you watch/play most |
| **Hour-of-day chart** | Trakt | 24-hour distribution of when you watch |
| **Monthly breakdown** | Trakt, Steam Replay, Backloggd | Bar chart of consumption per month |
| **Yearly trend** | Backloggd, Trakt | Bar chart of items consumed per year |

**Opportunity:** Mediatheca already has timestamped watch sessions and play sessions. The data exists — it just needs visualization. A monthly breakdown bar chart and a day-of-week chart would be the highest-impact additions.

### 5. Director/Actor/Studio Affinity — Who You Watch Most

Present in Letterboxd (top 20 actors/directors), Trakt (YIR), Serializd:

- Most watched actors, directors, studios
- Highest rated actors/directors (your personal preference vs. volume)
- Click through to see all films with that person

**Opportunity:** Mediatheca stores cast data for movies and series. A "Most Watched Actors" and "Most Watched Directors" section on the Movies tab would be distinctive.

### 6. Progress Visualizations — Completion Rings and Bars

**TV Series:**
- Per-show progress bars (episodes watched / total) — Trakt, Simkl, Showly
- Time remaining estimate ("12 hours left in this series") — Trakt
- Season-by-season progress — Serializd

**Games:**
- Completion percentage vs. HLTB average — universal in game trackers
- Status distribution donut chart (Playing/Backlog/Completed/Abandoned) — Backloggd
- Games per status per platform — Backloggery, Backloggd

**Opportunity:** The Games tab already shows HLTB data. Adding a status distribution donut chart and per-series progress bars would be high value.

### 7. Year in Review / Wrapped — The Engagement Feature

Every major platform now has annual summaries (Letterboxd Wrapped, Trakt YIR, TV Time REWIND, Steam Replay, PlayStation Wrap-Up, Xbox Year in Review). Key elements:

- **Total consumption stats** for the year
- **Top items** (most watched/played)
- **Genre/category breakdown** for that year
- **Monthly breakdown** showing busiest months
- **First and last** item of the year
- **Shareable card** visual for social media
- **Personality type** classification (Xbox's "Knight Owl" for late-night gaming, etc.)

This is consistently the single most engaging feature across all platforms — users share these annually and it drives massive engagement.

**Opportunity:** This is a v2 feature in the vision. All the data is already in the event store. A yearly stats page per media type would be a strong v2 differentiator.

### 8. Gamification and Streaks

Present in TV Time (badges), SavePoint (XP/levels/streaks), Steam (profile level):

- **Watch/play streaks** — consecutive days with activity (Duolingo-style)
- **Milestone badges** — "100th film," "1000th episode," "50 games beaten"
- **Consistency rewards** — "Watched within 48h of air date for 10 weeks"

**Opportunity:** Light-touch — milestones ("100th movie watched!") could appear as celebratory moments on the dashboard without building a full badge system.

### 9. Calendar and Schedule Views

Present in Trakt, Simkl, next-episode.net, Showly:

- **Upcoming episodes** for tracked series with air dates and countdowns
- **Release calendar** for wishlist games
- **iCal export** for system calendar integration

**Opportunity:** Air date data comes from TMDB for series. An "Upcoming Episodes" section on the TV Series tab showing next air dates for in-progress series would be valuable.

### 10. What Users Request Most (Across All Communities)

Synthesized from Reddit, forums, and reviews across movie/TV/game communities:

| Request | Frequency | Mediatheca Status |
|---------|-----------|-------------------|
| Cross-media unified tracking | Very High | **Already built** (key differentiator) |
| Total time spent | Very High | **Already shown** on each tab |
| Activity heatmap (GitHub-style) | High | Not built |
| Ratings distribution chart | High | Not built |
| Genre breakdown | High | Not built |
| Monthly/yearly trends | High | Not built |
| Rewatch/replay tracking | High | **Already supported** (multiple sessions) |
| Custom status categories | Medium | **Already built** (game statuses) |
| Year in Review | Medium | Not built (v2 vision item) |
| Director/actor affinity | Medium | Not built |
| Time-of-day analytics | Medium | Not built |
| Watch streaks | Medium | Not built |
| HLTB comparison | Medium | **Already built** |
| Upcoming episode schedule | Medium | Not built |
| Backlog time estimation | Medium (games) | Partially (HLTB data exists) |

## Recommendations for Each Dashboard Tab

### Movies Tab — Recommended Additions

**Quick Wins (data already available):**
1. **Ratings distribution** — bar chart of your movie ratings (the data exists if movies have ratings)
2. **Genre breakdown** — horizontal bar chart of movies by genre
3. **Recently watched** — movies with recent watch sessions (not just recently added)
4. **Most watched with** — friends who appear most in your watch sessions

**Medium Effort:**
5. **Monthly watch activity** — bar chart of movies watched per month (current year)
6. **Most watched actors/directors** — top 5, pulled from cast data
7. **World map** — countries of origin for your movies (TMDB provides production countries)

**Higher Effort:**
8. **Yearly trend** — movies watched per year over time
9. **Watch time by month** — cumulative hours per month

### TV Series Tab — Recommended Additions

**Quick Wins:**
1. **Currently watching count** — stat badge for active series
2. **Per-series progress bars** — visual completion indicator in the Next Up list
3. **Time remaining** — estimated hours left per series based on episode runtimes

**Medium Effort:**
4. **Episode activity chart** — similar to the Games 14-day chart but for episodes watched
5. **Genre breakdown** — bar chart of series by genre
6. **Binge detection** — highlight when 3+ episodes were watched in a single day
7. **Upcoming episodes** — next air dates for in-progress series (TMDB has this data)

**Higher Effort:**
8. **Monthly episode trend** — episodes watched per month
9. **Most watched with** — friends from rewatch sessions

### Games Tab — Recommended Additions

**Quick Wins:**
1. **Status distribution** — donut or stacked bar chart (Backlog/InFocus/Playing/Completed/Abandoned/OnHold/Dismissed)
2. **Backlog time estimate** — total HLTB hours for all Backlog + InFocus games
3. **Average rating** — stat badge

**Medium Effort:**
4. **Genre breakdown** — bar chart of games by genre
5. **Platform breakdown** — games by platform/store
6. **Completion rate** — percentage of collection that's been beaten (motivating metric)
7. **Monthly play time trend** — bar chart of hours played per month

**Higher Effort:**
8. **HLTB comparison scatter plot** — your play time vs. HLTB average for completed games
9. **Yearly games beaten** — bar chart over time

### All Tab — Recommended Additions

**Quick Wins:**
1. **Total media time** — combined watch + play time across all media as a single hero stat
2. **This week/month activity summary** — "4 episodes, 2 movies, 6 hours of gaming this week"

**Medium Effort:**
3. **Activity heatmap** — GitHub-style calendar grid showing days with any media activity
4. **Cross-media monthly breakdown** — stacked bar chart (movies + episodes + game hours per month)

## Industry Comparison: Mediatheca vs. Leaders

| Feature | Letterboxd | Trakt | Backloggd | Mediatheca (Current) | Mediatheca (Potential) |
|---------|-----------|-------|-----------|---------------------|----------------------|
| Cross-media | Movies only | Movies + TV | Games only | Movies + TV + Games | **Unique advantage** |
| Stats badges | Pro only | VIP | Free | Free | Expand counts |
| Ratings chart | Pro | VIP (YIR) | Free | -- | Easy add |
| Genre breakdown | Pro | VIP (YIR) | Free | -- | Medium effort |
| Activity heatmap | Pro (weekly) | VIP (daily) | -- | 14-day play chart | Extend to all media |
| Actor/director stats | Pro | VIP (YIR) | -- | -- | Medium effort |
| World map | Pro | VIP (YIR) | -- | -- | Higher effort |
| Year in Review | Pro | VIP | Backer | -- | v2 feature |
| Progress tracking | -- | Free (TV) | Free (games) | Partial (TV next-up) | Add progress bars |
| Calendar | -- | Free | -- | -- | Medium effort |
| HLTB integration | -- | -- | -- | Free | Already built |
| Streaks/gamification | -- | -- | -- | -- | Optional |

## Open Questions

- What rating scale does Mediatheca use? (affects ratings distribution chart design)
- Is there a genre field stored for all three media types, or only from external APIs?
- Would a "Year in Review" be interesting for v2 even though it's a single-user app? (no social sharing motivation, but personal reflection value)
- How much dashboard real estate is available on mobile? Prioritization matters more for small screens.
- Would an upcoming episodes calendar require periodic TMDB polling, or can air dates be stored at import time?

## Sources

### Movie Trackers
- [Letterboxd](https://letterboxd.com/) — Stats, Year in Review, List Progress
- [Letterboxd Year in Review FAQ](https://letterboxd.com/journal/2025-letterboxd-year-in-review-faq/)
- [Letterboxd All-Time Stats](https://letterboxd.com/journal/all-time/)
- [Trakt.tv](https://trakt.tv/) — Year in Review, All-Time Stats, Calendar
- [Trakt Year in Review](https://support.trakt.tv/support/solutions/articles/70000381026-year-in-review)
- [IMDb Ratings Insights](https://community-imdb.sprinklr.com/conversations/imdbcom/reintroducing-ratings-insights/68ed4a8d53b7872ee1f8298b)
- [JustWatch Streaming Charts](https://www.justwatch.com/us/streaming-charts)
- [Simkl Profile Statistics](https://docs.simkl.org/how-to-use-simkl/core-features/social-and-community/profile-statistics)

### TV Series Trackers
- [Serializd](https://www.serializd.com/) — TV-focused Letterboxd alternative
- [TV Time](https://www.tvtime.com/) — Badges, REWIND yearly recap
- [TV Time Badges Guide](https://webv1.tvtime.com/article/how-to-guide-badges-and-stats)
- [Showly](https://www.showlyapp.com/) — Open-source, Trakt-integrated
- [next-episode.net](https://next-episode.net) — Calendar and countdown focus
- [Simkl Calendar Sync](https://docs.simkl.org/how-to-use-simkl/advanced-usage/additional-tools/calendar-sync)
- [Trakt Calendar Documentation](https://support.trakt.tv/support/solutions/articles/70000376853-how-to-use-the-calendars)

### Game Trackers
- [Backloggd](https://backloggd.com/) — Stats, yearly recap, journal
- [Backloggd 1.8 Stats Update](https://backloggd.medium.com/1-8-stats-update-e04f76c4fc5f)
- [HowLongToBeat](https://howlongtobeat.com) — Completion time data
- [Grouvee](https://www.grouvee.com/) — Social game tracking
- [Infinite Backlog](https://infinitebacklog.net/) — Detailed ownership tracking
- [Stash](https://stash.games/) — Mobile-first game tracker
- [SavePoint](https://www.twoaveragegamers.com/why-your-game-tracker-needs-xp-streaks-and-badges/) — Gamification approach

### Platform Year-End Summaries
- [Steam Replay 2025](https://www.pcworld.com/article/3015881/steam-replay-2025-is-here-see-your-year-on-steam-stats-and-all.html)
- [PlayStation Wrap-Up 2025](https://www.pushsquare.com/news/2025/12/playstation-wrap-up-2025-live-now-get-your-gaming-stats-for-the-year)
- [Xbox Year in Review 2024](https://news.xbox.com/en-us/2024/12/04/xbox-year-in-review-2024/)

### Community Discussions
- [ResetEra — Game Trackers Discussion](https://www.resetera.com/threads/game-trackers-which-one-do-you-use-and-why.462414/)
- [Trakt Forums — Status Request](https://forums.trakt.tv/t/set-tv-show-as-dropped-watching-completed-or-on-hold/5800)
- [Trakt Forums — Separate Movie/TV Stats](https://forums.trakt.tv/t/year-in-review-separate-stats-for-movies-vs-tv-shows/93535)
- [SaaSHub — Trakt vs TV Time](https://www.saashub.com/compare-trakt-tv-vs-tv-time)
- [One Organizer to Rule Them All](https://emusements.com/wanted-one-movie-and-tv-organizer-to-rule-them-all)
