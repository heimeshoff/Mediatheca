---
id: intelligence-wecjh
title: Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild
status: done
type: refactor
context: intelligence
created: 2026-09-06
completed: 2026-09-06
depends_on: [intelligence-encn4]
blocks: []
tags: [dashboard, dead-code, cleanup, frontend, intelligence]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk]
---

## Why
`intelligence-dq8rk` restructured the All tab to direction 3a — it stripped the Activity
section, the hero spotlight, the games 14-day play chart, the summary stats and the New Games
card, and replaced the list-row sections with poster sections. It changed the *call sites* but
never deleted the *definitions*. Later work (donut charts replacing the hand-rolled bar
helpers, the poster rewrite of the games column) left the same residue.

F# does not warn on an unreferenced `let private` at module level, and dead code compiles
cleanly, so `npm run build` has never flagged any of it. The result: **43 of the 97 top-level
definitions in `src/Client/Pages/Dashboard/Views.fs` are unreachable — roughly 2000 of its
4419 lines, 45% of the file.** Every future reader of this file pays for that, and it is
actively misleading: there are two `"Games In Focus"` sections, two `"New Games"`-shaped code
paths, and three chart builders, only some of which render.

## What
Delete the unreachable definitions from `src/Client/Pages/Dashboard/Views.fs`. The set below
was computed as a transitive closure (a helper used *only* by other dead helpers is itself
dead) and spot-verified. Line numbers are pre-change and will drift as deletions are applied —
**work bottom-up (highest line first) so earlier ranges stay valid**, and re-confirm each name
is unreferenced immediately before removing it rather than trusting these numbers.

| Lines | Count | Definition |
|---|---|---|
| 32-37 | 6 | `formatShortDate` |
| 38-53 | 16 | `formatDayOfWeek` |
| 148-176 | 29 | `sectionCardOverflowWithAction` |
| 218-274 | 57 | `seriesNextUpItem` |
| 275-381 | 107 | `seriesPosterCard` |
| 382-396 | 15 | `seriesNextUpScroller` |
| 397-407 | 11 | `seriesNextUpSection` |
| 561-597 | 37 | `movieToWatchItem` |
| 598-628 | 31 | `gameInFocusItem` |
| 629-639 | 11 | `gamesInFocusSection` |
| 640-682 | 43 | `gameRecentlyPlayedItem` |
| 683-694 | 12 | `gamesRecentlyPlayedSection` |
| 707-717 | 11 | `chartColorClasses` |
| 718-734 | 17 | `chartColorTextClasses` |
| 735-782 | 48 | `buildChartData` |
| 783-868 | 86 | `playSessionChartArea` |
| 869-872 | 4 | `playSessionBarChartNoLegend` |
| 873-906 | 34 | `playSessionBarChart` |
| 907-951 | 45 | `gamePosterFromSession` |
| 952-979 | 28 | `gamesRecentlyPlayedChart` |
| 980-1107 | 128 | `heroSpotlight` |
| 1267-1309 | 43 | `playSessionSummaryStats` |
| 1310-1350 | 41 | `gamesRecentlyPlayedChartWithStats` |
| 1351-1400 | 50 | `newGameItem` |
| 1401-1411 | 11 | `newGamesSection` — **see the open question below, do not delete blind** |
| 1495-1505 | 11 | `formatTotalTime` |
| 1506-1525 | 20 | `heroStatCard` |
| 1526-1585 | 60 | `crossMediaHeroStats` |
| 1586-1616 | 31 | `weeklyActivitySummary` |
| 1617-1767 | 151 | `activityHeatmapContent` |
| 1768-1939 | 172 | `monthlyBreakdownContent` |
| 1940-1954 | 15 | `activitySection` |
| 2102-2146 | 45 | `genreBreakdownBars` |
| 2389-2436 | 48 | `movieRecentlyWatchedItem` |
| 2643-2752 | 110 | `seriesNextUpItemEnhanced` |
| 2753-2803 | 51 | `buildEpisodeChartData` |
| 2804-2927 | 124 | `episodeActivityChart` |
| 3087-3131 | 45 | `seriesGenreBreakdownBars` |
| 3564-3572 | 9 | `gameStatusColors` |
| 3573-3626 | 54 | `gameStatusDistributionChart` |
| 3701-3745 | 45 | `gameGenreBreakdownBars` |
| 3746-3824 | 79 | `monthlyPlayTimeChart` |
| 4359-4371 | 13 | `placeholderTab` |

