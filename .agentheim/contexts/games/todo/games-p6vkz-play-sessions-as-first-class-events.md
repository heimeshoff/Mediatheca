---
id: games-p6vkz
title: Model play sessions as first-class Games events keyed on (game, gaming day) — replacing the non-event-sourced game_play_session table and the republished-SUM Game_play_time_set
status: todo
type: feature
context: games
created: 2026-08-01
completed:
depends_on: [administration-t9bzx, design-system-001]
blocks: []
tags: [games, play-session, journal, event-sourcing, steam, determinism]
related_adrs: [0002, 0026, 0028, 0031, 0042]
related_research: []
prior_art: [games-status-vocabulary-reconcile, integration-004]
---

## Why

`game_play_session` (`src/Server/PlaytimeTracker.fs:71`) holds 42 rows of real user history across 8
games in a table that is not a projection, not event-sourced, and not rebuildable — a single point of
failure with no recovery path.

What *does* reach the log is `Game_play_time_set`: a republished `SELECT SUM(minutes_played)`
(`PlaytimeTracker.fs:314-320`), carrying `{"totalMinutes": N}`. Because it republishes a sum, it is
provably non-monotonic — verified against the live log: Grounded 2952→2282, Windrose 975→375,
Starcom 979→811. Those decreases are session edits and deletes leaking into the log as a lower total.

The per-day delta — the thing that actually happened, and the only thing Steam lets us observe once —
is computed at `PlaytimeTracker.fs:693` (`currentPlaytime - lastTotal`), written straight to the table
at line 710, and never emitted. That is the inverse of the intended design.

Closes the standing open questions at `games/README.md:60` and `journal/README.md:49`.

## What

**Four events / four commands**, unprefixed — mirroring `Watch_session_recorded` (`Movies.fs:40`);
Games is not universally `Game_`-prefixed (cf. `Want_to_play_with`):

- `Play_session_recorded` (additive)
- `Play_session_minutes_corrected`
- `Play_session_moved` (merges on collision)
- `Play_session_removed`

**Identity is the natural key `(gameSlug, gamingDay)` — no synthetic id.** The argument is mechanical,
not aesthetic: `Administration.diffTable` keys rows by declared PK, so under
`id INTEGER PRIMARY KEY AUTOINCREMENT` a shadow replay assigns different rowids and **every** row
reports `onlyInLive` / `onlyInShadow`. Removing the id is what makes the table drift-checkable at all.
Contrast Movies, which genuinely needs its GUID — one film, one day, two friend sets is real. Games
have no such distinguisher; Steam supplies no start/end times.

`PlaySessionSource` extends to `SteamSync | Manual | Imported`.

**`previousMinutes` and the trailing `minutesPlayed` are stamped by `decide` from aggregate state,
never supplied by the caller.** This is load-bearing: it makes every downstream a pure fold, so
`GameProjection`'s `total_play_time` arithmetic never has to read `game_play_session` — a *different*
projection with an independently-advancing checkpoint, which is precisely how replay nondeterminism
gets in.

**Aggregate.** `ActiveGame` gains `PlaySessions: Map<string, int>`; `TotalPlayTimeMinutes` becomes
`Σ PlaySessions.Values`. Minutes strictly `> 0` — correcting to 0 is refused, use remove. The ≤1440
ceiling and the no-future-date check stay in the **manual-session API layer**, deliberately *not*
aggregate invariants: the aggregate must accept Steam lumps and imported baselines far above 1440.
Say so in a comment, or someone will "fix" it.

**Auto-promotion moves into `Games.decide`,** fixing a real CQRS inversion. Today
`promoteToInFocusIfNeeded` (`PlaytimeTracker.fs:326`) consults `GameProjection.getGameStatus` — a read
model — to decide whether to emit an event, so promotion misfires whenever the projection lags.
`Record_play_session` returns
`[Play_session_recorded d] @ (if status <> InFocus then [Game_status_changed InFocus] else [])`, the
same shape `Record_watch_session` already uses (`Movies.fs:214-220`). ADR-0042's any-status rule is
unchanged in meaning.

**Only recording a new session promotes.** `Play_session_minutes_corrected`, `Play_session_moved` and
`Play_session_removed` do **not** promote — a deliberate narrowing of today's `updatePlaySessionApi:385`
behaviour. Fixing a typo in a February session must not yank a Retired game back into focus.

