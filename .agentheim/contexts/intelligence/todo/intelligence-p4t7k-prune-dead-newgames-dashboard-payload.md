---
id: intelligence-p4t7k
title: Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it
status: todo
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
never re-wired it onto the Games tab. `intelligence-wecjh` confirmed with the builder that the
drop is intentional (2026-09-06) and deleted the now-dead client view helpers (`newGamesSection`,
`newGameItem`) from `src/Client/Pages/Dashboard/Views.fs`. Nothing in the client reads New Games
data anymore — `Dashboard/State.fs`, `Types.fs`, and `Views.fs` consume `DashboardAllTab` but
never touch its `NewGames` field.

The server side was left untouched by design — `intelligence-wecjh`'s scope excluded the server,
`Shared`, and `Api.fs` — but it still does real work on every dashboard load for a value nobody
reads:

- `GameProjection.getDashboardNewGames` (`src/Server/GameProjection.fs:1037`) runs a query for
  10 rows of `game_detail`, then calls `resolveFriendRefs` once per row to hydrate
  `FamilyOwners` (up to 10 extra friend lookups per dashboard load).
- `Api.fs`'s `getDashboardAllTab` handler calls it (`src/Server/Api.fs:2162`) and assigns the
  result to `NewGames` when building the response record (`Api.fs:2231`).
- `Shared.DashboardAllTab.NewGames: DashboardNewGame list` (`src/Shared/Shared.fs:430`) ships
  those rows over the wire on every load.

**Correction from refinement (2026-09-06):** the capture named the field
`DashboardGameStats.NewGames`. That record does not carry it — `DashboardGameStats` is the
Games-tab stats block (`Api.fs:2415`, rendered by `gameStatsRow` in the client) and is
untouched by this task. The dead field lives on `DashboardAllTab`, the All-tab payload. The
intelligence README's "Retired" note repeats the wrong record name; fixing it is in scope.

## What

Remove the dead `NewGames` field end to end, in four places:

1. `src/Shared/Shared.fs` — drop `NewGames: DashboardNewGame list` from `DashboardAllTab`, and
   delete the `DashboardNewGame` record type. Refinement verified that `DashboardNewGame` has
   exactly two references in the whole tree (its definition and the `getDashboardNewGames`
   mapper), so it goes with the field.
2. `src/Server/GameProjection.fs` — delete `getDashboardNewGames`. Keep `resolveFriendRefs`:
   it is `private` to the module but still used by the game-detail projection
   (`GameProjection.fs:763–766`).
3. `src/Server/Api.fs` — in `getDashboardAllTab`, remove the `let newGames = …` binding
   (line 2162) and the `NewGames = newGames` record field (line 2231).
4. `.agentheim/contexts/intelligence/README.md` — rewrite the "Retired" paragraph so it states
   the payload has been pruned (naming `DashboardAllTab`, not `DashboardGameStats`), and drop
   the "separate, not-yet-scheduled follow-up" sentence.

Nothing else moves. `DashboardGameStats`, `gameStatsRow`, the Games tab, and the client's
`DashboardAllTab` consumers are out of scope. No server test constructs or asserts on
`DashboardAllTab` (verified: zero hits under `tests/`), so no test edits are expected — the
`npm test` run is the regression gate, not a place where new assertions are needed.

This touches the `IMediathecaApi` response shape (`Shared.fs`) — the reason it was split out of
`intelligence-wecjh`. Because `Shared` compiles into both server and client, `npm run build`
is the proof the client never read the field: a Fable compile with the field gone must be clean
with no client edits.

## Acceptance criteria

- [ ] `DashboardAllTab` in `src/Shared/Shared.fs` no longer has a `NewGames` field.
- [ ] The `DashboardNewGame` record type is deleted from `src/Shared/Shared.fs`.
- [ ] `GameProjection.getDashboardNewGames` is deleted from `src/Server/GameProjection.fs`;
      `resolveFriendRefs` remains (still used by the game-detail projection).
- [ ] `Api.fs`'s `getDashboardAllTab` handler neither calls `getDashboardNewGames` nor assigns
      `NewGames`.
- [ ] `grep -rn "NewGames\|DashboardNewGame\|getDashboardNewGames" src/ tests/ --include=*.fs`
      returns nothing.
- [ ] No file under `src/Client/` is modified (the field was never read there).
- [ ] The intelligence README's "Retired" note says the server payload is pruned and names
      `DashboardAllTab`; it no longer calls the prune a pending follow-up.
- [ ] `npm test` passes (Expecto).
- [ ] `npm run build` is clean (Fable compile proves no client consumer existed).

## Notes

- Prior art: `intelligence-wecjh` (`.agentheim/contexts/intelligence/done/intelligence-wecjh-dashboard-views-dead-code-sweep.md`)
  did the client-side half of this cleanup and confirmed (with the builder) that New Games is
  retired, not merely unwired — see that task's Outcome and the intelligence README's
  "Retired" note.
- Refinement (2026-09-06) was a code-fact check rather than a modeling round: no domain
  decision is involved, no ADR is warranted, and the task does not split. The one substantive
  change was correcting the record name (`DashboardGameStats` → `DashboardAllTab`) and pinning
  down that `DashboardNewGame` and the README note are in scope while `resolveFriendRefs` is not.
- The `game_detail.steam_library_date` column and the projection that fills it are untouched —
  only the dashboard read is removed. If a "recently added games" surface ever returns, the data
  is still there; only the query needs re-adding.