Also remove any section-comment banner (`// -- New Games Card --` and friends) left orphaned
above a deleted definition, and any `open` that becomes unused as a result.

**Nothing that renders may change.** This task deletes only unreachable code — it is not a
redesign, and it must not alter the output of any of the four tabs.

## Open question the worker must resolve before deleting `newGamesSection`
`intelligence-dq8rk` explicitly decided New Games would be **dropped from the All tab but stay
available on the dedicated Games tab** ("recently-added games stay available on the dedicated
Games tab"). It is referenced from neither — `gamesTabView` (~line 4263) never calls it, so
the Games tab currently shows no New Games card at all. That is a **latent regression against
dq8rk's stated intent**, not merely dead code, and the server still projects the data for it
(`getDashboardNewGames`, `Api.fs:2162`, 10 rows shipped on every dashboard load).

Two valid resolutions — **ask the builder, do not pick one silently**:
- **Re-wire:** call `newGamesSection data.NewGames` from `gamesTabView`, honouring dq8rk. Then
  `newGamesSection` / `newGameItem` are live and drop out of the deletion set.
- **Confirm the drop:** delete both, and note in the BC README that New Games was retired.

## Acceptance criteria
- [ ] Every definition listed above is either deleted, or (for `newGamesSection` /
      `newGameItem`) re-wired per the builder's answer to the open question.
- [ ] A re-run of the transitive dead-code scan over `Dashboard/Views.fs` reports **zero**
      unreferenced top-level definitions.
- [ ] `npm run build` is clean — no new warnings, no unused-`open` warnings introduced.
- [ ] `npm test` and `npm run test:client` pass.
- [ ] All four dashboard tabs (All / Movies / TV Series / Games) render **identically to
      before** in a browser smoke check — same sections, same order, same content. Verified via
      the Chrome DevTools MCP per CLAUDE.md, with no new console errors.
- [ ] `Views.fs` is materially shorter (expect ~2400 lines, down from 4419).
- [ ] No behaviour change is introduced anywhere; no server, `Shared`, or CSS file is touched.

## Notes
- **Ordering:** depends on `intelligence-encn4`, which edits `gamesInFocusPosterSection` and
  `booksColumnPlaceholder` by line reference. Landing this sweep first would invalidate every
  line number in that task. Do encn4 first.
- **How the set was derived:** parse every top-level `let [private] <name>` and its line span,
  map each identifier occurrence to the definition block containing it, then iterate to a fixed
  point removing names whose only remaining references sit inside already-dead blocks. Rerun
  the same scan as the verification step in the acceptance criteria.
- **Spot-checks confirming the method:** `genreBreakdownBars` (2102) has exactly one occurrence
  in the file, its own definition — the Movies tab's "Genre Breakdown" card now calls
  `Charts.donutChart` (line 2609) instead. `formatDayOfWeek` is referenced twice (859, 2894),
  but both call sites live inside `playSessionChartArea` and `episodeActivityChart`, which are
  themselves dead — a genuine transitive case.
- **Out of scope, worth its own capture:** the server still computes and ships dashboard payload
  fields that nothing renders — `NewGames` is the confirmed one (`Api.fs:2162` ->
  `GameProjection.getDashboardNewGames`). Once this sweep settles which client sections are
  really gone, a follow-up can prune the corresponding `Shared` fields and projection queries.
  Do not attempt that here — it touches the `IMediathecaApi` contract and server tests.

## Outcome

Resolved the open question per the builder's instruction ("confirm the drop"): `newGamesSection`
and `newGameItem` were deleted along with the rest of the dead set, and the intelligence BC
README now records the New Games card as retired.

**Method.** Wrote a throwaway Python scan (kept outside the worktree, in the session scratch
dir) that parsed every top-level `let [private] <name>` in
`src/Client/Pages/Dashboard/Views.fs` with its line span (97 defs found, matching the task's
count), built a reference graph via word-boundary identifier matching mapping each occurrence to
its enclosing definition, and iterated to a fixed point from a root set of `{view}` (the sole
non-`private` def and the file's only external call site — confirmed via
`grep -rn "Dashboard.Views" src/Client`, which shows only `src/Client/Views.fs:29` calling
`Pages.Dashboard.Views.view`). The scan found exactly the same 43 dead names the task file
listed (including `newGamesSection`/`newGameItem`, both dead — `gamesTabView` never calls
`newGamesSection`, confirming dq8rk's stated Games-tab intent was never honoured and is now
formally retired instead).

Deletion was done by a second script that additionally classified every top-level `//`/`///`
banner/doc-comment as belonging to whichever def followed it (skipping intervening blank lines
and other comment lines), so that banners only get deleted when the def they introduce is dead,
and stay put when a banner sits over a dead-then-live boundary (e.g. the "Stacked Bar Chart —
Play Sessions" banner is retained because `chartColors`, the first def under it, survives while
`chartColorClasses` and everything else in that group is dead). Deletions were applied bottom-up
in one pass by line-range removal, preserving the file's CRLF line endings (verified before
writing — a first draft naively translated CRLF to LF and was reverted via `git stash`/`drop`
before any commit-worthy state was reached).

