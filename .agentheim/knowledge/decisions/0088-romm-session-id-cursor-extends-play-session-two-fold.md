---
id: 0088
title: RomM-sourced play sessions are deduplicated by a per-game set of imported RomM session ids, folded from Play_session_recorded itself — a second cursor shape alongside ADR-0050's SteamObservedMinutes, both event-log-derived and both "never reduced" by later edits
scope: games
status: accepted
date: 2026-09-25
supersedes: []
superseded_by: []
amends: [0050]
related_tasks: [games-rmxg2, integration-jkbm1]
related_research: [romm-vs-mediatheca-2026-09-23]
---

# ADR 0088: RomM's session-id cursor extends ADR-0050's two-fold design to a third source

## Context

`integration-jkbm1` (the RomM adapter) needs to import RomM's own play sessions as
Games play sessions, alongside the existing `SteamSync` and `Manual` sources
(ADR-0050). Re-running the sync must be idempotent: no new events when RomM has
reported no new sessions since the last run.

ADR-0050 solved this problem once already, for Steam — but Steam's shape is
fundamentally different from RomM's. Steam's `GetRecentlyPlayedGames` reports one
**cumulative lifetime total** per game, with no session boundaries; the cursor
(`ActiveGame.SteamObservedMinutes`) exists purely to let the aggregate compute
`delta = observedTotal - lastObservedTotal` without external imperative state.

RomM's `GET /api/play-sessions` reports the opposite shape: discrete sessions,
each with its own stable `id`, `start_time`, `end_time`, and `duration_ms`. There
is no cumulative total to diff against, and inventing one by summing RomM's
sessions and diffing against a remembered sum would be strictly worse than what
RomM already gives us for free — a rock-solid identity per session.

## Decision

**The RomM cursor is "have we already imported this session id", not "what is
the delta since last total".** Concretely:

- `Games.PlaySessionRecordedData` (the `Play_session_recorded` payload) gains
  `RommSessionIds: Set<string>` — empty for `SteamSync`/`Manual` records,
  populated with the RomM session ids that contributed to a `RomM`-sourced
  record. Old serialized events (no such field) decode it as `Set.empty`.
- `ActiveGame` gains `ImportedRommSessionIds: Set<string>`, folded by `evolve`'s
  `Play_session_recorded` arm — union, same as `SteamObservedMinutes`'s
  accumulation, and for the same reason: **never reduced** by a later
  `Play_session_minutes_corrected`/`Play_session_moved`/`Play_session_removed`.
- `Record_romm_play_session (day, sessions: (rommSessionId * minutes) list)` —
  the adapter has already grouped RomM sessions onto one gaming day (by each
  session's own `start_time`, through the same `PlaytimeTracker.toGamingDay`
  function Steam/manual sessions use, so a session is never split across two
  days). `decide` filters `sessions` to the ones whose id is NOT already in
  `game.ImportedRommSessionIds`; if none are new, or their summed minutes round
  to zero, the whole call is `Ok []`. Otherwise it emits ONE
  `Play_session_recorded` carrying only the new ids' summed minutes and only
  those ids.

This is deliberately shaped like ADR-0050's own cursor, not a new pattern:
**still derived purely by folding the event log** (no external imperative
state, no synthetic cursor table), and **still asymmetric between recording and
editing** — a session id, once it has appeared in an emitted
`Play_session_recorded`, stays "known" forever, exactly as `SteamObservedMinutes`
stays at its high-water mark regardless of what the user later does to the
resulting day. The mechanism differs (a `Set<string>` of ids vs. an `int`
running total) because the two sources hand Mediatheca genuinely different
facts — a total, or an identity — and the cursor shape should match the fact
the source actually provides, not force one shape onto both.

### Why not reuse `SteamObservedMinutes`-style total-diffing for RomM

Rejected. RomM already tells us which sessions are which; discarding that and
re-deriving a diffable total would throw away real information RomM provides
for free, and would reintroduce exactly the class of bug ADR-0050 closed for
Steam (a corrected/removed day silently re-appearing) via a needless extra
layer of indirection. Using the id directly is strictly simpler and strictly
more precise.

### Why extend the payload rather than add a fourth event type

`Play_session_recorded` already carries a `Source` field; a `RomM` source
whose extra data (the contributing session ids) rides the SAME event as an
optional, source-scoped field keeps `PlaySessionProjection` and every existing
reader of `Play_session_recorded` unchanged in shape — only `ActiveGame`'s
`evolve` needs to know the field exists. A parallel event type
(`Romm_session_imported`, say) would duplicate `Play_session_recorded`'s
day/minutes bookkeeping and force `PlaySessionProjection` to merge two event
shapes into the same table row.

## Consequences

- `PlaySessionProjection.fs`'s `encodeSource`/`getSource`/`toPlaySessionDto`
  must become an exhaustive 3-way match. They currently collapse anything that
  isn't the literal string `"Manual"` to `SteamSync` — a latent bug this ADR's
  own third source would otherwise silently trip (every RomM-sourced diary row
  would read as Steam).
- `GameProjection` gains `findByRommRomId`, mirroring `findBySteamAppId`, backing
  a new `Set_romm_rom_id`/`Game_romm_rom_id_set` command/event pair — the
  identity join key `integration-jkbm1`'s matching step needs, modeled exactly
  like `Set_steam_app_id`/`Game_steam_app_id_set`.
- The F# compiler's exhaustiveness check on `Shared.PlaySessionSource` is the
  safety net that every existing two-way match on the type (client-side session
  badges included, since Fable compiles `Shared.fs` too) must be updated to
  handle `RomM` before the change compiles — no runtime discovery of a missed
  call site.
- This work is split into its own Games-BC task (`games-rmxg2`), which
  `integration-jkbm1` (the RomM HTTP adapter itself) depends on — the aggregate
  change is self-contained, unit-testable with zero HTTP, and independently
  verifiable before the adapter that calls it exists.
