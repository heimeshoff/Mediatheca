# Movies Dashboard — Redesign Recommendation

*2026-08-14. Based on a code survey of the current Dashboard page and competitor research
(Letterboxd, Trakt, Plex/Jellyfin, JustWatch, Movary — full citations in
`.agentheim/knowledge/research/movie-dashboard-patterns-2026-08-14.md`).*

## TL;DR

The Movies tab currently treats ten cards as equals, so it answers no question well.
Redesign it around a hierarchy of three questions, in this order:

1. **"What should I watch tonight?"** — the hero, ~60% of the page
2. **"What have I been watching?"** — the diary
3. **"Who am I as a watcher?"** — one compact insight card linking to a dedicated Stats page

The biggest untapped differentiator is the social data: `recommended_by`,
`want_to_watch_with`, and Catalogs exist in the model and appear nowhere on this page.

## What other apps do — and what users love

- **The two universally loved features are the watchlist and the diary.** Letterboxd's
  entire reputation rests on "log what you watched, keep a list of what's next."
  Mediatheca already has both primitives; the dashboard's job is to *stage* them, not
  invent new ones.
- **Trakt/Plex's "continue watching" is the most-praised dashboard element** — but the
  enthusiasm is really about serialized TV. For a *movies* page, the equivalent of
  "up next" is a well-curated watchlist, not a resume row. Don't copy Plex here.
- **Stats are loved as a periodic ritual, not a daily view.** Letterboxd ships them as
  Year in Review; Trakt gates them behind VIP on a separate page. The one single-user
  precedent, Movary, does put stats on its dashboard — but stats earn their place as
  *self-insight* ("huh, I've been on a rewatch spree"), not as five equal-weight charts.
- **The most complained-about element across all apps is the unscoped "Recently Added"
  row** (Plex has a 1000+-reply forum thread about the clutter). Mediatheca's version is
  already scoped to unwatched items, which is right; keep it small.

## Card-by-card verdict on the current page

| Current card | Verdict |
|---|---|
| Stats pills row | Keep, shrink — one quiet line, not the opening act |
| Recently Watched | **Keep & fix** — show friend *names/avatars* (raw slugs today), add rating and a rewatch marker |
| Recently Added | Keep, demote to a small shelf |
| Movies to Watch | **Promote to hero**, and split into lanes (below) |
| Monthly Activity chart | Move to Stats page |
| Ratings Distribution chart | Move to Stats page |
| Top Actors | Move to Stats page (fun, but not a nightly decision aid) |
| Top Directors | Fix the empty-data bug or kill until crew data flows |
| Most Watched With | Keep — social and clickable — merge into the social zone |
| Genre donut, Movie Origins | Move to Stats page; Origins only renders conditionally anyway |

## The proposed page, top to bottom

### 1. "Tonight" — the hero zone

Instead of one flat "Movies to Watch" scroller, stage the decision as two or three lanes
with real editorial logic:

- **In Focus** — the explicit shortlist, as the lead `filmstripRow` (the All tab's
  filmstrip is the best component available; the Movies tab currently uses the plainer
  poster variant — unify on the filmstrip).
- **"Recommended by…"** — unwatched movies with `recommended_by`, showing the friend's
  avatar on the card. No competitor app can do this; it's the killer card.
- **"Movie night with…"** — `want_to_watch_with` grouped by friend: "3 movies waiting
  for you and Anna." Turns the dashboard into a planning tool — exactly the cinephile
  use case for a shared library.

Cinephile touch: a **runtime filter pill** ("under 100 min") on the hero zone —
"it's 10pm, what fits?" is *the* real-world movie-picking constraint, `runtime` is in
the model, and no card uses it.

### 2. "Your diary" — Recently Watched, fixed

The Letterboxd-loved feature. Each entry: poster, date, personal star rating, companions
as avatar pills (names, not slugs), and a subtle "rewatch" badge (multi-session data
exists and is currently unused).

### 3. "From your catalogs"

One shelf surfacing curated Catalogs (e.g. the next unwatched entry from each sorted
catalog). Catalogs are a whole Curation context the dashboard completely ignores; this
is where "work through my Kurosawa list" lives.

### 4. "Collection pulse"

The stat pills plus a small Recently Added shelf, one row.

### 5. "Your taste" — one compact card, not five charts

A single card with two or three headline insights ("Top genre this year: Thriller ·
41h watched · most-seen actor: Toshiro Mifune") that links to a new dedicated
**Movie Stats page** holding the monthly activity chart, ratings histogram, genre donut,
origins map, and top people lists. That's the Letterboxd/Trakt pattern: stats as a
destination you visit, framed as insight, with room to grow (streaks, decade breakdown,
year-in-review).

## Implementation notes

- `Dashboard/Views.fs` is 4,411 lines with ~8 hand-rolled copies of the same
  snap-scroller and 5+ bespoke bar charts. The redesign is the natural moment to extract
  a generic shelf primitive and a stat-card into `DesignSystem.fs` + StyleGuide, where
  neither exists today.
- The All tab already renders "Movies to Watch"; keep the Movies tab's hero *richer*
  than the All tab's row (lanes + runtime filter) so the two don't feel redundant.
- Known data quirks found in the survey: "Most Watched Directors" is effectively always
  empty unless `movie_crew` is populated; "Movie Origins" only renders when
  `production_countries` exists; Recently Watched returns friend slugs, not names.

## Research caveats

Reddit was largely unreachable during the research session, so user-sentiment evidence
leans on app-store and blog reviews. Movary's exact dashboard layout is worth eyeballing
at demo.movary.org for a direct single-user comparison.
