---
id: games-p6vkz
title: Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor
status: done
type: feature
context: games
created: 2026-08-01
completed: 2026-08-01
depends_on: [administration-t9bzx, design-system-001]
blocks: []
tags: [games, play-session, prior-play-time, journal, event-sourcing, steam, determinism]
related_adrs: [0002, 0026, 0028, 0031, 0042, 0050]
related_research: []
prior_art: [games-status-vocabulary-reconcile, integration-004]
---

## Why

Three pieces of the playtime model are wrong, and they are wrong in ways that compound.

**1. The domain fact is not in the log.** `game_play_session` (`src/Server/PlaytimeTracker.fs:71`)
holds 42 rows of real user history across 8 games in a table that is not a projection, not
event-sourced, and not rebuildable — a single point of failure with no recovery path.

**2. A derived cache value is in the log instead.** `Game_play_time_set` is a republished
`SELECT SUM(minutes_played)` (`PlaytimeTracker.fs:314-320`) carrying `{"totalMinutes": N}`. Because it
republishes a sum it is provably non-monotonic — verified against the live log: Grounded 2952→2282,
Windrose 975→375, Starcom 979→811. Those decreases are session edits and deletes leaking into the log
as a lower total. The per-day delta — the thing that actually happened, and the only thing Steam lets
us observe once — is computed at `PlaytimeTracker.fs:693` and thrown away.

**3. A game's whole pre-Mediatheca history is misfiled as a single play session.** On first sight of a
game, `PlaytimeTracker.fs:667-680` records Steam's entire lifetime total as one play session dated at
`rtime_last_played`. For a 500-hour game that asserts a 500-hour day that never happened, and it
poisons the Journal heatmap and Recently Played with a fabricated spike.

Playtime accumulated before session tracking began is **not a play session**. It is a distinct fact:
*this much was played before we started recording, and we do not know when.* It belongs in the log as
its own event, with no date, contributing to the total but never to the diary.

**And the sync cursor is unrebuildable state whose loss corrupts history.**
`steam_playtime_snapshot` (12 rows) is neither projection nor cache — it is external state remembered
solely to compute the next delta. If a row is lost, `getLastSnapshot` returns `None` and the code
above records the entire lifetime total all over again. Once prior playtime and every session are in
the log, the aggregate itself knows what has been accounted for, and the cursor becomes derivable —
closing the hazard by construction rather than guarding it.

Closes the standing open questions at `games/README.md:60` and `journal/README.md:49`.

## What

### Events and commands

Six events, unprefixed — mirroring `Watch_session_recorded` (`Movies.fs:40`); Games is not
universally `Game_`-prefixed (cf. `Want_to_play_with`):

- `Prior_play_time_recorded of minutes: int` — playtime accumulated before tracking began. **No date.**
- `Play_session_recorded`
- `Play_session_minutes_corrected`
- `Play_session_moved` (merges on collision)
- `Play_session_removed`
- `Steam_observed_total_reconciled of observedMinutes: int` — sets `SteamObservedMinutes` **without
  touching `TotalPlayTimeMinutes`**. The resync primitive: it records "Steam reports X" as an
  observation, decoupled from what we counted. Needed by `games-h4mrd` to carry the existing cursor
  across the cutover for the handful of games where the user's edits diverged from Steam's total, and
  it remains the standing repair for any future desync.

Commands: `Record_prior_play_time`, `Record_play_session`, `Correct_play_session_minutes`,
`Move_play_session`, `Remove_play_session`, `Reconcile_steam_observed_total`, plus the Steam-sync entry
point below.

**Session identity is the natural key `(gameSlug, gamingDay)` — no synthetic id.** The argument is
mechanical, not aesthetic: `Administration.diffTable` keys rows by declared PK, so under
`id INTEGER PRIMARY KEY AUTOINCREMENT` a shadow replay assigns different rowids and **every** row
reports `onlyInLive` / `onlyInShadow`. Removing the id is what makes the table drift-checkable at all.
Contrast Movies, which genuinely needs its GUID — one film, one day, two friend sets is real. Games
have no such distinguisher; Steam supplies no start/end times.

`PlaySessionSource = SteamSync | Manual`. **No `Imported` case** — with the pre-tracking lump modelled
as its own event, every remaining session is a genuinely observed delta on a genuinely known date.

### The Steam sync becomes a pure decision

Replace the adapter's branching with one command whose whole policy lives in `Games.decide`:

