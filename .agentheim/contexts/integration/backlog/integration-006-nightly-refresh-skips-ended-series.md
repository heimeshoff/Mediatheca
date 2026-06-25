---
id: integration-006
title: Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up
status: backlog
type: bug
context: integration
created: 2026-06-25
depends_on: []
blocks: []
tags: [tmdb, series, sync, scheduled-job, refresh, status]
related_adrs: []
related_research: []
prior_art: []
---

## Why

The nightly refresh only re-checks series whose status is `Returning` or `InProduction`
(`SeriesRefresh.getRefreshCandidates`, `src/Server/SeriesRefresh.fs:316`). When TMDB has
not yet added a new season, it still lists the show's status as `Ended` (per TMDB there is
no new season), so the show is excluded from the nightly candidate set. The consequence:
even once a TMDB volunteer adds the missing season, the app will *never* pick it up
automatically — it stays invisible until someone manually hits "Refresh from TMDB" on
that exact series. This is the second-order bug hiding behind the *Interview with the
Vampire* S3 report: the show may be stuck as `Ended` in our projection.

## What

Make sure a series that TMDB currently reports as `Ended` can still be re-checked
periodically, so a newly-added TMDB season is discovered without a manual refresh —
while not hammering TMDB on every nightly run for a large catalogue of genuinely-finished
shows.

## Acceptance criteria

- [ ] An `Ended` series for which TMDB later adds a season is eventually re-checked by an
      automated path (not only via manual refresh) and the new season/episodes appear.
- [ ] The solution does not re-fetch every `Ended` series on every nightly run for a large
      library (e.g. a slower cadence for `Ended` series, or a staleness window) — decide
      and document the cadence.
- [ ] Throttling / TMDB rate-budget behaviour of the nightly job is preserved.
- [ ] A test exercises the "Ended series becomes a refresh candidate again" path.

## Notes

- Open design point (why this is in backlog, not todo): the *cadence* for re-checking
  `Ended` series isn't pinned yet — options include a separate slower schedule, a
  "last-refreshed older than N days" staleness gate, or only re-checking `Ended` series
  with recent watch activity. Pick one during refinement.
- Independent of [[integration-005]] (which source we use) — this is purely about the
  candidate filter for the existing TMDB path. They share the "TMDB didn't deliver"
  origin but can be worked separately.
- Manual "Refresh from TMDB" already ignores status (`SeriesRefresh.refreshOne` does not
  filter), so the manual escape hatch works today — this bug is only about the *automatic*
  path.
