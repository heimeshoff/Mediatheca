---
id: integration-006
title: Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up
status: todo
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

**Cadence decided (refinement 2026-06-26): activity-gated.** An `Ended` series re-enters
the nightly candidate set only when it carries a *recency signal* — it is `in_focus`, or it
has been watched within the last **180 days** (`MAX(series_episode_progress.watched_date)`).
This targets exactly the shows the user is engaged with — the only ones where a surprise
new season actually matters — and keeps the `Ended` additions bounded, so the candidate
set never balloons to the whole finished library. Chosen over a staleness window (would
need a new `last_refreshed` projection column + event-handler change, and re-fetches
genuinely-finished shows forever) and over a separate slower schedule (a second
`ScheduledJobs` entry that still re-fetches every finished show). The 180-day window and
the `in_focus` signal are tunable knobs, not invariants.

## Acceptance criteria

- [ ] `getRefreshCandidates` (`src/Server/SeriesRefresh.fs:316`) returns an `Ended` series
      when it is `in_focus = 1` **or** its most recent `watched_date` in
      `series_episode_progress` is within 180 days — in addition to the existing
      `Returning` / `InProduction` set.
- [ ] An `Ended` series with **no** recency signal (not `in_focus`, no watch activity in
      the window) is **not** a nightly candidate (so a large finished library is not
      re-fetched every night).
- [ ] An `Ended` series for which TMDB later adds a season, and which meets the recency
      gate, is re-checked by the automated path and the new season/episodes appear (no
      manual refresh required).
- [ ] The nightly job's existing throttle (`Async.Sleep 500` between series) is unchanged;
      the candidate-set growth is bounded by the recency gate, so the TMDB rate budget is
      preserved.
- [ ] A test exercises both the positive path ("Ended + recency signal becomes a refresh
      candidate") and the negative path ("Ended + no signal stays excluded").

## Notes

- **Implementation shape:** a single `WHERE`-clause change in `getRefreshCandidates`. No
  schema migration — `in_focus` lives on `series_detail` and the last-watched date is the
  existing `MAX(watched_date)` subquery over `series_episode_progress` (the same subquery
  the dashboard's `getDashboardSeriesNextUp` already uses, `SeriesProjection.fs:1115`).
  Sketch:

  ```sql
  SELECT slug FROM series_detail sd
  WHERE status IN ('Returning', 'InProduction')
     OR (status = 'Ended' AND (
           in_focus = 1
           OR (SELECT MAX(watched_date) FROM series_episode_progress
               WHERE series_slug = sd.slug) >= date('now', '-180 days')
        ))
  ORDER BY slug
  ```
  (Mind `watched_date` nullability and the `date('now', …)` comparison — dates are stored
  as ISO strings, so lexical comparison is correct.)
- Independent of [[integration-005]] (which *source* we use). This task is purely the
  candidate filter for the existing TMDB path; integration-005 is the separate question of
  materializing a season from Jellyfin when TMDB never delivers. They share the "TMDB
  didn't deliver" origin but ship independently — and they compose: 006 ensures TMDB's
  eventual season is auto-discovered, 005 covers the case where TMDB never adds it.
- Manual "Refresh from TMDB" already ignores status (`SeriesRefresh.refreshOne` does not
  filter), so the manual escape hatch works today — this bug is only about the *automatic*
  path, and remains the fallback for an `Ended` show with no recent activity that
  surprise-returns.
- The cadence decision was kept inline (Why/What above) rather than promoted to an ADR: it
  is a single reversible candidate-filter heuristic in a generic BC, with no cross-cutting
  or persistence impact.
