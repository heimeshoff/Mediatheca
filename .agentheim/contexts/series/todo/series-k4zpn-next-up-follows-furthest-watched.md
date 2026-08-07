---
id: series-k4zpn
title: Next Up must follow the furthest-watched episode, not the first unwatched one — a skipped episode currently pins Next Up forever; when nothing remains beyond the furthest watched, show the fully-watched state even if a gap exists
status: todo
type: bug
context: series
created: 2026-08-07
completed:
depends_on: [design-system-001]
blocks: []
tags: [next-up, series-detail, dashboard, projection-view, watch-state]
related_adrs: [0046, 0048]
related_research: []
prior_art: [series-m7fdk, series-q8jwc]
---

## Why

Next Up currently answers "what is the earliest episode I have no watch record
for". That is the wrong question. If an episode gets missed — skipped, watched
elsewhere and never marked, or simply passed over — every later episode can be
watched and Next Up will *still* point at the missed one, indefinitely. The
series is stuck: the one surface whose whole job is "what do I watch next"
keeps recommending an episode the user has already moved past.

The right question is "what is the earliest episode I have not watched that
comes *after* the furthest point I have reached". Gaps behind the frontier are
history, not a queue.

This is a real defect on the primary Next Up surfaces, not a refinement — it
directly undermines the vision's intent-driven promise ("the dashboard shows
what you want to do next").

## What

Change the Next Up rule, in every place it is computed, from

> the first episode (ordered by season, then episode) with no watch record

to

> the first episode strictly *after* the furthest-watched episode, ordered by
> (season, episode), that has no watch record

where **furthest-watched** is the maximum `(season_number, episode_number)`
tuple that has a watch record. If no episode has been watched, there is no
frontier and the rule degenerates to today's behaviour — the first episode of
the series. If no unwatched episode exists beyond the frontier, there is **no
Next Up at all**, regardless of gaps below it.

Three call sites, all currently implementing the old rule independently:

1. **`series_next_up` SQL view** — `src/Server/MetadataCache.fs:335`. Today a
   `ROW_NUMBER() OVER (PARTITION BY series_slug ORDER BY season_number,
   episode_number)` over episodes whose `LEFT JOIN series_episode_progress`
   misses. The frontier must be folded in: compute the max watched tuple per
   `series_slug` and restrict candidates to those strictly greater than it.
   SQLite has no tuple comparison here — express it as
   `(e.season_number > maxS OR (e.season_number = maxS AND e.episode_number > maxE))`,
   with the no-episodes-watched case falling through to no restriction.

   Fixing the view fixes all six of its consumers at once —
   `SeriesProjection.getAll` / `getBySlug` / `getRecentSeries` /
   `getRecentlyFinished` / `getRecentlyAbandoned` / `getDashboardSeriesNextUp`
   — i.e. the dashboard "TV: Next Up" section and the series list page. This is
   the payoff of ADR-0048's query-time composition: one view, one rule.

2. **Series detail hero "Next Up" card** — `src/Client/Pages/SeriesDetail/Views.fs:1759`.
   Computed client-side over `series.Seasons` as
   `List.tryPick (fun s -> s.Episodes |> List.tryFind (fun e -> not e.IsWatched))`.
   Apply the same frontier rule over the same in-memory season/episode tree.

3. **Episodes-tab "NEXT" badge and "Coming Next" divider** —
   `src/Client/Pages/SeriesDetail/Views.fs:1206`, currently the first unwatched
   episode *within each season*. Apply the frontier rule here too (assumption
   below).

**When there is nothing left after the frontier**, the hero renders exactly
what a fully-watched series renders today: nothing — the `| None -> ()` branch
at `Views.fs:1789` already handles it, and taking that branch for the
gap-behind-the-frontier case is the whole point. No new "caught up" UI element
is in scope (builder-confirmed).

## Acceptance criteria

- [ ] Server: a series where episode (1,3) has no watch record but (1,4)–(1,10)
      do returns Next Up = (1,11) — not (1,3) — from `series_next_up`. Covered
      by a test in `tests/Server.Tests/SeriesProjectionReadsTests.fs`.
- [ ] Server: a series where the furthest-watched episode is the last episode of
      the last season returns **no row** from `series_next_up` — even with
      unwatched gaps behind it — so every consuming read function yields
      `NextUp = None`.
- [ ] Server: regression — a series with no watch records at all still returns
      the first episode overall; a series with a contiguous watch run still
      returns the episode immediately after it. The existing
      `getDashboardSeriesNextUpTests` assertions continue to hold.
- [ ] Client: the series-detail hero Next Up card, for a series with a gap
      behind the furthest-watched episode, names the episode after the frontier
      — not the gap. Covered by a Fable/Vitest unit test over the extracted
      next-up function (see Notes).
- [ ] Client: the hero Next Up card is absent when no episode exists beyond the
      frontier, whether or not gaps remain behind it.
- [ ] Client: the Episodes-tab "NEXT" badge and "Coming Next" divider mark the
      same episode the hero names, and no other.
- [ ] The change is view-and-view-logic only: no new projection column, no new
      `series_list`/`series_detail` field. `Administration.checkProjectionDrift`
      still reports zero discrepancies for `SeriesProjection` (ADR-0051's
      property is preserved).
- [ ] `npm test` green; `npm run build` clean.

## Notes

**Watch-record scope differs between server and client — keep each as it is.**
The `series_next_up` view joins `series_episode_progress` on
`(series_slug, season_number, episode_number)` only, deliberately ignoring
`rewatch_id`, so its notion of "watched" is the union across every rewatch
session (see the index comment at `SeriesProjection.fs:126`). The detail page's
`EpisodeDto.IsWatched` is scoped to the *selected* rewatch session
(`SeriesProjection.fs:938`, with a separate `overallWatched` set alongside).
Compute the frontier from whatever watch set the surface already uses — do not
unify the two scopes in this task. Changing rewatch scoping is a separate
modelling question.

**Assumption on the Episodes-tab badge (flag if it reads wrong).** Today each
season computes its own first-unwatched episode, so several seasons can each
show a "NEXT" badge. Under the frontier rule this task treats the badge as
marking the single global Next Up episode: a season lying entirely at or below
the frontier gets no badge and no divider. This keeps the badge and the hero
card in agreement, which is the point of the fix — but it is a small behaviour
change beyond the literal bug report.

**Season 0 / specials.** The `(season, episode)` ordering puts season 0 first,
so an unwatched special below the frontier is treated as history like any other
gap. That falls out of the rule; no special-casing intended.

**Unaired episodes.** The view has no air-date filter today and this task adds
none — an unaired next episode surfacing as Next Up is existing behaviour,
unchanged here.

**Client testability.** The hero's next-up computation is currently an inline
expression inside the view. Extract it to a named, testable function (a pure
`SeasonDto list -> (int * EpisodeDto) option`) so both client criteria can be
asserted without driving the DOM — the `fable-frontend-tests` skill covers the
Vitest-through-vite-plugin-fable setup. Sharing that one function between the
hero and the Episodes tab is also what mechanically guarantees the
"marks the same episode" criterion.

**Frontend gate.** `depends_on: design-system-001` per the series BC README's
frontend gate; that styleguide task is already in `done/`, so the dependency is
met and does not block.
