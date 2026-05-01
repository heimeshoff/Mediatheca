# Task: Remove `Playing` Status; Auto-Promote to `InFocus` on Steam Play

**ID:** 048
**Milestone:** --
**Size:** Medium
**Created:** 2026-05-01
**Dependencies:** --

## Objective

Collapse the redundant `Playing` game status into `InFocus` (one fewer state to reason about), and during the scheduled Steam sync auto-promote any game with new play activity to `InFocus` if it isn't already — including games that were `Completed`, `Abandoned`, or `Dismissed`. The user's intent for "in focus" thus covers everything the user is *currently engaged with*, regardless of prior terminal status.

## Background

`GameStatus` today (`src/Shared/Shared.fs:608`):

```fsharp
type GameStatus =
    | Backlog | InFocus | Playing | Completed | Abandoned | OnHold | Dismissed
```

`Playing` and `InFocus` overlap in practice — both describe an active game — and the dashboard "Games in Focus" widget filters on `InFocus` only (`GameProjection.fs:577`), so games that were manually moved to `Playing` silently disappear from the dashboard. Removing `Playing` keeps the model honest.

Steam sync (`PlaytimeTracker.runSync`, `src/Server/PlaytimeTracker.fs:302`) already records play sessions when it detects a positive delta vs. the last snapshot, but it never touches the game's status. After this task, every recorded session also drives the game into `InFocus`.

The event store contains historical `Game_status_changed Playing` events (encoded as the string `"Playing"` in event payloads, see `Games.fs:345`). We will **not rewrite the event store**; instead, the deserializers map the legacy string to `InFocus` on read.

## Details

### Part 1 — Remove `Playing` from `GameStatus`

#### 1.1 Domain (`src/Shared/Shared.fs:608`)
Remove the `Playing` case from the `GameStatus` DU.

#### 1.2 Backend serialization with legacy compat
Both `Games.fs` (event payload codec) and `GameProjection.fs` (projection row codec) have symmetric `encodeGameStatus` / `decodeGameStatus` pairs.

- **Encode**: drop the `Playing -> "Playing"` case (`Games.fs:345`, `GameProjection.fs:82`).
- **Decode**: keep accepting the literal string `"Playing"` and **map it to `InFocus`** so historical events and legacy projection rows still load.
  - `Games.fs:355`: `| "Playing" -> InFocus  // legacy — folded into InFocus by task 048`
  - `GameProjection.fs:92`: same.
- `EventFormatting.fs:43`: keep `"Playing" -> "InFocus"` (or simply drop the case if the formatter has a sensible default — verify first).

#### 1.3 Projection queries that hard-code `'Playing'`
- `Api.fs:1899` — `gamesInProgress` query: `WHERE status = 'Playing'` → `WHERE status = 'InFocus'`. The "in-progress" stat now means "in focus", which is the desired collapsed semantic.
- `GameProjection.fs:889` — same query, same fix.
- Verify nothing else (genre/status distribution, etc.) references `'Playing'` as a literal.

#### 1.4 One-time data migration
On server startup (or as part of a projection rebuild step — match the existing pattern), run once:

```sql
UPDATE game_list SET status = 'InFocus' WHERE status = 'Playing';
```

Place it next to existing migrations / projection setup. Idempotent — safe to re-run.

#### 1.5 Frontend
- `src/Client/Pages/Games/Views.fs:15,34,103` — remove `Playing` from the status name map, color map, and `allStatuses` list.
- `src/Client/Pages/GameDetail/Views.fs:69,79,289` — same: badge class, display name, `allStatuses` list.
- `src/Client/Pages/Dashboard/Views.fs:3435` — remove `"Playing" -> "bg-info"` case (verify the surrounding match still type-checks; the wildcard should cover it).

> **Note**: `PlayingTrailerUrl` in `GameDetail/State.fs` and `Types.fs` is unrelated — it tracks an actively-playing trailer video, not game status. Leave it alone.

#### 1.6 Tests touching `Playing` (`tests/Server.Tests/GamesTests.fs`)
The following tests currently use `Playing` as a non-trivial status target:
- Lines 90, 96, 111, 114, 120 — `Change_status Playing` and the `"Transition InFocus to Playing"` test.
- Lines 387, 438, 524 — fixtures that emit `Game_status_changed Playing`.

For each: replace `Playing` with another status (e.g. `OnHold` or `Completed`) so the test still meaningfully covers the transition. Delete tests that exist only to verify `Playing` semantics. Add one new test that asserts the **legacy decoder** still maps a stored `"Playing"` payload to `InFocus` (round-trip through `decodeGameStatus`).

### Part 2 — Auto-promote to `InFocus` during Steam sync

#### 2.1 Logic in `PlaytimeTracker.runSync` (`src/Server/PlaytimeTracker.fs:302`)
The two existing branches that record a play session both increment `sessionsRecorded`:
- First-snapshot branch (`PlaytimeTracker.fs:354–361`)
- Reconciliation backfill (`PlaytimeTracker.fs:367–373`)
- Delta-positive branch (`PlaytimeTracker.fs:377–394`)

