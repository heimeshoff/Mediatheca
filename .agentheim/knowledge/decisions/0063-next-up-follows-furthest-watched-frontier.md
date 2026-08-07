---
id: 0063
title: Next Up follows the furthest-watched episode, not the first unwatched one
scope: series
status: accepted
date: 2026-08-07
supersedes: []
superseded_by: []
related_tasks: [series-k4zpn]
related_research: []
---

# ADR 0063: Next Up follows the furthest-watched episode, not the first unwatched one

## Context

Next Up (dashboard, series list, series-detail hero, Episodes-tab badge) answered
"what is the earliest episode with no watch record". A single skipped/missed
episode pinned Next Up at that episode forever — every later episode could be
watched and Next Up would still point at the gap, defeating the surface's
entire purpose ("what do I watch next").

Per ADR-0048, all six server read functions (`getAll`/`getBySlug`/
`getRecentSeries`/`getRecentlyFinished`/`getRecentlyAbandoned`/
`getDashboardSeriesNextUp`) compose Next Up from one shared SQL view,
`series_next_up` (ADR-0046, `MetadataCache.fs`). Two more independent
implementations of the same rule existed client-side, in
`src/Client/Pages/SeriesDetail/Views.fs`: the hero card and the
Episodes-tab "NEXT" badge.

## Decision

**The rule becomes:** the first episode, ordered by `(season, episode)`, that
has no watch record *and* comes strictly after the **frontier** — the
maximum `(season, episode)` tuple with a watch record. No watched episodes at
all ⇒ no frontier ⇒ falls back to the first episode overall (today's
behaviour, preserved). Nothing left past the frontier ⇒ no Next Up at all,
even with unwatched gaps sitting behind the frontier — those gaps are
history, not a queue.

**Server (`series_next_up` view, `MetadataCache.fs`):** the view's inner
`SELECT` gains a `LEFT JOIN` to a per-`series_slug` frontier subquery (max
`(season_number, episode_number)` tuple via `ROW_NUMBER() OVER (... ORDER BY
season_number DESC, episode_number DESC) = 1`, since SQLite has no native
tuple comparison), and the `WHERE` clause that already restricted candidates
to "no watch record" gained an additional "and strictly after the frontier
(or no frontier exists)" condition. One view, all six consumers fixed at
once — the payoff ADR-0048 was written to buy.

**View-redefinition hazard (fix, not scope creep):** `initialize` created
`series_next_up`/`series_episode_counts` with `CREATE VIEW IF NOT EXISTS`,
which is a no-op against a database that already has the view from a prior
boot (true for any live/dev database, since series-m7fdk first created it).
Redefining the view's SQL body would silently never take effect on redeploy.
Fixed by adding an unconditional `DROP VIEW IF EXISTS` immediately before
the `CREATE VIEW IF NOT EXISTS` block — cheap, since both views are computed
on read with no data to lose, and it mirrors the drop/recreate idiom already
used by `recoverStranded`'s stranded-row repair path for these same two
views.

**Client (`src/Client/Pages/SeriesDetail/NextUp.fs`, new module):** the
frontier rule is extracted into one pure function,
`compute: SeasonDto list -> (int * EpisodeDto) option`, with no Feliz
dependency. Both the hero card and the Episodes-tab badge/divider now call
this one function — mechanically guaranteeing they always agree on which
episode is Next Up (previously each season independently computed its own
first-unwatched episode, so multiple seasons could each show a "NEXT" badge;
now at most one season ever does).

**Watch-record scope is intentionally not unified between server and
client.** The `series_next_up` view's "watched" is a union across every
rewatch session (ADR-0046's deliberate choice). The series-detail client
page's `EpisodeDto.IsWatched` is scoped to the *selected* rewatch session.
`NextUp.compute` operates purely on whatever `IsWatched` values it's handed —
it doesn't need to know or care which scope produced them. Changing rewatch
scoping is a separate modelling question, out of scope here.

**Client test coverage deferred.** No Vitest infrastructure exists in this
repo. Standing it up requires a new `npm install`, which is unsafe to run
from inside a worker's git worktree (the worktree's `node_modules` is a
junction to the main tree's real one — an install there would mutate shared
state outside the worker's isolated scope). `NextUp.compute`'s correctness
is established by: (a) mirroring the SQL view's logic function-for-function,
line-by-line, and (b) 4 new Expecto tests (`SeriesProjectionReadsTests.fs`,
part of the full 680-test suite) proving the identical rule server-side,
including the exact gap/frontier/
no-more-episodes/no-watch-records/contiguous-run scenarios the task's
acceptance criteria describe. Bootstrapping Vitest and adding
`NextUp.spec` is backlogged as `series-x4qte`.

## Consequences

- A skipped episode no longer pins Next Up indefinitely on any of the six
  server-composed surfaces or either client surface.
- A fully-watched-with-gaps-behind-the-frontier series now correctly shows
  no Next Up, consistent with the fully-watched-no-gaps case.
- The Episodes-tab "NEXT" badge is now a single global marker, not a
  per-season one — a deliberate, small behaviour change beyond the literal
  bug report, called out in the task's own Notes as builder-confirmed.
- View redefinitions in `MetadataCache.initialize` now require an explicit
  `DROP VIEW IF EXISTS` to actually take effect on redeploy; future view
  changes in this file should follow the same drop-then-recreate shape.
- `series-x4qte` backlogged: bootstrap Fable/Vitest client test
  infrastructure from the main tree (not a worker worktree).

## Note on ADR numbering

Minted provisionally as ADR-0062 in its worker worktree. A sibling task's ADR already claimed that number (or the guess overshot the true count) by the time this task's conductor finalized numbering at squash-merge integration (`lib/adr-allocation.mjs`'s `finalizeAdrNumbering`, ADR-0058) — this ADR was renumbered to **ADR-0063**, the true next-free number on `main` at that moment. No content besides this identity changed.
