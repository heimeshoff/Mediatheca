---
id: journal-k52j1
title: Reading day and Book finished join the Journal's ubiquitous language — a README-only change, because no live activity surface exists to attach code to (the All-tab heatmap/monthly payload was pruned, and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer)
status: backlog
type: chore
context: journal
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [books, journal, ubiquitous-language, reading-progress]
related_adrs: [0076, 0050, 0043]
related_research: []
prior_art: []
---

## Why

Reading is the fourth verb in the Journal's cross-media diary, but Journal's ubiquitous language
and subscribed-events list have never named it — Books wasn't a core BC (ADR-0076) when the README
was last written.

**Premise corrected twice since capture (2026-09-16).** First, `intelligence-h4qk2` pruned the
dead 365-day heatmap / monthly-breakdown payload end to end (no client ever read it). Second, this
refinement found that `DashboardCrossMediaStats` (the supposed "live cross-media stats block") and
`getRecentActivity` / `RecentActivityItem` are **also** unconsumed payload — a grep of `src/Client`
for `CrossMediaStats`, every one of its field names, `getRecentActivity` and `RecentActivityItem`
returns nothing, and no test references them either. There is no live Journal or cross-media
activity surface of any kind today.

This task therefore does the one thing that is actually true and cheap: it gives Journal the
*language* for reading as an activity, so whoever eventually builds a real activity surface (a
separate, future task) has the vocabulary settled instead of inventing it under implementation
pressure.

## What

Edit `.agentheim/knowledge/contexts/journal/README.md` only. No source or test file changes.

1. **Ubiquitous language** — insert two bullets after **Play session** (keeping source-BC order
   Movies → Series → Games → Books):

   > - **Reading day** — sourced from Books. One *book* on one *calendar day* that at least one
   >   `Reading_progress_observed` touched (distinct from **Activity day**, which is one day across
   >   all media). One per book per day, never one per observation: ADR-0076 already collapses
   >   same-day-same-source observations, and the residual multiplicity — Audible *and* Goodreads
   >   both observing the same book that day — is integration topology, not diary fact. Unlike Play
   >   session it is **not** an upstream event: Journal derives it by grouping observations on
   >   `(book, day)`, so it survives as long as one observation for that book-day remains —
   >   removing the last observation for a book-day retracts the day; removing one of several does
   >   not. Carries **no source** (a day can be backed by several sources; "which source" is
   >   answered by the underlying `book_progress` rows, not by the day) and **no friends** —
   >   reading is solitary here, and `Book_recommended_by` is library-level provenance, not an
   >   activity relationship.
   > - **Book finished** — sourced from Books as the `Book_status_changed` transition to
   >   `Finished`; there is no separate finish event. The diary's punctuation mark for a book, the
   >   way a Watch session is for a movie — unlike Games' `Retired` (shelving, not an activity),
   >   this is diary-worthy. Dated by the effective-on date the event carries (ADR-0077), which may
   >   be **historical** when a Goodreads shelf import backfills an already-read book — a future
   >   Recent-activity view must sort on that date, not on append order.

2. **Key events — Subscribes to:** append a fourth bullet after the Games line:

   > - Books: `Reading_progress_observed`, `Reading_progress_observation_removed`,
   >   `Book_status_changed` (the `Finished` transition only; other transitions are library state,
   >   not diary).

3. **Purpose** — extend the media list: "...watch sessions (Movies), episode-watched events
   (Series), play-time changes (Games), and reading-progress observations (Books) — into a unified
   activity timeline."

4. **Aggregates** — "derived from events published by Movies / Series / Games" becomes
   "Movies / Series / Games / Books".

5. **Relationships with other contexts** — "**Downstream of:** Movies, Series, Games" becomes
   "**Downstream of:** Movies, Series, Games, Books".

6. **Open questions** — add two bullets:

   > - A live reading-activity surface (heatmap, monthly rollup, or a Recent-activity list) has no
   >   `IMediathecaApi` method today — the All tab's former `ActivityDays`/`MonthlyBreakdown`
   >   payload was pruned (`intelligence-h4qk2`), and `DashboardCrossMediaStats` /
   >   `getRecentActivity` are confirmed-dead payload with no client consumer (found during
   >   `journal-k52j1`'s refinement, 2026-09-16). When one is built, it needs its own API method
   >   sized to that view — never a field bolted onto the All-tab landing payload — and should
   >   decide whether **Reading day** (high-frequency) or **Book finished** (low-frequency,
   >   narratively significant), or both in different views, belongs in a reverse-chronological
   >   list versus a heatmap / monthly rollup. `getDailyReadingActivity`'s deleted shape
   >   (`COUNT(DISTINCT book_slug)` grouped by `observed_on` over `book_progress`, see
   >   `git show ca464a1:src/Server/BookProjection.fs`) is exactly what Reading day encodes.
   > - No source BC's `*_removed_from_library` event is in the subscribed list, so deleting a
   >   movie, series, game or book may leave phantom activity in any future diary view. Not fixed
   >   here; for whoever builds the first live activity surface.

No `Book_removed_from_library` subscription is added in step 2 — deliberately deferred to the open
question rather than added asymmetrically ahead of the other three media types.

## Acceptance criteria

- [ ] `grep -n "Reading day" .agentheim/knowledge/contexts/journal/README.md` and
      `grep -n "Book finished" .agentheim/knowledge/contexts/journal/README.md` each match inside
      the "## Ubiquitous language" section.
- [ ] `grep -n "Reading_progress_observed" .agentheim/knowledge/contexts/journal/README.md`
      matches inside the "## Key events" subscribed list.
- [ ] `grep -n "reading-progress observations (Books)"`, `grep -n "Movies / Series / Games / Books"`
      and `grep -n "Downstream of:\*\* Movies, Series, Games, Books"` on the Journal README each
      match (Purpose, Aggregates, Relationships).
- [ ] `grep -c "^## Open questions" .agentheim/knowledge/contexts/journal/README.md` is 1 and the
      section holds the two new bullets (grep `journal-k52j1` and `phantom activity`).
- [ ] `git diff --stat -- src/ tests/` is empty for this task's commit — no source or test file
      touched.

## Notes

- ADR-0076 (observation events, natural key, no-op rule), ADR-0077 (status change carries an
  effective-on date), ADR-0050 (Play session precedent — day-keyed, source-tagged, no synthetic id),
  ADR-0043 (event-worthiness doctrine underlying why Reading day is derived rather than mirrored).
- Refined 2026-09-16 (tactical-modeler via orchestrator): the original "recent-activity list",
  "monthly breakdown" and "cross-media stats" code deliverables are all dropped — the first two
  surfaces no longer exist and the third never had a consumer. The "Recently Read rail" question is
  moot — `intelligence-dnv2y`'s Reading card (All tab, 7-day finished linger) and
  `DashboardBookStats` (Books tab, finished counts) already cover it. No ADR: no aggregate,
  consistency boundary or pattern choice is introduced.
- The README delta is reported in the RESULT block and applied by the conductor. The Journal README
  is CRLF on disk — normalise LF in, CRLF out, or `readme-delta` silently appends at EOF instead of
  replacing (known gotcha).
- Follow-up candidate surfaced by this refinement, NOT captured here: prune `DashboardCrossMediaStats`
  (14 feeder queries computed on every All-tab load) and `getRecentActivity` / `RecentActivityItem`
  from `Shared.fs` / `Api.fs`, mirroring `intelligence-h4qk2` / `intelligence-p4t7k`. Belongs in
  `intelligence/`.
