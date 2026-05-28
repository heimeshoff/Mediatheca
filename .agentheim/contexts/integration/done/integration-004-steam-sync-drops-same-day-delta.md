---
id: integration-004
title: Steam playtime sync silently drops same-day deltas
status: done
type: bug
context: integration
created: 2026-05-28
completed: 2026-05-28
commit:
depends_on: []
blocks: []
tags: [steam, sync, playtime, games]
related_adrs: []
related_research: []
prior_art: []
---

## Why

When the user syncs Steam playtime twice within the same gaming day — e.g.
syncs in the evening after a session, plays another hour late at night, then
syncs the next morning — the second delta is silently dropped and the minutes
are lost forever.

Concrete repro:
1. Day 1, 21:00 — user plays for 100 min, clicks "sync playtime" on the
   dashboard. `runSync` computes delta=+100, inserts a row for `"Day 1"` with
   100 min, snapshot bumps from 100 -> 200.
2. Day 1, 23:00 — user plays another 60 min. Steam total now 260.
3. Day 2, 09:00 — user syncs again. delta=+60. The `lastDate < today` branch
   correctly attributes the session to `"Day 1"` via `rtime_last_played`
   (`src/Server/PlaytimeTracker.fs:666-669`). But `recordPlaySession`
   (`PlaytimeTracker.fs:108`) uses `INSERT OR IGNORE` against
   `UNIQUE(game_slug, date)` (`PlaytimeTracker.fs:64`) — the existing Day 1 row
   blocks the insert, the 60 min is dropped, and the snapshot still updates to
   260.

After step 3, the game's `TotalPlayTimeMinutes` is permanently 60 min short of
Steam's reported total, and the next sync will see delta=0 — the lost minutes
are never recovered.

The existing test "Manual sessions do not interfere with Steam delta tracking"
(`tests/Server.Tests/PlaytimeTrackerTests.fs:237`) uses different dates per
call, so the conflict path is uncovered.

## What

Change the Steam-sync delta-recording path so that when a session row already
exists for the same `(game_slug, date)`, the new delta is **summed into the
existing row** instead of being dropped. The merge-on-conflict pattern already
exists for manual sessions in `upsertManualPlaySession`
(`PlaytimeTracker.fs:179`) — apply the same SQL semantics to
`recordPlaySession`:

```sql
INSERT INTO game_play_session (game_slug, steam_app_id, date, minutes_played, created_at)
VALUES (@slug, @app_id, @date, @minutes, @now)
ON CONFLICT(game_slug, date) DO UPDATE SET
    minutes_played = minutes_played + excluded.minutes_played
```

This keeps the per-gaming-day attribution (one row per day) and makes the daily
total truthful when multiple syncs hit the same day.

## Acceptance criteria

- [x] When `runSync` records a Steam delta against the same gaming day as an
      existing `game_play_session` row, the row's `minutes_played` is summed
      (not silently dropped).
- [x] After the merge, `GameProjection.getBySlug(...).TotalPlayTimeMinutes`
      reflects the new total (via `recomputeAndPublishTotal`).
- [x] New regression test in `tests/Server.Tests/PlaytimeTrackerTests.fs`:
      seed snapshot=200 with a session row for day D (100 min) ->
      `recordPlaySession` for day D (+60 min, same steam_app_id) -> assert
      the row for day D now reads 160 min and `getTotalFromProjection`
      reflects the bump.
- [x] Initial-snapshot path (`getLastSnapshot = None`,
      `PlaytimeTracker.fs:631-643`) and reconciliation path
      (`hasAnyPlaySessions = false`, `PlaytimeTracker.fs:645-654`) remain
      correct: the precondition (`not (hasAnyPlaySessions)`) on the
      reconciliation path means no conflict can occur there; the initial path
      is a brand-new Steam game and any pre-existing manual session for the
      same day legitimately merges.
- [x] Existing tests in `PlaytimeTrackerTests.fs` (including
      "Manual sessions do not interfere with Steam delta tracking") remain
      green.
- [x] `npm run build` clean and `npm test` green.

## Notes

- The fix is one-line SQL in `recordPlaySession` plus a new test. No new
  function needed — change the semantics of the existing one, since all three
  call sites are safe with merge-on-conflict (see acceptance criteria #4).
- Source labelling is unaffected: the SQL above doesn't touch `steam_app_id`,
  so an existing manual row (app_id=0) that gets a Steam delta merged in stays
  labelled `Manual` in `toPlaySessionDto`. Acceptable: the row's date-bucket
  total is what matters; per-source breakdown is not a current UI need.
- Gaming-day boundary (syncHour + 30 min grace, default 04:30 local) is the
  reason a 23:00 session lands on the previous day — keep this in mind when
  writing the regression test.
- This is a write-path bug — no event-store schema change, no event types
  added, no projection rebuild required. Pure SQL semantic fix on the
  read-side cache table.

## Outcome

`PlaytimeTracker.recordPlaySession` now uses `ON CONFLICT(game_slug, date) DO
UPDATE SET minutes_played = minutes_played + excluded.minutes_played` (mirroring
`upsertManualPlaySession`) instead of `INSERT OR IGNORE`. When two Steam syncs
both attribute to the same gaming day — e.g. an evening sync followed by a
late-night session that the next morning's sync routes to the previous day via
`rtime_last_played` — the second delta is now summed into the existing row
instead of being silently dropped, and `recomputeAndPublishTotal` propagates
the bump to `TotalPlayTimeMinutes`. `steam_app_id` is intentionally not
overwritten on conflict, so a pre-existing Manual row (app_id=0) that absorbs
a Steam delta stays labelled `Manual` in `toPlaySessionDto`.

Key files:
- `src/Server/PlaytimeTracker.fs` — `recordPlaySession` SQL switched to
  merge-on-conflict (lines around 106-126).
- `tests/Server.Tests/PlaytimeTrackerTests.fs` — added regression test
  "Same-day Steam delta merges into the existing session row instead of being
  dropped", which seeds a row for day D with 100 min, calls
  `recordPlaySession` again for day D with +60 min, and asserts the row reads
  160 min and the projection total matches.

All 266 Expecto tests pass (was 265 before); `npm run build` clean. The
"Manual sessions do not interfere with Steam delta tracking" test continues to
pass — it uses distinct dates per call, so it exercises the no-conflict path
and is unaffected by the new conflict behaviour.
