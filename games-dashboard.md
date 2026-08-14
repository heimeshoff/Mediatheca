# Games Dashboard — Redesign Recommendation

*2026-08-14. Based on a code survey of the current Dashboard Games tab and competitor
research (Backloggd, HowLongToBeat, Steam, Playnite, GOG Galaxy, Grouvee, backlog-
management culture — full citations in
`.agentheim/knowledge/research/games-dashboard-patterns-2026-08-14.md`).
Companion to `movies-dashboard.md` and `series-dashboard.md`.*

## TL;DR

The Games tab is currently an analytics page wearing a dashboard's clothes: six charts,
two poster scrollers, an achievements list — and **not a single decision aid**. You can
see that 34 games are in the backlog but never *which ones*; the In Focus roster renders
only on the All tab while the Games tab gets just the hours-remaining number; friends,
Steam Deck compatibility, play facets (co-op/couch/VR), and upcoming releases are all in
the model and never shown.

The research's central finding: every loved tracker is a *museum* — great at recording,
useless for "what should I play tonight." Mediatheca has actual session durations, play
facets, and an In Focus shortlist, which makes it one of very few apps positioned to
build the deciding half. Redesign around:

1. **"Tonight"** — the In Focus roster with per-game progress, filtered by the time you
   actually have
2. **"Back at it"** — recently played with resume context
3. **"The backlog"** — HLTB-framed awareness plus a browsable next-up shortlist
4. Social + upcoming + the record, with the six charts moving to the shared Stats page

## What the research says — and what users love

- **Recording vs. deciding is the fault line.** A Backloggd power-user called it "a
  museum, not a decision-maker" after six months of daily use. Logging/status/ratings
  are solved problems; decision support is white space.
- **Session-length-aware picking is unclaimed territory.** No major tracker filters by
  "what fits my evening" — that need is served entirely by third-party randomizer/wheel
  tools (Steam Roulette, pickaga.me) filtering on playtime, genre, co-op. Mediatheca
  logs real session durations and has `PlayFacets` — it can do natively what no surveyed
  competitor does.
- **HLTB backlog-hours framing is loved as an awareness stat, not trusted as a precise
  predictor** (small noisy samples per game). It answers "how big is my backlog," not
  "what do I play in the next hour" — don't conflate the two on the page.
- **Fixed status taxonomies break in the same two places everywhere**: Playnite's
  community has spent years asking for (a) "Abandoned" distinct from "never started"
  and (b) a state for endless/live-service games that can't be "beaten." Mediatheca's
  `Backlog | InFocus | Retired | Abandoned | Dismissed` already handles (a), and
  "Retired" is refreshingly honest about fuzzy completion — but (b) is a real gap:
  an endless game is neither retirable nor abandoned.
- **Social feeds don't transplant to a single-user app.** Backloggd/Steam/GOG all
  default to *other people's* activity. Friend metadata works better as contextual
  badges on game cards ("recommended by Anna") than as any feed.
- **Friction kills logging habits** — one user's nightly habit died over a 45-second vs
  10-second logging flow. Mediatheca's Steam sync auto-capture is the moat: never add a
  manual logging step the sync could cover.

## Card-by-card verdict on the current tab

| Current element | Verdict |
|---|---|
| Stat badges row | Keep, shrink — one quiet line |
| In-Focus Estimate hero (numbers only) | **Merge into the hero** — show the roster *with* the estimate, not instead of it |
| Recently Played scroller | Keep, **enrich** — add per-game progress vs HLTB and a resume context line |
| Recently Added scroller | Keep, demote to a small shelf |
| Status donut | Replace with **clickable status buckets** (counts that link to filtered lists); donut moves to Stats |
| Genre radar | Move to Stats page |
| Monthly Play Time (stacked per-game) | Move to Stats page |
| Recent Achievements | Keep, compact — make entries link to the game (`GameAppId` is already shipped and unused) |
| Your Time vs HLTB | Move to Stats page — it's a delightful insight, not a nightly aid |
| Ratings Distribution / Retired Per Year | Move to Stats page |

## The proposed page, top to bottom

### 1. "Tonight" — the hero zone

The In Focus roster (currently All-tab-only) as cover cards, each with:

- **Progress vs HLTB main**: "14h / ~22h" or a subtle bar — Trakt-style progress,
  the loved feature translated to games.
- The aggregate estimate line ("~86h remaining across 5 in-focus games") as the zone's
  caption — the current hero card's number, demoted to context.
- **The session filter** — the genuinely novel piece: pills like "≤ 1h session" /
  "couch co-op" / "deck verified" that filter the roster using each game's *median
  logged session length*, `PlayFacets`, and `DeckCompat`. "It's 9pm, I have an hour,
  what fits?" is the game-picking constraint, and the data is already in the model.

### 2. "Back at it" — recently played

Keep the scroller, add what makes it a resume aid: last-played date, total hours,
progress vs HLTB (the dead `gameRecentlyPlayedItem` already rendered "12h / 40h" —
resurrect the idea). This zone is also the natural home for the **sync-playtime
button** — the feature is fully wired (`TriggerPlaytimeSync`, `IsSyncing`, API) but its
only button sits in a dead view function.

### 3. "The backlog" — awareness + next candidates

