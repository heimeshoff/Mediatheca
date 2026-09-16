---
id: journal-k52j1
title: Reading activity in the Journal — fold `book_progress` observation days and `Book_status_changed Finished` into the cross-media activity timeline, "Recently Read" list and monthly breakdown alongside watch sessions, episodes and play sessions
status: backlog
type: feature
context: journal
created: 2026-09-16
completed:
depends_on: [books-y9kxy, intelligence-dnv2y, intelligence-h4qk2]
blocks: []
tags: [books, journal, activity, reading-progress]
related_adrs: [0076, 0050]
related_research: []
prior_art: []
---

## Why

The Journal is the diary half of Mediatheca: when and with whom media was experienced. Reading is
the fourth verb. **Premise corrected 2026-09-16:** there is no live heatmap — `intelligence-dq8rk`
removed the Activity section from the All tab and `intelligence-h4qk2` prunes the dead
`ActivityDays`/`MonthlyBreakdown` payload (including `dnv2y`'s `Reading` field) end to end. This
task makes reading a first-class activity in every Journal surface (recent activity list, monthly
breakdown, cross-media stats) so "what did I do in August" includes the books.

## What

- Journal README: add **Reading day** (sourced from Books — a `Reading_progress_observed` event,
  keyed on `observed_on`, source-tagged `Audible | Goodreads | Manual`, no friends) and **Book
  finished** (`Book_status_changed Finished`) to the ubiquitous language and the subscribed-events
  list.
- Recent-activity list (`Api.getDashboardAllTab` / the Journal read path): reading days appear as
  "Read *Title* — 40 % (Audible)" entries; a finish appears as "Finished *Title*".
- Monthly breakdown and cross-media stats: a `Reading` series (days with an observation; books
  finished) next to movies/episodes/play time.
- No new tables: reads `book_progress` and `book_list.finished_at` (Journal owns no projection).

## Acceptance criteria

- [ ] Refine before promotion: decide whether a reading day with several observations of one book
      is one entry or several (proposal: one per book per day, latest percent), and whether the
      "Recently Read" rail should replace or sit beside "Recently Watched/Played" on the All tab.
- [ ] Tests over `TestDb`: a month with two reading days and one finish yields the expected
      breakdown row; the recent-activity list interleaves a reading day with a watch session by date.

## Notes

- ADR-0076 (observations as events, day-keyed), ADR-0050 (Games' source-tagged session precedent).
- Captured alongside the Books integration set (2026-09-16); intentionally after the dashboard
  task so the heatmap contract lands first.
- 2026-09-16 (h4qk2 refinement): the "heatmap contract" above never existed on the client — the
  Activity section was removed by `intelligence-dq8rk` and the server-side payload is pruned by
  `intelligence-h4qk2` (now a dependency). When this task is refined, a Journal activity view
  (heatmap + monthly breakdown) needs its own `IMediathecaApi` method sized to that view, and its
  own daily/monthly queries — never fields on `DashboardAllTab`. The deleted queries are in
  `git show ca464a1:src/Server/{Movie,Series,Game,Book}Projection.fs` if a starting point helps.
