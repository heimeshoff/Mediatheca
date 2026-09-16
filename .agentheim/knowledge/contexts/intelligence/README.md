# Intelligence

## Purpose
**Derived insights** over the library and journal. Stats blocks, breakdowns, heatmaps, HLTB comparisons, monthly play-time, watched-with stats. Read-only synthesis layer that answers "how am I doing", not "what should I watch".

## Classification
**core** — The "intelligence hub" half of the vision. The dashboard's analytical depth lives here.

## Actors
Single user.

## Ubiquitous language

- **Stats** — a snapshot of activity over a time window. Per-media-type (`DashboardMovieStats`, `DashboardSeriesStats`, `DashboardGameStats`) and cross-media (`DashboardCrossMediaStats`).
- **Activity day** — one calendar day for the heatmap. Reserved language — no live surface since `intelligence-dq8rk`; payload pruned by `intelligence-h4qk2`.
- **Heatmap** — calendar of activity intensity per day. Reserved language — no live surface since `intelligence-dq8rk`; payload pruned by `intelligence-h4qk2`.
- **Monthly breakdown** — activity rolled by month. Reserved language — no live surface since `intelligence-dq8rk`; payload pruned by `intelligence-h4qk2`.
- **HLTB comparison** — user's play time vs. HowLongToBeat average. Shows on the Games tab and per-game detail.
- **InFocus estimate** — how long the In Focus queue would take to clear (Games-specific).
- **Watched-with stats** — friend-keyed counts of shared sessions.
- **Person stats** — aggregate of one friend's contribution (sessions, items shared).
- **Upcoming rail** (intelligence-qh8mj) — the Dashboard Games tab's poster rail of unreleased Steam-linked games, soonest-first with TBA last, sourced verbatim from `GameProjection.getUpcomingGames` via `DashboardGamesTab.Upcoming` (`Shared.fs`) — absent entirely (not empty-rendered) when nothing is upcoming. Moved here from the (now-deleted) Games list page by `intelligence-qh8mj`, ahead of `design-system-fryq7`.

**Retired:** the **New Games** card (dashboard section listing recently-added games) was dropped
from the All tab by `intelligence-dq8rk`'s 3a rebuild and never re-wired onto the Games tab;
`intelligence-wecjh` confirmed the drop (2026-09-06) and deleted the dead `newGamesSection` /
`newGameItem` view helpers. `intelligence-p4t7k` pruned the server-side payload
(`GameProjection.getDashboardNewGames` and `Shared.DashboardAllTab.NewGames`) end to end — the
dashboard no longer computes or ships New Games data at all. `intelligence-h4qk2` similarly pruned the dead 365-day activity-heatmap payload (`DashboardActivityDay`/`DashboardMonthlyBreakdown`, `ActivityDays`/`MonthlyBreakdown` on `DashboardAllTab`, and the seven daily/monthly feeder queries) end to end — no client ever read it either.
- **Card grow** (intelligence-m09d4, intelligence-cs2dm, ADR-0073) — expanding or collapsing a dashboard card is a key-based FLIP travel, not a subtree swap-and-fade: items shared between the collapsed and expanded views (keyed on slug / person name / achievement composite id via `Motion.flipKey`, each card-scoped through `cardItemKey` so two cards that can list the same underlying item never collide) slide from their collapsed on-screen position to their expanded one in ~0.5s, while the card's own surface grows/shrinks height independently. `Views.fs`'s `growingTabArea` owns the choreography (snapshot-on-click, one `useLayoutEffect` for both directions, playing the FLIP against the visible face only). The collapsed content wrapper and the grown surface are keyed siblings (`prop.key "content"` / `prop.key "surface"`, intelligence-cs2dm) rather than two structurally different trees reconciled by index, so the collapsed cards' DOM nodes genuinely persist, mounted `invisible`, underneath whichever card is grown — which is also what lets `chromeClass`'s mount-entrance fade fire only on a tab's first mount, never replaying on collapse. The `Expanded: ExpandedCard option` model shape is unchanged.
- **Linger window** (intelligence-b1nz5) — a finished item stays on its All-tab rail for 7 days after it finished, visibly marked green rather than dropping off immediately. Established first for series (`SeriesProjection.getDashboardSeriesNextUp`'s `IsFinished`, a 7-day `MAX(watched_date)` window) and extended uniformly to movies (`DashboardMovieToWatch.IsFinished`, `MovieProjection.getAllTabMoviesToWatch` — a separate query from the Movies tab's own strictly-unwatched `MoviesToWatchQuery`, since that tab already has its own "Recently Watched" section), games (`DashboardGameInFocus.IsRetired`, `game_list.retired_at` — an event-derived column written by the `Game_status_changed Retired` handler from that event's own timestamp, cleared on any other status change, and backfilled idempotently in `GameProjection.createTables` for games retired before the column existed), and books (`DashboardBookItem.Finished`/`FinishedOn`, `BookProjection.getAllTabCurrentlyReading` — a `finished_at >= date('now','-7 days')` date-string comparison per ADR-0077, again split from the Books tab's own strict `getCurrentlyReading`, intelligence-dnv2y). Lingering items always sort ahead of a rail's other items, most recently finished/retired first.
- **Reading rail / Books tab** (intelligence-dnv2y) — the All tab's "Reading" card (portrait poster grid beside Games, same 130px poster cap as `c3vqm`) shows In Focus books via `BookProjection.getAllTabCurrentlyReading`, with the intelligence-b1nz5 7-day finished linger folded in (`DashboardBookItem.Finished`/`FinishedOn`). The Books tab itself (`DashboardBooksTab`, `Api.getDashboardBooksTab`) adds three strict poster-scroller rails — Currently Reading, Recently Finished (90-day window), Recently Added (excludes already-finished books) — plus a `DashboardBookStats` tile row (Total / In Focus / Finished this year / Finished all time / Pages read this year / Hours listened this year, the last two `option`-typed and hidden when unknown). The 365-day heatmap payload (`DashboardActivityDay`/`DashboardMonthlyBreakdown`, including the `Reading` field intelligence-dnv2y added to it) was pruned end to end by `intelligence-h4qk2` — no client ever consumed it.

## Aggregates

Intelligence **has no write aggregates**. All projections; all reads. Source streams are the events from Movies / Series / Games / Journal.

## Key events

None published.

## Key commands

None.

## Relationships with other contexts

- **Downstream of:** Movies, Series, Games, Journal (conformist).
- **Indirectly downstream of:** Integration (HLTB hours come from there into Games, then Intelligence reads them).
- **No upstream.** Intelligence is a leaf consumer.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

**Card-local adaptation:** `DesignSystem.nextEpisodeHeroCard`'s watched-with avatar
stack (top-left corner, overlapping `-space-x-3`, per-avatar `ring-2`) reuses the
styleguide `heroCard` pattern but uses `ring-white/30` instead of the specimen's
`ring-base-100` — the specimen's ring sits on `heroCard`'s flat gradient background,
while this card's avatars sit on a photographic backdrop where the dark
`ring-base-100` tone reads muddy. This is a per-card deviation, not a design-system
change (`intelligence-f6cfv`).

## Open questions

- Yearly intelligence reports (v2) — language not yet seeded.
- Friend-level intelligence (v2) — overlaps with Friends BC's open question on what they own vs. delegate here.
- Whether the projections inside Intelligence merit their own read-model store (separate from the per-BC projection tables) — currently they live alongside.