After **any** of these records a session for `slug`, fetch the game's current status and, if it is **not** `InFocus`, emit:

```fsharp
executeGameCommand conn slug (Games.Change_status InFocus) projectionHandlers |> ignore
```

Implementation hint — add a single helper at the top of the function so all three branches share it:

```fsharp
let promoteToInFocusIfNeeded slug =
    match GameProjection.getGameStatus conn slug with
    | Some InFocus -> ()
    | Some _ | None ->
        executeGameCommand conn slug (Games.Change_status InFocus) projectionHandlers |> ignore
```

…and call it next to each `sessionsRecorded <- sessionsRecorded + 1` line. Verify `getGameStatus` exists on `GameProjection`; if not, add a small helper that reads `status` from `game_list` for a slug.

**Scope confirmed**: this fires regardless of prior status. `Backlog`, `OnHold`, `Completed`, `Abandoned`, `Dismissed` all get pulled back into `InFocus` when Steam reports new playtime. Only games already `InFocus` are skipped (avoids redundant events).

**No promotion when the sync only updates metadata** (no session recorded): if `delta = 0` and there was an existing snapshot, nothing changes. This is correct — Steam often refreshes `rtime_last_played` without playtime increasing, and we don't want that to unbury a finished game.

#### 2.2 Track in result + log line
Add `GamesPromotedToFocus: int` to `PlaytimeSyncResult` and increment it whenever the helper actually emits the event. Update the log line in `Program.fs:267`:

```
[PlaytimeTracker] Sync complete: %d sessions, %d snapshots, %d games created, %d promoted to focus
```

This makes the sync's effect visible in the server log without needing a UI change.

#### 2.3 Tests (`tests/Server.Tests/`)
Add to the existing playtime-tracker test fixture (or create one if none exists yet — check what `PlaytimeTracker` tests look like). Cover:
- Game in `Backlog` with new Steam playtime → status becomes `InFocus`, `GamesPromotedToFocus = 1`.
- Game in `Completed` with new Steam playtime → status becomes `InFocus` (confirm the task's broad-scope decision).
- Game in `Abandoned` / `OnHold` / `Dismissed` → same.
- Game already in `InFocus` → no `Game_status_changed` event emitted, `GamesPromotedToFocus = 0`.
- Game in `Backlog` with no new playtime (delta = 0, snapshot exists) → status unchanged.
- First-snapshot branch (no prior snapshot, existing Steam playtime > 0) → promotes (this is the "we just discovered this game on Steam and it has hours" case).

## Acceptance Criteria

- [ ] `GameStatus` no longer contains `Playing`; the project compiles cleanly via `npm run build`
- [ ] Legacy events / projection rows containing `"Playing"` deserialize to `InFocus` without error (regression test added)
- [ ] One-time `UPDATE game_list SET status='InFocus' WHERE status='Playing'` runs on startup; verifying on a DB copy that previously had `Playing` rows shows them all converted
- [ ] Status pickers on the Games list and Game Detail pages no longer offer `Playing`
- [ ] Dashboard "in-progress" stat is now driven by `status='InFocus'` and matches the dashboard list count
- [ ] Running a Steam sync that records at least one new play session for a game whose status was `Backlog` (or `Completed`, `Abandoned`, `OnHold`, `Dismissed`) flips that game to `InFocus`; the change is visible on the dashboard immediately
- [ ] A Steam sync that records new playtime for a game already in `InFocus` does **not** emit a redundant `Game_status_changed` event
- [ ] A Steam sync where only `rtime_last_played` changed (no playtime delta) does **not** change any game's status
- [ ] `Program.fs` log line reports the count of games promoted to focus per sync
- [ ] All existing `npm test` tests pass; new tests cover the auto-promotion matrix and legacy decoder
- [ ] Design check: no UI changes required, but verify the removed status doesn't leave dead branches in match expressions (compiler will surface this)

## Notes

- **Why no event-store rewrite**: replaying every `Game_status_changed Playing` as `Game_status_changed InFocus` requires a new event-store migration with rollback risk. Mapping on read is simpler, idempotent, and fine — there's only one consumer of the codec (the projection rebuild), and the decoder folds the legacy value transparently.
- **Why also promote `Completed` / `Abandoned`**: per the user's intent, "in focus" should reflect what they're *currently engaged with*. If a finished game is being replayed, surfacing it on the dashboard is the right behavior. The user can always move it back manually if they don't want it there.
- **Manual play sessions** (task 046) — when that lands, `recordPlaySession` will also be called from the manual-add endpoint. The auto-promote helper should be invoked there too, for consistency. Worth noting in 046's PR if 048 lands first; otherwise 046 should pull the helper into a shared spot.
- **Out of scope**: any UI affordance to opt-out of auto-promotion. If this becomes annoying we'll add a per-game toggle later.
