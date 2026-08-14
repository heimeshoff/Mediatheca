# Series Dashboard — Redesign Recommendation

*2026-08-14. Based on a code survey of the current Dashboard Series tab and competitor
research (Trakt, TV Time, Plex/Jellyfin/Emby, Simkl, Sonarr, Ryot/MediaTracker — full
citations in `.agentheim/knowledge/research/series-dashboard-patterns-2026-08-14.md`).
Companion to `movies-dashboard.md`.*

## TL;DR

The dedicated Series tab is currently a *degraded duplicate* of the All tab: both render
the same `getDashboardSeriesNextUp` data, but the All tab uses the rich
`nextEpisodeHeroCard` (backdrop, episode title, season rail, per-episode dot progress)
while the Series tab shows small poster cards with a flat percent bar. The dedicated tab
should be the *richer* view, not the poorer one.

Redesign around what series tracking actually is — a **continuation loop**, not a picking
problem:

1. **"Continue watching"** — the hero: every in-progress show with episode title,
   progress, and **episodes/time remaining**
2. **"Airing / returning"** — calendar awareness for shows you follow
3. **"Stalled shows"** — the genuinely novel card: shows you quietly stopped watching
4. Social + diary + one compact insight card linking to a Stats page

Two loved features are nearly free: the time-remaining estimate already ships in the DTO
(`AverageRuntimeMinutes`, computed then discarded), and a dead view function
(`seriesNextUpItemEnhanced`) already implemented it once.

## What the research says — and what users love

- **Next-up correctness is the single highest-stakes element.** Every app centers on it
  (Trakt Up Next, TV Time Watch Next, Plex On Deck, Jellyfin Next Up), and when it's
  wrong it's the most-complained-about failure in the whole category — worse than any
  layout issue. Mediatheca's frontier rule (`NextUp.compute`: gaps behind the
  furthest-watched episode are history, not queue) plus its regression coverage is
  exactly the right investment; the dashboard should showcase it, not hide it.
- **Per-show progress with remaining episodes/time is Trakt's signature, genuinely loved
  feature** — cited as a reason people switch trackers. "4 episodes left · ~3h to
  finish" turns progress into a decision aid ("can I finish this season this weekend?").
- **Airing calendars are expected baseline, not a differentiator** — valued for
  existing, criticized when shallow (Trakt users complained when the redesigned calendar
  dropped air-times).
- **Status buckets (Watching / On Hold / Dropped / Plan-to-Watch)** are useful as
  filters but no source praised them as dashboard UX; Simkl's bucketed dashboard drew
  "cluttered" complaints even from fans of the lists.
- **Gamification (streaks, badges, social feeds) is the cautionary tale.** TV Time built
  its identity on it and shut down permanently on 2026-07-15; migration commentary
  frames Trakt/Simkl as the clutter-free alternative and treats badges as bloat.
  Self-hosted single-user tools (Jellyfin, Sonarr, Ryot, MediaTracker) uniformly drop
  gamification and social, converging on next-up correctness + calendar + scrobbling.
  Lean and correctness-first is the right posture for Mediatheca.
- **The "stalled show" problem — stopped watching but never marked dropped — has no
  solved precedent in any surveyed app.** Everyone relies on manual reclassification.
  This is white space where event-sourced watch history can compute staleness
  proactively.

## Card-by-card verdict on the current tab

| Current element | Verdict |
|---|---|
| Stats badges row | Keep, shrink — one quiet line |
| Next Up (poster cards) | **Replace with the hero treatment** — currently strictly poorer than the All tab's version of the same data |
| Returning Soon | Keep, **upgrade to the airing zone** — add next-air-date for currently-watching shows (data is already fetched per row and dropped) |
| Recently Finished / Recently Abandoned | Keep, compact diary — add the personal rating on finished shows (currently just name + year) |
| Monthly Activity / Ratings Distribution / Genre donut | Move to a dedicated Stats page (same pattern as `movies-dashboard.md`) |
| Most Watched With | Keep — merge into the social zone |

## The proposed page, top to bottom

### 1. "Continue watching" — the hero zone

Promote the `nextEpisodeHeroCard` treatment (backdrop, `S2E4: <episode title>`, season
rail, per-episode dot progress with holes preserved, friend avatars, Jellyfin play) to
the Series tab, and make it *richer* than the All tab's row:

