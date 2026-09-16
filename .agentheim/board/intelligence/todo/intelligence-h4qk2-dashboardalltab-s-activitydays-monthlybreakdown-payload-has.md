---
id: intelligence-h4qk2
title: Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them
status: todo
type: refactor
context: intelligence
created: 2026-09-16
completed:
depends_on: []
blocks: [journal-k52j1]
tags: [dashboard, dead-code, heatmap, cleanup, server, intelligence]
related_adrs: []
related_research: []
prior_art: [intelligence-p4t7k, intelligence-wecjh, intelligence-dq8rk, intelligence-dnv2y]
---

## Why

`getDashboardAllTab` computes a 365-day activity heatmap (`ActivityDays`: movie sessions,
episodes watched, game sessions and — since `intelligence-dnv2y` — distinct books read per day)
and a 12-month `MonthlyBreakdown` (movie / series / game minutes) on every All-tab load, and
ships both over the wire. No client code reads either field: a case-insensitive grep of
`src/Client` for `ActivityDays`, `MonthlyBreakdown` and `heatmap` returns nothing.

This is not a never-built feature — it is a retired one whose server half was left standing:

- Task 027 (archive) built the Activity section (heatmap + monthly breakdown) on the All tab;
  archive task 036 made the heatmap render Monday-first.
- `intelligence-dq8rk` (3a rebuild, 2026-09) **deliberately removed** the Activity section so the
  All tab reads as an intent-driven "what should I watch/play right now" surface, not a stats
  console (vision: *Unified Dashboard → All tab*, design principle *intent-driven, not a catalog*).
- `intelligence-wecjh` deleted the orphaned view helpers (`activitySection`,
  `activityHeatmapContent`, `monthlyBreakdownContent`) from `Dashboard/Views.fs`.
- `intelligence-p4t7k` pruned the sibling `DashboardAllTab.NewGames` payload end to end, but its
  scope was New Games only — `ActivityDays` / `MonthlyBreakdown` were not touched.
- `intelligence-dnv2y` was then captured on the false premise that the heatmap still existed
  ("reading days on the activity heatmap", "heatmap tooltip gains N books read"). The worker
  found no consumer, added the `Reading` field to the dead payload to satisfy its server-side
  criterion, and filed this task instead of inventing a heatmap UI.

**Decision (refinement, 2026-09-16, builder):** prune, do not rebuild. The builder removed the
analytics from the All tab on purpose two weeks ago; no Journal page exists; the vision lists
reading activity in the Journal as "Later"; and the only would-be consumer, `journal-k52j1`, is
itself a backlog task with open shaping questions. When a Journal / activity surface is built,
it gets its own API method sized to its own view — it must not ride the All-tab landing payload.
The source tables (`watch_sessions`, `series_episode_progress`, `game_play_session`,
`book_progress`) are untouched, so every one of these queries is a ten-line re-add.

## What

Remove the dead payload end to end. Line numbers are as of commit `ca464a1`; re-locate by
symbol if they have drifted.

1. **`src/Shared/Shared.fs`**
   - Delete the `DashboardActivityDay` record (lines ~418–427) and the
     `DashboardMonthlyBreakdown` record (lines ~429–434).
   - Drop `ActivityDays: DashboardActivityDay list` and
     `MonthlyBreakdown: DashboardMonthlyBreakdown list` from `DashboardAllTab` (lines ~924–925).
   - Fix the placement comment above `DashboardMovieStats` (lines ~436–440) that says "every other
     type it depends on (`DashboardSeriesNextUp` .. `DashboardMonthlyBreakdown`) is already
     declared above" — name the new last type instead of the deleted one.

2. **`src/Server/Api.fs`**, `getDashboardAllTab` (lines ~2775–2822 and ~2848–2849)
   - Delete the "Activity heatmap data (365 days)" block: the four `daily*` bindings, `allDates`,
     the four `*Map`s and `activityDays`.
   - Delete the "Monthly breakdown (12 months)" block: the three `monthly*` bindings, `allMonths`,
     the three `monthly*Map`s and `monthlyBreakdown`.
   - Remove `ActivityDays = activityDays` and `MonthlyBreakdown = monthlyBreakdown` from the
     response record.

3. **Feeder queries — delete all seven.** Each is referenced from `getDashboardAllTab` only
   (verified: `grep -rn` over `src/ tests/` finds no other call site; the Movies / Series / Games
   *tab* charts use the differently named `getMonthlyActivity`, `getMonthlyEpisodeActivity`,
   `getMonthlyPlayTime` / `getMonthlyPlayTimePerGame`, which stay):
   - `MovieProjection.getDailyMovieActivity` (~890) and `getMonthlyMovieMinutes` (~902)
   - `SeriesProjection.getDailyEpisodeActivity` (~1789) and `getMonthlySeriesMinutes` (~1846)
   - `GameProjection.getDailyGameActivity` (~1331) and `getMonthlyGameMinutes` (~1343)
   - `BookProjection.getDailyReadingActivity` (~562, with its doc comment that names
     `journal-k52j1` as the future consumer)

