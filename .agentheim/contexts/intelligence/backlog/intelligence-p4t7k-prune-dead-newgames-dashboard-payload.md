---
id: intelligence-p4t7k
title: Prune the New Games dashboard payload — server still computes and ships DashboardGameStats.NewGames but no client tab renders it
status: backlog
type: refactor
context: intelligence
created: 2026-09-06
completed:
depends_on: []
blocks: []
tags: [dashboard, dead-code, cleanup, server, intelligence]
related_adrs: []
related_research: []
prior_art: [intelligence-wecjh]
---

## Why

`intelligence-dq8rk`'s 3a rebuild dropped the New Games card from the dashboard's All tab and
never re-wired it onto the Games tab. `intelligence-wecjh` confirmed the drop is intentional
(2026-09-06) and deleted the now-dead client view helpers (`newGamesSection`, `newGameItem`)
from `src/Client/Pages/Dashboard/Views.fs`. Nothing in the client renders New Games data anymore.

The server side was left untouched by design — `intelligence-wecjh`'s scope excluded the server,
`Shared`, and `Api.fs` — but it still does real work every dashboard load for a value nobody
reads:

- `GameProjection.getDashboardNewGames` (`src/Server/GameProjection.fs:1037`) runs a query for
  10 rows on every dashboard load.
- `src/Server/Api.fs:2162` calls it and assigns the result to `NewGames` at `Api.fs:2231`.
- `Shared.DashboardGameStats.NewGames: DashboardNewGame list` (`src/Shared/Shared.fs:430`) ships
  those 10 rows over the wire on every load.

## What

Remove the dead `NewGames` field end to end: drop it from `DashboardGameStats` in
`src/Shared/Shared.fs`, remove the `getDashboardNewGames` query from `GameProjection.fs`, and
stop populating/calling it in `Api.fs`. Update any server tests that assert on
`DashboardGameStats` shape.

This touches the `IMediathecaApi` contract (`Shared.fs`) and server tests — explicitly out of
scope for `intelligence-wecjh`, hence this follow-up.

## Acceptance criteria

- [ ] `DashboardGameStats.NewGames` field removed from `src/Shared/Shared.fs`.
- [ ] `GameProjection.getDashboardNewGames` deleted from `src/Server/GameProjection.fs`.
- [ ] `Api.fs`'s dashboard handler no longer calls `getDashboardNewGames` or assigns `NewGames`.
- [ ] `DashboardNewGame` type itself is also removed if nothing else references it after the
      above (re-check — it may still back other reads; do not delete blind).
- [ ] `npm test` passes (Expecto, including any dashboard-payload-shape assertions updated).
- [ ] `npm run build` is clean.

## Notes

- Prior art: `intelligence-wecjh` (`.agentheim/contexts/intelligence/done/intelligence-wecjh-dashboard-views-dead-code-sweep.md`)
  did the client-side half of this cleanup and confirmed (with the builder) that New Games is
  retired, not merely unwired — see that task's Outcome and the intelligence BC README's
  "Retired" note.