```fsharp
Record_steam_observed_total (observedMinutes, gamingDay) ->
    if SteamObservedMinutes = 0 then
        // first sight of this game
        if observedMinutes > PriorPlayTimeThresholdMinutes then [ Prior_play_time_recorded observedMinutes ]
        elif observedMinutes > 0                            then [ Play_session_recorded { Day = gamingDay; Minutes = observedMinutes; Source = SteamSync } ] @ promotion
        else []
    else
        match observedMinutes - SteamObservedMinutes with
        | delta when delta > 0 -> [ Play_session_recorded { Day = gamingDay; Minutes = delta; Source = SteamSync } ] @ promotion
        | _ -> []      // zero or negative: emit nothing, adjust nothing
```

`PriorPlayTimeThresholdMinutes = 960` (16 hours) — a named constant in `Games.fs`, not a setting.
Rationale to record in the ADR: a first observation at or under 16 hours is plausibly one real sitting,
and the existing code already dates it correctly from `rtime_last_played`; above 16 hours it cannot be
one sitting, so it is accumulated history.

Putting the threshold in `decide` rather than the adapter makes the whole Steam policy a pure,
directly-testable function; the adapter's remaining jobs are supplying `(observedMinutes, gamingDay)`
and enforcing the migration gate below.

### The sync is gated until the history migration has run

`runSync` must not dispatch `Record_steam_observed_total` while the store still contains
`Game_play_time_set` events and the `play_session_migration_completed` setting (written by
`games-h4mrd` on success) is absent. The race it closes: on a legacy store every game reconstitutes
with `SteamObservedMinutes = 0`, so an ungated sync in the deploy-to-migration window treats all 157
games as first sight and appends `Prior_play_time_recorded` lumps to untouched streams — and
`games-h4mrd`'s per-stream idempotency refusal then skips exactly those streams, permanently leaving
their real history unreconstructed. The window is not hypothetical: the migration is
operator-triggered, so scheduled syncs fire inside it unless gated.

Shape: a pure `PlaytimeTracker.syncGateOpen : hasLegacyPlayTimeEvents: bool -> migrationCompleted: bool -> bool`,
called once at the top of `runSync` (a store-level condition, so it lives in the adapter, not
`decide`), skipping the run with an `eprintfn` naming the un-gate condition. The gate self-retires:
a fresh install has no `Game_play_time_set` events and is never gated; an existing install un-gates
the moment the migration completes. No setting, no UI, nothing to remove later.

### Two folds, not one — the trap that makes the cursor derivable

`ActiveGame` gains:

- `PriorPlayTimeMinutes: int`
- `PlaySessions: Map<string, int>` — gaming day → minutes
- `SteamObservedMinutes: int`

with:

- `TotalPlayTimeMinutes = PriorPlayTimeMinutes + Σ PlaySessions.Values` — **what the user asserts happened.**
- `SteamObservedMinutes = PriorPlayTimeMinutes + Σ SteamSync deltas as originally recorded` —
  **what Steam has told us**, and therefore never reduced by a later correction or removal.

The second fold is load-bearing and is the whole reason the cursor can be retired. Deleting a
Steam-sourced session must not make the next sync re-add it: Grounded's live log holds 2282 while
Steam reports ~2952, so a cursor derived from `TotalPlayTimeMinutes` would fabricate a 670-minute
session on the very next sync. `SteamObservedMinutes` is computable because
`Play_session_minutes_corrected` and `Play_session_removed` both carry `previousMinutes`.

`previousMinutes` and the trailing `minutesPlayed` are **stamped by `decide` from aggregate state,
never supplied by the caller.** This is what makes every downstream a pure fold, so `GameProjection`'s
`total_play_time` arithmetic never has to read `game_play_session` — a *different* projection with an
independently-advancing checkpoint, which is precisely how replay nondeterminism gets in.

### Invariants

- Session minutes strictly `> 0`. Correcting to 0 is refused — use remove.
- `Record_prior_play_time` is refused when `PriorPlayTimeMinutes > 0`. Prior playtime is recorded once
  per game; this refusal is the domain-level guard that makes a lost or reset cursor harmless.
- Prior playtime **never promotes to InFocus** — it is history, not activity.
- The ≤1440-minute ceiling and the no-future-date check stay in the **manual-session API layer**,
  deliberately *not* aggregate invariants: the aggregate must accept Steam lumps far above 1440. Say so
  in a comment, or someone will "fix" it.