Two elements, kept deliberately distinct per the research:

- **The awareness stat**: "Backlog: 34 games · ~410h" — `BacklogTimeHours`,
  `BacklogGameCount`, `BacklogGamesWithoutHltb` are computed server-side on every load
  and currently thrown away unrendered. Framed with a wink, not guilt.
- **Next candidates**: a small shelf of actual backlog games worth starting — e.g.
  "quick wins" (shortest HLTB first) and "recommended by friends". This is what turns
  the museum into a decision-maker.
- **Clickable status buckets**: Backlog / In Focus / Retired / Abandoned / Dismissed as
  count chips linking to the filtered games list — replaces the donut's aggregate-only
  view of the same data.

### 4. "Play with friends" — the social zone

`RecommendedBy`, `WantToPlayWith`, `PlayedWith`, and `FamilyOwners` all exist with
ready-made projections (`getGamesRecommendedByFriend`, `getGamesWantToPlayWithFriend`,
`getGamesPlayedWithFriend`) and none reach the tab. Mirror the movies/series docs:
"recommended by…" covers with friend avatars, and "game night with…" for
want-to-play-with — co-op planning is the strongest social use case games have.
The `FamilyOwners` data ("Anna owns this too") is a co-op planning signal no commercial
tracker can offer.

### 5. "Coming soon"

`getUpcomingGames` is already on the API and unused by the dashboard. A small list —
cover, name, release date, countdown — mirrors the series tab's Returning Soon and
covers the wishlist-anticipation loop Backloggd users value.

### 6. "The record" — retired diary + achievements

Recently retired games with personal rating and final playtime, next to the compact
achievements feed (entries linking to their games). This is the diary zone — the
recording half, kept small because it's already well-solved.

### 7. "Your taste" — one compact card, not six charts

Same pattern as the other two docs: one insight card ("214h this year · most played:
Baldur's Gate 3 · you beat games 12% slower than HLTB average") linking to the Stats
page, which takes the status donut, genre radar, monthly stacked play-time chart, the
Your-Time-vs-HLTB comparison, ratings histogram, and retired-per-year.

## Open design question: endless games

Live-service/endless titles (no completion state) break every tracker's taxonomy and
Mediatheca's too: they can sit "In Focus" forever or be wrongly "Retired". Worth a
modeling pass — either a facet ("endless") that exempts a game from completion framing,
or a distinct lifecycle. Flagged as unsolved everywhere; whatever Mediatheca does here
is novel.

## Implementation notes

- **Unreachable wired feature**: `TriggerPlaytimeSync` / `IsSyncing` /
  `api.triggerPlaytimeSync` are fully implemented; the only dispatching button is in the
  dead `gamesRecentlyPlayedChartWithStats`. `gamesTabView` receives `dispatch` and never
  uses it — the tab is fully static today.
- **Dead payload to use or trim**: `Stats.BacklogTimeHours`/`BacklogGameCount`/
  `BacklogGamesWithoutHltb` (use in zone 3), `Stats.MonthlyPlayTime` (superseded by
  `MonthlyPlayTimePerGame` — stop computing), `RecentlyPlayed.HltbHours` (use in
  zone 2), `HltbComparisons.CoverRef`, achievements' `GameAppId`/`Description`, and the
  full `GameListItem` on RecentlyAdded (`PlayFacets`, `DeckCompat`, `PersonalRating`,
  `ReleaseDate` etc. — the projection joins facet overrides for nothing).
- **Dead view functions**: `gameInFocusItem`/`gamesInFocusSection`,
  `gameRecentlyPlayedItem`/`gamesRecentlyPlayedSection`, the whole
  `playSessionChartArea`→`gamesRecentlyPlayedChartWithStats` chain,
  `newGamesSection` (only consumer of `DashboardNewGame.FamilyOwners`),
  `gameStatusDistributionChart` (whose per-status colors the donut lost). Delete or
  resurrect deliberately.
- **All-tab split is backwards**: All tab has the in-focus *roster*, Games tab the
  *estimate*. After the redesign the Games tab holds both; the All tab keeps a small
  roster teaser. The All tab's `GamesRecentlyPlayed`/`PlaySessions`/`NewGames` payload
  is dead there too.
- Three near-identical poster-card implementations (`gameInFocusPosterCard`,
  `gameRecentlyPlayedPosterCard`, `gameRecentlyAddedPosterCard`) differ only in the
  subtitle line — fold into one design-system card with a caption slot, per the shared
  shelf-primitive ask in the other two docs.
- **Catalogs don't support games**: `CatalogProjection.fs` has no `Game` handling, so a
  "From your catalogs" shelf needs Curation-context work first — note it as a gap, not
  a dashboard task.
- No launch affordance exists (no Steam deep link, unlike the Jellyfin play buttons on
  movies/series). `steam://run/<appid>` from the hero cards would be the parity move —
  worth checking browser-protocol behavior before committing.

## Research caveats

HowLongToBeat's and Grouvee's own sites blocked direct fetching, so those sections rely
on secondary sourcing. Simkl-for-games turned up no substantive coverage (unresearched,
not confirmed absent). Intermittent live-service engagement ("abandoned-but-not-
dropped") appears genuinely unsolved in every tool surveyed.
