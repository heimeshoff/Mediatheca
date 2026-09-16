---
id: intelligence-p7k3x
title: Prune dead DashboardCrossMediaStats / getRecentActivity / RecentActivityItem payload
status: doing
type: chore
context: intelligence
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [dead-code, dashboard, cleanup, server, intelligence]
related_adrs: []
related_research: []
prior_art: [intelligence-h4qk2, intelligence-p4t7k, intelligence-wecjh, intelligence-dq8rk]
---

## Why

`journal-k52j1`'s refinement (2026-09-16) found, and this task's worker independently re-verified by grep, that `DashboardCrossMediaStats` (populated in `Api.fs`'s `getDashboardAllTab` via 14 feeder queries computed on every All-tab dashboard load) and `getRecentActivity` / `RecentActivityItem` (a full recent-events scan plus a 40-arm event-type → English description match) have **zero references anywhere under `src/Client` or `tests/`** — no client consumer, no test coverage of the API surface. This mirrors the already-pruned All-tab heatmap/monthly-breakdown payload (`intelligence-h4qk2`) and New Games payload (`intelligence-p4t7k`) — both were removed end to end once confirmed dead.

Leaving this payload in place means every All-tab dashboard load pays for 14 feeder queries (`CrossMediaStats`) that no rendered pixel ever reads, and `getRecentActivity` is a landmine for the next person who assumes a member's presence in `IMediathecaApi` implies a live surface.

**Refinement re-verification (2026-09-17):** the grep still returns nothing under `src/Client` or `tests/` for the three symbols. Two facts the original capture missed, both folded into *What* below: (1) 12 of the 14 feeder queries and `EventStore.getRecentEvents` are single-caller and go too — h4qk2 shape, nothing half-wired; (2) one Expecto test *does* touch a feeder (`getGamesBeatenThisYear`), so the "there was no test coverage" claim was wrong for that one function.

## What

Remove the dead payload end to end. Line numbers are as of commit `933bf7b`; re-locate by symbol if they have drifted.

1. **Re-grep at execution time** — `grep -rn "DashboardCrossMediaStats\|getRecentActivity\|RecentActivityItem\|CrossMediaStats" src/Client tests/` must still return nothing before anything is deleted. If it doesn't, bounce the task back rather than guessing.

2. **`src/Shared/Shared.fs`**
   - Delete the `RecentActivityItem` record (~line 279).
   - Delete the `DashboardCrossMediaStats` record (~line 377).
   - Drop `CrossMediaStats: DashboardCrossMediaStats` from `DashboardAllTab` (~line 886).
   - Remove `getRecentActivity: int -> Async<RecentActivityItem list>` from `IMediathecaApi` (~line 1935).
   - Fix the placement comment above `DashboardMovieStats` (~line 397) that says "every other type it depends on (`DashboardSeriesNextUp` .. `DashboardCrossMediaStats`) is already declared above" — the range now ends at `DashboardPlaySession` (or whichever is the last All-tab dependency left above that comment).

3. **`src/Server/Api.fs`**
   - Delete the `getRecentActivity` member (~lines 2598–2653, the whole event-type → description match included).
   - In `getDashboardAllTab` (~lines 2656–2705), delete the `// Cross-media stats` block of 14 `let` bindings and the `CrossMediaStats = { ... }` field in the returned record.

4. **Feeder queries — delete the 12 that only `CrossMediaStats` ever called, keep the 2 with other callers:**
   - `MovieProjection.fs`: `getTotalWatchTimeMinutes`, `getMoviesWatchedThisYear`, `getMoviesWatchedThisMonth`, `getMoviesWatchedThisWeek` (~lines 864–890) — delete.
   - `SeriesProjection.fs`: `getTotalSeriesWatchTimeMinutes`, `getEpisodesWatchedThisYear`, `getEpisodesWatchedThisMonth`, `getEpisodesWatchedThisWeek` (~lines 1759–1790) — delete. **Keep `getCurrentlyWatchingCount`** (~line 1602) — the Series tab's `getDashboardSeriesTab` also calls it (`Api.fs` ~2787).
   - `GameProjection.fs`: `getTotalGamePlayTimeMinutes`, `getGamesBeatenThisYear`, `getGamesPlayedThisMonth`, `getGameMinutesThisWeek`, `getActiveGamesCount` (~lines 1293–1331) — delete. Their `// Cross-media:` header comments go with them.
   - Untouched by this task: `getGamesCompletedPerYear` (Games tab, `Api.fs` ~2857) and every other per-tab stats query.

5. **`src/Server/EventStore.fs`** — delete `getRecentEvents` (~line 349); `getRecentActivity` was its only caller. `getTotalEventCount` and the store-head query right below it stay (Administration uses them).

6. **`tests/Server.Tests/GameFacetProjectionTests.fs`** (~lines 272–284) — the testCase `"getGamesCompletedPerYear/getGamesBeatenThisYear have no stale column to fall back to at all — honest degradation"` asserts on both functions. Keep the `getGamesCompletedPerYear` half (it is still live), delete the two `getGamesBeatenThisYear` lines, and rename the case to drop the deleted function from its title. Nothing else in `tests/` references any deleted symbol.

7. **README delta (reported, conductor-applied):** the Intelligence README's *Stats* ubiquitous-language bullet currently reads "…and cross-media (`DashboardCrossMediaStats`)". Report an amended bullet that drops the cross-media clause, and a one-sentence addition to the **Retired** paragraph noting that `intelligence-p7k3x` pruned `DashboardCrossMediaStats` (14 feeders) and `getRecentActivity` / `RecentActivityItem` (plus `EventStore.getRecentEvents`) end to end, no client ever read them. The `expected` text must include the leading `- ` of the bullet, or `readme-delta` appends a duplicate instead of replacing (curation-kezpv, 2026-09-17).

Follow the same end-to-end removal shape `intelligence-h4qk2` used: type, API member, server implementation, feeder queries, payload embedding — nothing left half-wired. The source tables (`watch_sessions`, `series_episode_progress`, `game_play_session`, `game_list`, `events`) are untouched; every deleted query is a ten-line re-add if a stats surface is ever built, and such a surface gets its own API method sized to its own view rather than riding the All-tab landing payload (h4qk2's decision).

## Acceptance criteria

- [ ] `grep -rn "DashboardCrossMediaStats\|getRecentActivity\|RecentActivityItem\|CrossMediaStats\|getRecentEvents" src/ tests/` returns nothing (excluding `fable_modules/`).
- [ ] `grep -rn "getTotalWatchTimeMinutes\|getTotalSeriesWatchTimeMinutes\|getTotalGamePlayTimeMinutes\|getMoviesWatchedThisYear\|getEpisodesWatchedThisYear\|getGamesBeatenThisYear\|getMoviesWatchedThisMonth\|getEpisodesWatchedThisMonth\|getGamesPlayedThisMonth\|getActiveGamesCount\|getMoviesWatchedThisWeek\|getEpisodesWatchedThisWeek\|getGameMinutesThisWeek" src/ tests/` returns nothing.
- [ ] `SeriesProjection.getCurrentlyWatchingCount` and `GameProjection.getGamesCompletedPerYear` still exist and are still called from `Api.fs`.
- [ ] `npm run build` succeeds (Fable compiles clean with the types/member gone).
- [ ] `npm test` passes; the only test edit is the trimmed `getGamesCompletedPerYear` honest-degradation case in `GameFacetProjectionTests.fs`, and the Expecto test count drops by 0.
- [ ] `npm run test:client` passes unchanged (no client code references any deleted symbol, so nothing there should need touching).

## Notes

- **No orchestrator round** — a grep-verified dead-code deletion with a shipped precedent (`intelligence-h4qk2`) needs no specialist; the refinement's value was re-running the greps and finding the single-caller feeders and the one test assertion the capture missed.
- Capture's line references (`Shared.fs:413/315/924/1986`, `Api.fs:2716-2764/2807`) had already drifted by the time of refinement because `curation-kezpv` deleted code above them; that's why *What* pins to commit `933bf7b` and says "relocate by symbol".
- `getRecentActivity`'s 40-arm event-type → description table is the only place those English labels exist. Nothing consumes them; if an activity feed is ever built (journal-k52j1 shipped reading activity without one), it will want per-BC descriptions from the event DUs, not a string match in `Api.fs`.