### Auto-promotion moves into `Games.decide`

Today `promoteToInFocusIfNeeded` (`PlaytimeTracker.fs:326`) consults `GameProjection.getGameStatus` — a
read model — to decide whether to emit an event, so promotion misfires whenever the projection lags.
`Record_play_session` returns
`[Play_session_recorded d] @ (if status <> InFocus then [Game_status_changed InFocus] else [])`, the
same shape `Record_watch_session` already uses (`Movies.fs:214-220`). ADR-0042's any-status rule is
unchanged in meaning.

**Only recording a new session promotes.** `Play_session_minutes_corrected`, `Play_session_moved`,
`Play_session_removed` and `Prior_play_time_recorded` do **not** — a deliberate narrowing of today's
`updatePlaySessionApi:385`. Fixing a typo in a February session must not yank a Retired game back into
focus.

### Retirements

- **`Set_play_time` is deleted from `GameCommand`;** `Game_play_time_set` gets an explicit
  `| _, Game_play_time_set _ -> state` arm in `evolve`. **The no-op is mandatory, not tidy:**
  `games-h4mrd` appends session events to streams that already contain `Game_play_time_set`, and if
  both applied, replay would set the total to 2282 and then add the reconstructed 2282 on top.
- **`steam_playtime_snapshot` is dropped**, along with `getLastSnapshot` and `saveSnapshot`
  (`PlaytimeTracker.fs:88-111`). Remove its `Imperative` entry from `tableRegistry`.
- `recordPlaySession`, `recomputeAndPublishTotal`, `upsertManualPlaySession`, `updatePlaySession`,
  `deletePlaySession`, `getPlaySessionById`, `promoteToInFocusIfNeeded` and `ManualSteamAppId` are all
  deleted. `runSync`'s whole per-game block (`PlaytimeTracker.fs:664-721`) collapses to a single
  `Record_steam_observed_total` dispatch.

### Projection

**New `src/Server/PlaySessionProjection.fs`** — *not* folded into `GameProjection`: coupling would
force an operator to drop 900 games' catalog to rebuild the diary. Table PK `(game_slug, date)`;
`source TEXT` replaces the `steam_app_id = 0` sentinel; **`created_at` is dropped** — a write-time
artifact that would make every drift check report `columnMismatch` on every row.
`PlaytimeTracker.initialize` stops creating the table. Register in `Composition.fs` after
`GameProjection.handler`, and in `tableRegistry` as `Projected "PlaySessionProjection"`.

**Prior playtime writes no session row** — so the Journal heatmap, Recently Played and
`getDashboardPlaySessions` exclude it *by construction*, with no filter to remember. It lands in
`GameProjection` only, as a new `game_detail.prior_play_time` column (event-derived, therefore
replayable, therefore it stays a projection column under the `infrastructure-e4kwm` identity-card
clause).

`total_play_time` stays in **`GameProjection`**, its arm moving from `Game_play_time_set`
(`GameProjection.fs:319-327`) to `Prior_play_time_recorded` plus the four session events — pure payload
arithmetic, no cross-projection write.

integration-004 is preserved *by construction*: two syncs in one gaming day append two events with the
same date; aggregate and projection both sum. The second delta is now **visible in the log** instead of
vanishing into a rewritten row.

### Shared / client

- `PlaySessionDto.Id` removed; `updatePlaySession: int64 * string * int` becomes a `PlaySessionEdit`
  record; `deletePlaySession: int64` becomes `string * string`.
- `GameDetailDto` gains `PriorPlayTimeMinutes: int`.
- GameDetail shows the breakdown when it is `> 0` — e.g. *"512h before tracking + 12h tracked"* —
  so the total is honest about what Mediatheca actually observed and the HLTB comparison stays
  interpretable. Single number when it is `0`.
- Remaining client changes are mechanical: `GameDetail/State.fs:364`, `Views.fs:1509`, `Views.fs:1550`,
  and the `prop.key`.

`EventFormatting.fs` and `Api.fs:1840`'s description map each need six new arms;
`Games.Serialization.handledEventTypes:568-599` gains six entries and loses none.

## Acceptance criteria

