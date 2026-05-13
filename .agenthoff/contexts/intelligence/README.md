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

## Open questions

- Yearly intelligence reports (v2) — language not yet seeded.
- Friend-level intelligence (v2) — overlaps with Friends BC's open question on what they own vs. delegate here.
- Whether the projections inside Intelligence merit their own read-model store (separate from the per-BC projection tables) — currently they live alongside.
