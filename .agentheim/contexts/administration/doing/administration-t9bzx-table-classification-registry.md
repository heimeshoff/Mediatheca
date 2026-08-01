---
id: administration-t9bzx
title: Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions
status: doing
type: refactor
context: administration
created: 2026-08-01
completed:
depends_on: []
blocks: []
tags: [projection, drift, registry, taxonomy]
related_adrs: [0025, 0031, 0021]
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