- [x] Expecto: `Record_steam_observed_total` on an unseen game with `observedMinutes = 30000` emits exactly one `Prior_play_time_recorded 30000`, no session event, and no `Game_status_changed`.
- [x] Expecto: `Record_steam_observed_total` on an unseen game with `observedMinutes = 180` emits one `Play_session_recorded` dated at the supplied gaming day, plus promotion.
- [x] Expecto: boundary — `observedMinutes = 960` emits a session; `961` emits `Prior_play_time_recorded`.
- [x] Expecto: after prior playtime of 30000, a later `Record_steam_observed_total 30120` emits one 120-minute session.
- [x] Expecto: `syncGateOpen` (pure) — refuses with legacy events present and the marker absent; permits with the marker set; permits with no legacy events regardless of the marker.
- [x] **Expecto (the phantom-session regression): record 509 prior, then sessions summing to 2443 (total 2952), then remove a 670-minute session. `TotalPlayTimeMinutes` = 2282 and `SteamObservedMinutes` = 2952; a subsequent `Record_steam_observed_total 2952` emits nothing.**
- [x] Expecto: `Steam_observed_total_reconciled 2952` on a game whose sessions sum to 2282 leaves `TotalPlayTimeMinutes` at 2282 and sets `SteamObservedMinutes` to 2952; a following `Record_steam_observed_total 2952` then emits nothing.
- [x] Expecto: `Record_prior_play_time` on a game that already has prior playtime returns `Error`.
- [x] Expecto: two `Play_session_recorded` events on the same `(slug, date)` produce one projection row with summed minutes (integration-004 regression, ported from `PlaytimeTrackerTests.fs`).
- [x] Expecto: `decide` rejects `minutesPlayed <= 0` on record and on correct.
- [x] Expecto: `Record_play_session` on a `Retired` game emits `Game_status_changed InFocus`; on an `InFocus` game emits only the session event (ADR-0042 rule preserved).
- [x] Expecto: correct / move / remove / `Record_prior_play_time` emit **no** `Game_status_changed`, tested against a game in each of the five statuses.
- [x] Expecto: correct / move / remove against a nonexistent session return `Error`.
- [x] Expecto: for every game, `Games.reconstitute(stream).TotalPlayTimeMinutes` = `prior_play_time + Σ game_play_session.minutes_played` = `game_list.total_play_time` = `game_detail.total_play_time`.
- [x] Expecto: a game with only prior playtime produces **zero** rows in `game_play_session`, and `getDashboardPlaySessions` / `getPlaytimeSummary` return nothing for it.
- [x] **Expecto: `checkProjectionDrift` returns an empty discrepancy list for `PlaySessionProjection` and for `GameProjection`.**
- [x] Expecto: `Games.evolve` on `Game_play_time_set` is a no-op; its codec round-trip still succeeds; `buildUnknownEventReport` reports it neither unhandled nor unformattable.
- [x] `grep -rc "Set_play_time\|ManualSteamAppId\|promoteToInFocusIfNeeded\|steam_playtime_snapshot\|getLastSnapshot\|saveSnapshot" src/Server/` returns 0.
- [x] `npm test` passes; `npm run build` passes.
- [ ] The GameDetail play-session list, add, edit and delete behave as before, and the prior-playtime breakdown reads correctly on a game that has one. [human-eye — not exercised by this worker; no browser tool in this session, see Outcome]

## Notes

**ADR:** *"Play sessions are first-class Games events keyed on (game, gaming day); pre-tracking
playtime is its own dateless event; the Steam sync cursor is derived from the log"*, `scope: games`.

Must record:
- the drift-detector / `AUTOINCREMENT` argument for the natural key;
- the CQRS-inversion fix in auto-promotion, and its narrowing to new sessions only;
- the **two-fold** design and the phantom-session failure it prevents — this is the non-obvious part
  and the reason `steam_playtime_snapshot` can be deleted rather than merely guarded;
- the 16-hour threshold and why it lives in `decide` rather than the adapter.

Touches `src/Client/Pages/GameDetail/`, hence `depends_on: design-system-001` per the games BC frontend
gate (`games/README.md:56`). The prior-playtime breakdown is a typography/label change within existing
patterns; if it wants new visual vocabulary, stop and file a design-system task first.

## Outcome