4. **Tests — delete the two cases that exist only to exercise the dead payload:**
   - `tests/Server.Tests/BookProjectionTests.fs` — `"getDailyReadingActivity counts distinct days"`
     (~129).
   - `tests/Server.Tests/DashboardBooksTests.fs` — `"DashboardActivityDay.Reading counts distinct
     books per day, not observation rows"` (~148) and the matching bullet in the file's header
     comment (~12). The other four cases in that file stay.
   No other test constructs or asserts on `ActivityDays` / `MonthlyBreakdown`.

5. **`src/Client/` — no edits.** As in `intelligence-p4t7k`, a clean Fable compile with the fields
   gone is the proof no consumer existed.

6. **BC READMEs — keep the language, retire the code claims.** Do not delete the ubiquitous-language
   terms; they describe a Journal/Intelligence concept the vision still intends ("Later: reading
   activity in the Journal").
   - `.agentheim/knowledge/contexts/intelligence/README.md`: annotate **Activity day**, **Heatmap**
     and **Monthly breakdown** (lines ~15–17) as *reserved language — no live surface since
     `intelligence-dq8rk`; payload pruned by `intelligence-h4qk2`*; drop the "rendered Monday-first;
     see task 036" clause. In the **Reading rail / Books tab** entry (~32) replace the sentence
     about `DashboardActivityDay` gaining a `Reading` field with one line saying the heatmap payload
     was pruned by this task. Add this task to the **Retired** paragraph next to the New Games prune.
   - `.agentheim/knowledge/contexts/journal/README.md`: in **Purpose** (~4) change "Powers the
     heatmap, ..." to say the heatmap is a planned surface, not a live one; leave **Activity day**
     and **Monthly breakdown** in the ubiquitous language as-is (they are concepts, not code).

Out of scope: the prose comments in `PlaySessionProjection.fs` (~22) and `PlaytimeTracker.fs`
(~55) that mention "the Journal heatmap" — harmless, and they describe the table's contents, not
a live view. `DashboardCrossMediaStats` and every other `DashboardAllTab` field stay.

## Acceptance criteria

- [ ] `src/Shared/Shared.fs` has no `DashboardActivityDay` or `DashboardMonthlyBreakdown` type and
      `DashboardAllTab` has no `ActivityDays` / `MonthlyBreakdown` field.
- [ ] `Api.getDashboardAllTab` no longer calls any `getDaily*Activity` or `getMonthly*Minutes`
      query and no longer assigns `ActivityDays` / `MonthlyBreakdown`.
- [ ] The seven feeder queries (`getDailyMovieActivity`, `getMonthlyMovieMinutes`,
      `getDailyEpisodeActivity`, `getMonthlySeriesMinutes`, `getDailyGameActivity`,
      `getMonthlyGameMinutes`, `getDailyReadingActivity`) are deleted;
      `getMonthlyActivity`, `getMonthlyEpisodeActivity`, `getMonthlyPlayTime` and
      `getMonthlyPlayTimePerGame` remain and the Movies / Series / Games tabs still compile against
      them.
- [ ] `grep -rn -i "ActivityDays\|MonthlyBreakdown\|DashboardActivityDay\|DashboardMonthlyBreakdown\|getDailyMovieActivity\|getDailyEpisodeActivity\|getDailyGameActivity\|getDailyReadingActivity\|getMonthlyMovieMinutes\|getMonthlySeriesMinutes\|getMonthlyGameMinutes" src/ tests/ --include=*.fs`
      returns nothing.
- [ ] No file under `src/Client/` is modified.
- [ ] The two dead test cases are removed; `DashboardBooksTests.fs` keeps its other four cases.
- [ ] The intelligence README marks Activity day / Heatmap / Monthly breakdown as reserved language
      with no live surface, its Reading-rail entry no longer describes a `Reading` heatmap field,
      and its Retired paragraph names this task; the journal README's Purpose no longer claims to
      power a live heatmap. No ubiquitous-language term is deleted from either README.
- [ ] `npm test` (Expecto) passes.
- [ ] `npm run build` is clean (Fable compile proves no client consumer existed).
- [ ] `npm run test:client` (Vitest) passes.

## Notes

- Refinement (2026-09-16) was a code-fact check plus one builder decision (prune vs. rebuild —
  prune chosen; see Why). No domain decision, no ADR, no split. The task changed from a
  build-or-prune judgment call (`chore`) to a prescribed prune (`refactor`).
- `journal-k52j1` (Journal backlog) was captured assuming "intelligence-dnv2y already puts reading
  days on the heatmap". That premise is false — corrected in its Notes during this refinement, and
  it now `depends_on` this task so it is shaped against the pruned tree, not the dead payload. When
  it is refined, a Journal activity view should get its own `IMediathecaApi` method (heatmap +
  monthly breakdown sized to that view), never a field on `DashboardAllTab`.
- Prior art: `intelligence-p4t7k` is the template (same shape, same four-file prune, same
  "Fable compile is the proof" gate). `intelligence-dq8rk` holds the decision that removed the
  Activity section; `intelligence-wecjh` deleted the client views; `intelligence-dnv2y`'s Outcome
  ("Scope note on the heatmap tooltip line") is where the gap was found.
- `book_progress`, `watch_sessions`, `series_episode_progress` and `game_play_session` are untouched.
  The deleted queries are trivially re-derivable from `git show ca464a1:src/Server/<file>.fs`.