- **Remaining-work line**: "4 episodes left this season · ~3h" from
  `AverageRuntimeMinutes` × unwatched count. The dead `seriesNextUpItemEnhanced`
  (Dashboard/Views.fs:2635) already computed exactly this — resurrect the logic, not
  the function.
- **Order**: In Focus first, then last-watched recency (current server order) — this is
  Trakt's Up Next model and it matches how people actually resume shows.
- Division of labor vs the All tab: All shows the top ~11 as a teaser; the Series tab
  shows the full in-progress set with the remaining-work detail.

### 2. "On the air" — the calendar zone

Merge Returning Soon with a "new episode soon" row for shows currently being watched:
poster, show name, "S3E5 airs Thu Aug 20 (in 6 days)". `NextAirDate` is already computed
per `SeriesListItem` row and never displayed — this card is where it belongs. Keep it a
list, not a full calendar grid; the research says depth (which episode, when) beats
breadth.

### 3. "Stalled" — the novel card

Shows with watch history but no session in N weeks (e.g. 6), not finished, not
abandoned, not returning-soon: "You left *Dark* at S2E3, last watched in May." Offer two
inline actions: jump back in (navigate/play) or mark abandoned. No competitor does this;
the event store makes it trivial to compute, and it directly attacks the guilt-pile
problem every tracker user has. This also keeps "Abandoned" honest — today a show only
reaches Recently Abandoned if manually flagged.

### 4. "Watch with friends" — the social zone

Mirror the movies recommendation: `RecommendedBy` and `WantToWatchWith` exist on series
and appear nowhere on the tab.

- **"Recommended by…"** — unstarted series with the recommending friend's avatar.
- **"Watching with…"** — shows tied to a friend (rewatch-session friends +
  want_to_watch_with): "2 shows in progress with Anna." Series co-watching is even more
  of a standing ritual than movie nights — this is the planning surface for it.
- Fold **Most Watched With** ("N episodes together", clickable to friend) in here.

### 5. "The record" — finished & abandoned diary

Keep the two compact lists; add the star rating to finished entries and the
furthest-watched point to abandoned ones ("stopped at S4E2"). Consider one extra bucket
the status data already supports: **"Ended, unfinished"** — shows whose run is over
(status Ended/Canceled) that you haven't completed; these are completable backlog,
qualitatively different from airing shows.

### 6. "Your taste" — one compact card, not three charts

Same pattern as movies: a single insight card ("312 episodes this year · top genre:
Sci-Fi · longest binge: March") linking to the dedicated Stats page, which takes the
monthly episode chart, ratings histogram, genre donut — and is the right home for the
per-day stacked `episodeActivityChart` that was built, shipped in the payload
(`EpisodeActivity`), and never rendered.

## Implementation notes

- **Dead code**: three abandoned generations of Next Up presentation
  (`seriesNextUpItem`, `seriesNextUpScroller`/`seriesNextUpSection`, `heroSpotlight`,
  `seriesNextUpItemEnhanced`) plus `buildEpisodeChartData`/`episodeActivityChart` sit
  uncalled in Dashboard/Views.fs. The redesign is the moment to delete or resurrect
  deliberately.
- **Wasted queries**: the Series tab fetches Next Up *unbounded* with per-row SQL for
  `AverageRuntimeMinutes`, `CurrentSeasonWatched`, `SeasonsTouched`, and per-row
  `getNextAirDate` on `SeriesListItem` — then renders almost none of it. The redesign
  uses this data; if any card doesn't, trim the DTO.
- The client-side `not IsAbandoned` filter on Next Up is a no-op (the SQL view already
  excludes abandoned) — remove it.
- `seriesTabView` takes no `dispatch` — the tab is fully static today. The stalled-show
  actions (resume / mark abandoned) will be its first interactions.
- Catalogs support series (including season/episode children via
  `getCatalogsForSeriesWithChildren`) and are absent from the tab — same "From your
  catalogs" shelf option as the movies dashboard if curation grows series-side.
- Shared with `movies-dashboard.md`: extract a generic shelf primitive and stat-card
  into `DesignSystem.fs` + StyleGuide; build the Stats page once with sections per
  medium.

## Research caveats

TV Time shut down 2026-07-15 — its UX remains a useful reference but older reviews
describe a dead product. TrustPilot's TV Time page couldn't be fetched directly;
Sonarr-calendar and MyAnimeList-bucket sentiment are flagged in the report as
plausible-but-not-strongly-sourced.