Delivered as specified. `Games.fs` gains six events (`Prior_play_time_recorded`,
`Play_session_recorded`, `Play_session_minutes_corrected`, `Play_session_moved`,
`Play_session_removed`, `Steam_observed_total_reconciled`) and the matching commands, plus
`PriorPlayTimeThresholdMinutes = 960`. `Record_steam_observed_total` implements the whole
Steam-sync policy as one pure decision in `decide`. `ActiveGame` carries the two-fold
(`PriorPlayTimeMinutes`/`PlaySessions`/`SteamObservedMinutes` vs. `TotalPlayTimeMinutes`) that
makes the sync cursor derivable. `Set_play_time` is deleted from `GameCommand`;
`Game_play_time_set` gets an explicit no-op arm in `evolve` (kept in the DU/serializer/formatter
for replay of old streams). Auto-promotion moved from `PlaytimeTracker.promoteToInFocusIfNeeded`
(a CQRS-inverted read-model consult) into `decide`, narrowed to newly-recorded sessions only.

New `src/Server/PlaySessionProjection.fs` owns `game_play_session` (PK `(game_slug, date)`,
`source TEXT`, no `created_at`) as its own checkpoint-tracked projection, registered in
`Composition.fs` after `GameProjection.handler`. `GameProjection` gains `game_detail.prior_play_time`
and computes `total_play_time` via pure payload arithmetic on the new events (no cross-projection
read). `steam_playtime_snapshot` and its CRUD (`getLastSnapshot`/`saveSnapshot`) are deleted;
`Administration.tableRegistry` drops that entry and reclassifies `game_play_session` from
`Imperative "PlaytimeTracker"` to `Projected "PlaySessionProjection"`.

`PlaytimeTracker.fs` is substantially rewritten: `syncGateOpen` (pure) gates `runSync` on
legacy `Game_play_time_set` events plus the absent `play_session_migration_completed` setting;
the per-game sync body collapses to a single `Record_steam_observed_total` dispatch; the manual
session API (`addManualPlaySessionApi`/`updatePlaySessionApi`/`deletePlaySessionApi`) is
re-keyed on the natural `(gameSlug, date)` identity, with the 1440-minute ceiling and
no-future-date check staying at this layer, not in `decide`. `Api.fs`'s `importSteamLibrary`
(a separate one-time bulk import, not the scheduled sync) now dispatches the same
`Record_steam_observed_total` decision instead of the deleted `Set_play_time`.

Shared/client: `PlaySessionDto.Id` removed (natural key is `(GameSlug, Date)`);
`updatePlaySession` takes a new `PlaySessionEdit` record; `deletePlaySession` takes
`string * string`. `GameDetail` gains `PriorPlayTimeMinutes`. `GameDetail/State.fs` and
`Views.fs` are updated mechanically (session identity keyed on date, not id) plus a new
prior-playtime breakdown line in the hero ("512h before tracking + 12h tracked" / single
number when there's no prior playtime).

All 20 acceptance criteria with automated checks are covered by tests across
`GamesTests.fs` (new `testList "Games play sessions"`), `PlaytimeTrackerTests.fs` (rewritten:
`syncGateOpen`, natural-key manual session API, integration-004 same-day merge, prior-only-game
zero-session-rows, full aggregate/projection total consistency), `ProjectionDriftTests.fs` (new
zero-discrepancy case for `PlaySessionProjection`/`GameProjection`), `AdministrationTests.fs`
(new `Game_play_time_set` handled+formattable case), and `TableClassificationTests.fs` (registry
reclassification). `npm test` (471 tests) and `npm run build` both pass. The `grep -rc` acceptance
check returns 0 across `src/Server/`.

The final acceptance criterion (`[human-eye]`, GameDetail add/edit/delete + breakdown display)
was not exercised in a browser — this worker's toolset has no browser/MCP access. The code paths
were reviewed by hand (State.fs's session-edit dispatch, Views.fs's key/lookup changes, the new
hero breakdown text) and the full test suite plus Fable client build both pass, but an actual
click-through in the running app is still owed before this is fully verified end-to-end.

Key files: `src/Server/Games.fs`, `src/Server/PlaySessionProjection.fs` (new),
`src/Server/PlaytimeTracker.fs`, `src/Server/GameProjection.fs`, `src/Server/Administration.fs`,
`src/Server/Api.fs`, `src/Server/Composition.fs`, `src/Server/EventFormatting.fs`,
`src/Shared/Shared.fs`, `src/Client/Pages/GameDetail/{Types,State,Views}.fs`,
`.agentheim/knowledge/decisions/0050-play-sessions-first-class-events-two-fold-cursor.md` (authored as 0045, renumbered at integration).