**`Set_play_time` is deleted from `GameCommand`;** `Game_play_time_set` gets an explicit
`| _, Game_play_time_set _ -> state` arm in `evolve`. **The no-op is mandatory, not tidy:**
`games-h4mrd` appends session events to streams that already contain `Game_play_time_set`, and if both
applied, replay would set the total to 2282 and then add the reconstructed 2282 on top.

**New `src/Server/PlaySessionProjection.fs`** — *not* folded into `GameProjection`: coupling would
force an operator to drop 900 games' catalog to rebuild the diary. Table PK `(game_slug, date)`;
`source TEXT` replaces the `steam_app_id = 0` sentinel; **`created_at` is dropped** — a write-time
artifact that would make every drift check report `columnMismatch` on every row.
`PlaytimeTracker.initialize` stops creating the table. Register in `Composition.fs` after
`GameProjection.handler`, and in `tableRegistry` as `Projected "PlaySessionProjection"`.

`total_play_time` stays in **`GameProjection`**, its arm moving from `Game_play_time_set`
(`GameProjection.fs:319-327`) to the four session events — pure payload arithmetic, no cross-projection
write.

**`PlaytimeTracker` cleanup.** `recordPlaySession`, `recomputeAndPublishTotal`,
`upsertManualPlaySession`, `updatePlaySession`, `deletePlaySession`, `getPlaySessionById` and
`ManualSteamAppId` are all deleted; `runSync:710` becomes a `Record_play_session` dispatch.
**Change one behaviour:** make `saveSnapshot` at line 720 conditional on `Ok` — on failure, leaving the
cursor at the old total makes the next sync re-derive the delta instead of silently losing it.

integration-004 is preserved *by construction*: two syncs in one gaming day append two events with the
same date; aggregate and projection both sum. The second delta is now **visible in the log** instead of
vanishing into a rewritten row.

**Shared/client.** `PlaySessionDto.Id` removed; `updatePlaySession: int64 * string * int` becomes a
`PlaySessionEdit` record; `deletePlaySession: int64` becomes `string * string`. Client changes are
mechanical: `GameDetail/State.fs:364`, `Views.fs:1509`, `Views.fs:1550`, and the `prop.key`.

`EventFormatting.fs` and `Api.fs:1840`'s description map each need four new arms;
`Games.Serialization.handledEventTypes:568-599` gains four entries and loses none.

## Acceptance criteria

- [ ] Expecto: two `Play_session_recorded` events on the same `(slug, date)` produce one projection row with summed minutes (integration-004 regression, ported from `PlaytimeTrackerTests.fs`).
- [ ] Expecto: `decide` rejects `minutesPlayed <= 0` on record and on correct.
- [ ] Expecto: `Record_play_session` on a `Retired` game emits `Game_status_changed InFocus`; on an `InFocus` game emits only the session event (ADR-0042 rule preserved).
- [ ] Expecto: `Correct_play_session_minutes`, `Move_play_session` and `Remove_play_session` emit **no** `Game_status_changed`, on a game in every one of the five statuses.
- [ ] Expecto: those three commands against a nonexistent session return `Error`.
- [ ] Expecto: for every game, `Games.reconstitute(stream).TotalPlayTimeMinutes` = `Σ game_play_session.minutes_played` = `game_list.total_play_time` = `game_detail.total_play_time`.
- [ ] **Expecto: `checkProjectionDrift` returns an empty discrepancy list for `PlaySessionProjection`.**
- [ ] Expecto: `Games.evolve` on `Game_play_time_set` is a no-op; its codec round-trip still succeeds; `buildUnknownEventReport` reports it neither unhandled nor unformattable.
- [ ] `grep -rc "Set_play_time\|ManualSteamAppId\|promoteToInFocusIfNeeded" src/Server/` returns 0.
- [ ] `npm test` passes; `npm run build` passes.
- [ ] The GameDetail play-session list, add, edit and delete behave as before. [human-eye]

## Notes

**ADR:** *"Play sessions are first-class Games events keyed on (game, gaming day)"*, `scope: games`.
Must record the drift-detector / `AUTOINCREMENT` argument for the natural key, the CQRS-inversion fix,
and the deliberate narrowing of auto-promotion to new sessions only.

Touches `src/Client/Pages/GameDetail/`, hence `depends_on: design-system-001` per the games BC frontend
gate (`games/README.md:56`). The client changes are mechanical DTO-shape follow-through; no new visual
vocabulary.