**Result.** 14 contiguous dead runs, 1991 lines removed. `Views.fs`: 4418 -> 2427 lines (45%
reduction, matching the task's ~2400-line estimate). Re-running the scan on the post-deletion
file reports 54 total defs, 54 live, **0 dead** — the acceptance criterion's zero-unreferenced
bar is met.

**Verification performed:**
- `npm run build` — clean, 196 modules transformed, no new warnings (only the pre-existing
  unrelated `@property` CSS optimizer notice).
- `npm test` — 685 passed, 0 failed (Expecto).
- `npm run test:client` — 20 passed, 0 failed (Vitest/Fable.Mocha), only the pre-existing
  unrelated FS0020 warning in `AdminProjections/Views.fs`.
- `git diff --stat` on `Views.fs` shows only deletions in substance; the raw diff also contains
  some `+`/`-` pairs of *identical* surviving text (e.g. `chartColors`, `seriesNextEpisodeCard`,
  `gameInFocusPosterCard`) — this is Myers-diff realignment noise from one very large deletion,
  not real edits. Confirmed by reading the resulting file directly at each of those functions
  (`grep -n` + targeted `Read`): every one is complete and unmodified byte-for-byte relative to
  its pre-change body.
- **Browser smoke check deferred to the conductor** — the Chrome DevTools MCP tool is not
  available to a worker. Static argument for why all four tabs render identically: the
  post-deletion scan proves every surviving definition is reachable from `view`; every deleted
  definition was, by the same reachability proof, unreachable *before* the change too (nothing
  live called it); and spot-reads plus the clean build confirm no surviving function body was
  altered. Since no live call site or live function body changed, the render tree of each tab
  (All / Movies / TV Series / Games) is unchanged.

**TDD_SKIPPED:** behaviour-preserving dead-code deletion, no new logic to assert; existing
suites (`npm test`, `npm run test:client`) plus a clean `npm run build` and the zero-unreferenced
scan are the regression net.

**Follow-up captured:** `intelligence-p4t7k` (backlog) — prune the server-side `NewGames`
payload (`Shared.DashboardGameStats.NewGames`, `GameProjection.getDashboardNewGames`,
`Api.fs`'s dashboard handler), out of scope here per the task's explicit boundary.

**Key files:**
- `src/Client/Pages/Dashboard/Views.fs` — 4418 -> 2427 lines.
- `.agentheim/contexts/intelligence/README.md` — added a "Retired" note under Ubiquitous
  language documenting the New Games card's retirement.
- `.agentheim/contexts/intelligence/backlog/intelligence-p4t7k-prune-dead-newgames-dashboard-payload.md`
  — new follow-up task.
