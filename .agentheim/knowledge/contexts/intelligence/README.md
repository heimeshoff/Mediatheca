# Intelligence

## Purpose
**Derived insights** over the library and journal. Stats blocks, breakdowns, heatmaps, HLTB comparisons, monthly play-time, watched-with stats. Read-only synthesis layer that answers "how am I doing", not "what should I watch".

## Classification
**core** — The "intelligence hub" half of the vision. The dashboard's analytical depth lives here.

## Actors
Single user.

## Ubiquitous language

- **Stats** — a snapshot of activity over a time window. Per-media-type (`DashboardMovieStats`, `DashboardSeriesStats`, `DashboardGameStats`) and cross-media (`DashboardCrossMediaStats`).
- **Activity day** — one calendar day for the heatmap (rendered Monday-first; see task 036 in archive).
- **Heatmap** — calendar of activity intensity per day.
- **Monthly breakdown** — activity rolled by month.
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
dashboard no longer computes or ships New Games data at all.
- **Card grow** (intelligence-m09d4, ADR-0073) — expanding or collapsing a dashboard card is a key-based FLIP travel, not a subtree swap-and-fade: items shared between the collapsed and expanded views (keyed on slug / person name / achievement composite id via `Motion.flipKey`, each card-scoped through `cardItemKey` so two cards that can list the same underlying item never collide) slide from their collapsed on-screen position to their expanded one in ~0.5s, while the card's own surface grows/shrinks height independently. `Views.fs`'s `growingTabArea` owns the choreography (snapshot-on-click, one `useLayoutEffect` for both directions, playing the FLIP against the visible face only since the collapsed subtree stays mounted `invisible` underneath); the `Expanded: ExpandedCard option` model shape is unchanged.

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
