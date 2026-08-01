---
id: administration-t9bzx
title: Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions
status: done
type: refactor
context: administration
created: 2026-08-01
completed: 2026-08-01
depends_on: []
blocks: []
tags: [projection, drift, registry, taxonomy]
related_adrs: [0025, 0031, 0021, 0044]
related_research: []
prior_art: [administration-btvqa, administration-xx3mw, administration-qjcp4]
---

## Why

`Administration.projectionTables` (`src/Server/Administration.fs:367`) covers 6 handlers and their
15 tables. Everything else in `mediatheca.db` — `game_play_session`, `steam_playtime_snapshot`,
`job_runs`, `settings`, `cast_members`, `game_journal_blocks`, `jellyfin_movie` / `jellyfin_series` /
`jellyfin_episode` — is classified only **negatively**: in scattered comments explaining why they are
omitted (`Administration.fs:500-502`, `381-384`), plus a defensive `tableExists` guard.

That is tribal knowledge encoded as absence, and it is why `game_play_session` could exist for months
holding 42 rows of unrebuildable user history with nothing in the system flagging it.

## What

- Add to `src/Server/Administration.fs`:

  ```fsharp
  type TableClass =
      | Projected of projectionName: string
      | Cache of refreshedBy: string
      | Imperative of writtenBy: string

  let tableRegistry : (string * TableClass) list = [ ... ]
  ```

  covering **every** durable table in `mediatheca.db`.

- Derive `projectionTables` from `tableRegistry`, preserving its exact existing
  `(projectionName * tableNames) list` shape, so `buildProjectionStats` and `checkProjectionDrift`
  are untouched.
- `checkProjectionDrift` continues to diff `Projected` tables only — no change needed, it already
  walks `projectionTables`.
- Surface `Cache` / `Imperative` row counts in `getProjectionStats` as a separate, explicitly
  un-rebuildable section. Server-side only; client display is optional and may be deferred.

## Acceptance criteria

- [ ] Expecto: every table returned by `SELECT name FROM sqlite_master WHERE type='table'` on a fully-initialized fixture — minus SQLite internals and `events*` / `projection_checkpoints` — appears exactly once in `tableRegistry`. This is the test that keeps the registry honest.
- [ ] Expecto: the derived `projectionTables` is set-equal, per projection, to the current hardcoded list.
- [ ] `checkProjectionDrift`'s existing tests in `tests/Server.Tests/ProjectionDriftTests.fs` pass unmodified.
- [ ] `game_play_session` and `steam_playtime_snapshot` each carry an explicit `Imperative` classification naming their writer.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

The payoff lands immediately in `games-p6vkz`: when play sessions become events,
`game_play_session` moves `Imperative → Projected` as a one-line diff that auto-enrolls it in drift
checking.

**ADR:** *"Every durable table is classified Projected | Cache | Imperative"*, `scope: administration`.
Supersedes the private `projectionTables` list and consolidates the scattered "these are imperative
writes and need no gating" comments in ADR-0025 / ADR-0031. Reasonable fold: merge into
`administration-c3nvp`'s ADR if one administration ADR is preferred over two.

`steam_playtime_snapshot` deserves its own note in the registry: today it is neither projection nor
cache but a **sync cursor** — external state remembered in order to compute the next delta, not
derivable from our log at all. If a snapshot row is lost, `getLastSnapshot` returns `None` and
`PlaytimeTracker.fs:667-680` records the entire lifetime total as one new session.

Classify it `Imperative "PlaytimeTracker"` here — it still exists at this point in the sequence — but
**note in the registry that `games-p6vkz` deletes it**: once prior playtime and every session are in
the log, `ActiveGame.SteamObservedMinutes` is the cursor, derived by replay, and the hazard closes by
construction rather than being guarded. This entry should disappear in that task's diff.

## Outcome

Added `Administration.TableClass` (`Projected | Cache | Imperative`) and `Administration.tableRegistry`
covering all 27 durable tables in `mediatheca.db` (15 `Projected`, 3 `Cache` — the `jellyfin_*` tables,
refreshed by Jellyfin sync — and 9 `Imperative`, including `game_play_session` and
`steam_playtime_snapshot` as required, the latter carrying the sync-cursor note verbatim from this
task's Notes section). `projectionTables` is now derived from `tableRegistry` by filtering to
`Projected` entries and grouping by projection name, so the two can never drift apart; `checkProjectionDrift`
and `buildProjectionStats` are unmodified and consume the derived value exactly as before.

Added `IAdminApi.getUnrebuildableTableStats` (additive, server-side only, `UnrebuildableTableStat` DTO)
surfacing `Cache`/`Imperative` row counts as their own section, gated by the same `tableExists` guard
`getReferencedImageRefs` uses — a separate Remoting method rather than a change to `getProjectionStats`'s
existing return shape, so no client code needed to change; client display is deferred per the task.

Tests: `tests/Server.Tests/TableClassificationTests.fs` (4 new Expecto tests) — registry-coverage
(`tableRegistry` set-equals every non-`sqlite_*`/non-`events*`/non-`projection_checkpoints` table in a
fully-initialized schema, no duplicates), the explicit `game_play_session`/`steam_playtime_snapshot`
`Imperative "PlaytimeTracker"` classification, derived-`projectionTables` set-equality per projection
against the original hardcoded list, and `getUnrebuildableTableStats` behavior (Cache/Imperative rows
reported, Projected tables excluded). `tests/Server.Tests/ProjectionDriftTests.fs` passes unmodified.
Full suite: 449/449 passing (`-- --sequenced`); `npm run build` passes.

ADR: `.agentheim/knowledge/decisions/0044-every-durable-table-classified-projected-cache-imperative.md` (authored as 0043, renumbered at integration).
BC README updated: new "Table classification registry" bullet, and the Projections-tab bullet's
description of `projectionTables` updated to say "derived" rather than "hardcoded".

Key files: `src/Server/Administration.fs`, `src/Shared/Shared.fs`,
`tests/Server.Tests/TableClassificationTests.fs`, `tests/Server.Tests/Server.Tests.fsproj`.
