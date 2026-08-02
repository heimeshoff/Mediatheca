---
id: 0050
title: Play sessions are first-class Games events keyed on (game, gaming day); pre-tracking playtime is its own dateless event; the Steam sync cursor is derived from the log
scope: games
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-p6vkz, games-h4mrd, integration-004, games-status-vocabulary-reconcile]
related_research: []
---

# ADR 0050: Play sessions are first-class Games events keyed on (game, gaming day); pre-tracking playtime is its own dateless event; the Steam sync cursor is derived from the log

> Note on ADR numbering: authored as 0045 in a parallel worker (batch 2, 2026-08-01); renumbered to 0050 at its conflict-delayed integration because ADRs 0045-0049 landed first.

## Context

Three pieces of the playtime model were wrong, and wrong in ways that
compound (see the games-p6vkz task's `## Why` for the full argument):

1. `game_play_session` was a hand-written table — not a projection, not
   event-sourced, not rebuildable. A single point of failure with no recovery
   path, holding real user history for 8 games.
2. `Game_play_time_set` republished `SELECT SUM(minutes_played)` into the
   event log — a derived cache value living where a fact should. Verified
   non-monotonic against the live log: session edits and deletes leaked into
   the log as a *lower* total on replay (Grounded 2952 -> 2282 is the
   regression this ADR's two-fold design exists to prevent).
3. A game's entire pre-Mediatheca history was misfiled as one play session
   dated at Steam's `rtime_last_played`. A 500-hour game asserted a 500-hour
   day that never happened, poisoning the Journal heatmap and Recently
   Played with a fabricated spike.

And underneath all three: `steam_playtime_snapshot` was neither a projection
nor a cache — it was external state (Steam's last-known total) remembered
solely to compute the next delta. Lose the row and the code re-records the
game's entire lifetime total as a brand-new session.

## Decision

### Six new events, unprefixed

Mirroring `Watch_session_recorded` (Movies is not universally `Game_`-
prefixed — cf. `Want_to_play_with`):

- `Prior_play_time_recorded of minutes: int` — playtime accumulated before
  tracking began. No date. Never promotes, never contributes to the diary,
  only to the total.
- `Play_session_recorded` — a `(day, minutes, source)` payload, `source`
  being `SteamSync | Manual`.
- `Play_session_minutes_corrected`, `Play_session_moved` (merges on
  collision), `Play_session_removed` — the three edit primitives.
- `Steam_observed_total_reconciled of observedMinutes: int` — sets
  `SteamObservedMinutes` without touching `TotalPlayTimeMinutes`: the standing
  repair for a desync, and the mechanism `games-h4mrd` uses to carry the
  existing cursor across the cutover for games where the user's edits
  diverged from Steam's own total.

`Game_play_time_set` is **not deleted from the DU** — it stays, forever, as a
legacy event `Games.evolve` now handles with an explicit no-op arm (`Active _,
Game_play_time_set _ -> state`), never a wildcard catch-all. The no-op is
mandatory, not tidy: `games-h4mrd` appends reconstructed session events onto
streams that already contain this event, and if both applied on replay, the
total would be set from the stale SUM and then have the reconstructed total
added on top of it.

### Session identity is the natural key `(gameSlug, gamingDay)` — no synthetic id

The argument is mechanical, not aesthetic. `Administration.diffTable` (the
shadow-replay drift detector, ADR-0031) keys rows by declared PK. Under an
`id INTEGER PRIMARY KEY AUTOINCREMENT` scheme, a shadow replay assigns
different rowids than the live table's insertion order produced, so *every*
row reports as `onlyInLive`/`onlyInShadow` — the table becomes undrift-
checkable by construction. Removing the id is what makes it checkable at all.
This is the opposite of Movies, which genuinely needs its GUID: one film, one
day, two different friend sets watching it together is a real, distinguishing
fact. Games have no such distinguisher — Steam supplies no start/end times,
only a lifetime total — so the day itself is the only fact worth keying on.

### The Steam sync becomes one pure decision

```fsharp
Record_steam_observed_total (observedMinutes, gamingDay) ->
    if SteamObservedMinutes = 0 then
        if observedMinutes > PriorPlayTimeThresholdMinutes then [ Prior_play_time_recorded observedMinutes ]
        elif observedMinutes > 0                            then [ Play_session_recorded { Day = gamingDay; Minutes = observedMinutes; Source = SteamSync } ] @ promotion
        else []
    else
        match observedMinutes - SteamObservedMinutes with
        | delta when delta > 0 -> [ Play_session_recorded { Day = gamingDay; Minutes = delta; Source = SteamSync } ] @ promotion
        | _ -> []
```

`PriorPlayTimeThresholdMinutes = 960` (16 hours), a named `[<Literal>]` in
`Games.fs`, not a runtime setting. Rationale: a first observation at or under
16 hours is plausibly one real sitting, and the existing code already dated
it correctly from `rtime_last_played`; above 16 hours it cannot be one
sitting, so it is accumulated history instead. Putting the threshold — and
the whole policy — in `decide` rather than the Steam adapter makes it a
pure, directly-testable function; the adapter (`PlaytimeTracker.runSync`)'s
only remaining jobs are supplying `(observedMinutes, gamingDay)` and
enforcing the migration gate below.

### The sync is gated until the history migration has run

`PlaytimeTracker.syncGateOpen : hasLegacyPlayTimeEvents: bool ->
migrationCompleted: bool -> bool` is a pure predicate, checked once at the
top of `runSync`. The race it closes: on a legacy store, every game
reconstitutes with `SteamObservedMinutes = 0` (since the legacy event is now
a no-op), so an ungated sync in the deploy-to-migration window would treat
every game as first sight and append `Prior_play_time_recorded` lumps to
streams the migration hasn't reached yet — and the migration's own per-stream
idempotency refusal then permanently skips exactly those streams. The gate
self-retires: a fresh install has no legacy events and is never gated; an
existing install un-gates the moment the migration completes. No setting to
remove later, no UI — the condition is derived, not stored.

### The two-fold design — the load-bearing, non-obvious part

`ActiveGame` gains `PriorPlayTimeMinutes: int`, `PlaySessions: Map<string,
int>` (gaming day -> minutes), and `SteamObservedMinutes: int`, with:

- `TotalPlayTimeMinutes = PriorPlayTimeMinutes + Σ PlaySessions.Values` —
  what the user asserts happened.
- `SteamObservedMinutes = PriorPlayTimeMinutes + Σ SteamSync deltas as
  originally recorded` — what Steam has told us, **never reduced** by a
  later correction, move, or removal.

This second fold is the whole reason `steam_playtime_snapshot` can be
*deleted* rather than merely guarded. Consider: 509 minutes prior, then
Steam-sourced sessions summing to 2443 (total 2952, matching what Steam has
always reported), then the user deletes one 670-minute session because it
was a friend's controller left running. `TotalPlayTimeMinutes` correctly
drops to 2282 — but `SteamObservedMinutes` stays at 2952, because the removal
never touches it. The next sync reports 2952 again; `delta = 2952 - 2952 =
0`; nothing is emitted. A cursor derived from `TotalPlayTimeMinutes` instead
would compute `delta = 2952 - 2282 = 670` and silently fabricate the deleted
session right back — the phantom-session bug this design exists to prevent.

### Auto-promotion moves into `Games.decide`, narrowed to new sessions only

`PlaytimeTracker.promoteToInFocusIfNeeded` used to consult
`GameProjection.getGameStatus` — a read model — to decide whether to emit a
promotion event, so it misfired whenever the projection lagged behind the
event log (a CQRS inversion: a write decision reading a read model).
`Record_play_session`/`Record_steam_observed_total`'s session-recording
branches now return `[Play_session_recorded d] @ (if status <> InFocus then
[Game_status_changed InFocus] else [])`, the same shape
`Movies.Record_watch_session` already uses. ADR-0042's any-status rule is
unchanged in meaning.

Deliberately narrowed beyond the old behavior: **only recording a new
session promotes.** `Correct_play_session_minutes`, `Move_play_session`,
`Remove_play_session`, and `Record_prior_play_time` never do. Fixing a typo
in a February session, or recording that 512 hours happened before tracking
began, must not yank a `Retired` game back into focus.

### Invariants, and where they don't live

- Session minutes strictly `> 0` (in `decide`, on record and correct;
  correcting to 0 is refused — use remove).
- `Record_prior_play_time` is refused once `PriorPlayTimeMinutes > 0` — the
  domain-level guard that makes a lost or reset cursor harmless.
- The 1440-minute ceiling and the no-future-date check stay in the
  **manual-session API layer** (`PlaytimeTracker.fs`), deliberately *not*
  aggregate invariants: the aggregate must accept Steam lumps far above 1440
  minutes without complaint.

### Projection split

`PlaySessionProjection.fs` is new, and deliberately *not* folded into
`GameProjection`: coupling the diary to the catalog would force an operator
to drop 900 games' catalog just to rebuild the play-session diary. Its table
keeps the name `game_play_session` (so nothing downstream needs a rename)
but the schema changes: PK is `(game_slug, date)`, `source TEXT` replaces the
old `steam_app_id = 0` sentinel, and `created_at` is dropped — a write-time
artifact that would make every drift check report a column mismatch on every
row. `total_play_time` stays in `GameProjection`, computed as pure payload
arithmetic on every relevant event (never by re-reading
`PlaySessionProjection`'s table) — the same "no cross-projection read or
write" discipline ADR-0031's drift detector depends on.

Prior playtime writes no session row at all, so the Journal heatmap,
Recently Played, and the dashboard/summary queries exclude it *by
construction*, with no filter anyone has to remember to add.

## Consequences

- The Steam sync cursor table is deleted outright, closing a "lost row ->
  re-recorded lifetime total" hazard by construction rather than continuing
  to guard it (`Administration.tableRegistry`'s registry-coverage test
  enforces the table no longer has an entry).
- `game_play_session` moves from `Imperative "PlaytimeTracker"` to `Projected
  "PlaySessionProjection"` in the table registry — checkpoint-tracked,
  rebuildable, and now covered by `checkProjectionDrift`.
- `games-h4mrd` (the history migration, blocked on this task) is responsible
  for reconstructing existing rows into `Prior_play_time_recorded`/session
  events on the legacy streams this ADR's gate protects.
- Client API surface changes: `PlaySessionDto` loses its synthetic `Id`;
  editing/deleting a session is now keyed on `(gameSlug, date)`
  (`PlaySessionEdit` for edits, `string * string` for deletes).
