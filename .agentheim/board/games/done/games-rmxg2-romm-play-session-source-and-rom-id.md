---
id: games-rmxg2
title: Games — RomM as a third play-session source, with a RomM rom id as external identity and a session-id cursor; fixes PlaySessionProjection mapping every non-Manual source to SteamSync
status: done
type: feature
context: games
created: 2026-09-25
completed:
depends_on: []
blocks: [integration-jkbm1]
tags: [games, romm, playtime, play-sessions, external-id]
related_adrs: [0088, 0050]
related_research: []
prior_art: []
---

## Why
`integration-jkbm1` (the RomM adapter) needs the Games aggregate to:

- (a) recognize `RomM` as a play-session source alongside `SteamSync` and `Manual` (ADR-0050),
- (b) hold a RomM rom id as an external identity, mirroring `Set_steam_app_id`,
- (c) record RomM's individually identified sessions idempotently.

RomM already hands us a stable id per session. The cursor should therefore ask "have we seen
this id?", not diff a re-derived cumulative total the way Steam's `SteamObservedMinutes` does.
ADR-0088 explains why the two shapes genuinely differ.

Separately, `PlaySessionProjection.fs`'s `encodeSource`/`getSource`/`toPlaySessionDto` map
any source string that isn't `"Manual"` to `SteamSync`. That is a latent bug: a third source
would immediately be mislabelled, so every RomM-sourced diary row would show as Steam.

## What
- `Shared.PlaySessionSource` gains `RomM` (now `SteamSync | Manual | RomM`).
- `Games.PlaySessionRecordedData` gains `RommSessionIds: Set<string>`. It is empty for
  `SteamSync`/`Manual` and holds the contributing RomM session ids for `RomM` records. Old
  serialized events without the field decode it as empty.
- `ActiveGame` gains `RommRomId: int option` and `ImportedRommSessionIds: Set<string>`.
- A new pair mirroring `Set_steam_app_id`/`Game_steam_app_id_set`:
  `Set_romm_rom_id of rommRomId: int` / `Game_romm_rom_id_set of rommRomId: int`.
  `GameProjection` gains `findByRommRomId`.
- A new command `Record_romm_play_session of day: string * sessions: (string * int) list`
  (RomM session id, minutes). The caller has already grouped the sessions onto one gaming
  day. It reuses `Play_session_recorded` with `Source = RomM`. `decide` keeps only ids not
  already in `ImportedRommSessionIds`. If none are new, or the new minutes sum to 0, it
  returns `Ok []`.
- `evolve`'s `Play_session_recorded` arm unions a `RomM` record's `RommSessionIds` into
  `ActiveGame.ImportedRommSessionIds`. `Play_session_minutes_corrected`, `Play_session_moved`
  and `Play_session_removed` never touch that set, mirroring `SteamObservedMinutes`'s "never
  reduced" rule.
- `PlaySessionProjection.fs`'s `encodeSource`/`getSource`/`toPlaySessionDto` become an
  exhaustive three-way match.
- `Games.Serialization` encode/decode and `handledEventTypes` cover `Game_romm_rom_id_set` and
  the extended `Play_session_recorded` payload.

## Acceptance criteria
- [ ] `Games.decide (Record_romm_play_session (day, [(id1,m1); (id2,m2)]))` on a game with an
      empty `ImportedRommSessionIds` returns
      `Ok [Play_session_recorded {Day=day; Minutes=m1+m2; Source=RomM; RommSessionIds={id1;id2}}] @ promotionEvents`.
- [ ] After that result is folded through `evolve`, the same call returns `Ok []`. This core
      idempotency property is unit-tested directly against `Games.decide`/`evolve`.
- [ ] A call with one known id and one new id sums only the new id's minutes into a single
      `Play_session_recorded`, and `RommSessionIds` carries only the new id.
- [ ] `Play_session_removed`, `Play_session_moved` and `Play_session_minutes_corrected` on a
      RomM-sourced day never remove entries from `ImportedRommSessionIds`. A later
      `Record_romm_play_session` with the same, since edited-away, id still returns `Ok []`.
      A unit test proves the phantom re-add is prevented, mirroring ADR-0050's Grounded
      example.
- [ ] A RomM session on a game that isn't InFocus promotes it (via the existing
      `promotionEvents`), with a unit test.
- [ ] `Set_romm_rom_id` is a no-op when the game already carries the same rom id, mirroring
      `Set_steam_app_id`. `GameProjection.findByRommRomId` returns the matching slug after
      `Game_romm_rom_id_set`.
- [ ] A `RomM`-sourced `Play_session_recorded` round-trips through `PlaySessionProjection`:
      `game_play_session.source = 'RomM'`, and `getForGame`/`getBySlugAndDay` return
      `Source = RomM`. This regression test fails against the current mapping, where any
      non-`Manual` source becomes `SteamSync`.
- [ ] A legacy `Play_session_recorded` JSON payload with no `rommSessionIds` field still
      decodes, with `RommSessionIds = Set.empty` (fixture regression test).
- [ ] `Games.Serialization.handledEventTypes` includes `Game_romm_rom_id_set`, and a
      serialize/deserialize round-trip test covers it.
- [ ] `npm run build`, `npm test` and `npm run test:client` all pass. The build confirms the
      new `PlaySessionSource` case leaves no non-exhaustive match anywhere, client included.

## Notes
- No HTTP and no `RomM.fs` code in this task. It is a pure aggregate and projection change,
  fully testable without touching anything `integration-jkbm1` owns.
- ADR-0088 describes the session-id cursor and why its shape differs from
  `SteamObservedMinutes`, while still following ADR-0050: derived from the event log, with no
  external imperative state.
- If the client renders a per-source label or icon for play sessions (e.g. in the Journal or
  on the game detail page), the new case needs a label there. The compiler flags every such
  match.

## Outcome

Implemented ADR-0088's second (session-id) cursor as a Games-BC aggregate/projection-only
change, with no HTTP and no `RomM.fs`, exactly as scoped:

- `Shared.PlaySessionSource` gains `RomM` (`SteamSync | Manual | RomM`).
- `Games.PlaySessionRecordedData` gains `RommSessionIds: Set<string>` — empty for
  `SteamSync`/`Manual`, populated for `RomM`; a legacy payload with no `rommSessionIds`
  field decodes as `Set.empty` (`Games.Serialization`'s `Play_session_recorded` decoder).
- `Games.ActiveGame` gains `RommRomId: int option` and `ImportedRommSessionIds: Set<string>`.
- New pair `Set_romm_rom_id`/`Game_romm_rom_id_set`, mirroring `Set_steam_app_id`/
  `Game_steam_app_id_set` exactly (no-op on a redundant set). `GameProjection.findByRommRomId`
  mirrors `findBySteamAppId`, backed by a new `game_detail.romm_rom_id` column (added via the
  same `ALTER TABLE ... try/with` migration idiom every other column addition in
  `GameProjection.createTables` uses).
- New command `Record_romm_play_session (day, sessions: (rommSessionId * minutes) list)`:
  `decide` keeps only ids not already in `ImportedRommSessionIds`, emits one
  `Play_session_recorded { Source = RomM }` carrying the new ids and their summed minutes
  (plus `promotionEvents`), or `Ok []` if no ids are new or the new minutes sum to zero.
- `evolve`'s `Play_session_recorded` arm unions a `RomM` record's `RommSessionIds` into
  `ImportedRommSessionIds`; `Play_session_minutes_corrected`/`Play_session_moved`/
  `Play_session_removed` never touch that set (unchanged arms — they only ever touched
  `PlaySessions`), verified directly by a Grounded-shaped regression test (correct, move,
  then remove a RomM session; the id stays "known"; a later `Record_romm_play_session` with
  the same id still returns `Ok []`).
- Fixed the latent bug the ADR calls out: `PlaySessionProjection.fs`'s
  `encodeSource`/`getSource`/`toPlaySessionDto` are now an exhaustive 3-way match
  (`SteamSync | Manual | RomM`) instead of collapsing anything non-`"Manual"` to `SteamSync`.
  A regression test proves a `RomM`-sourced `Play_session_recorded` round-trips through
  `game_play_session.source = 'RomM'` and both `getForGame`/`getBySlugAndDay`.
- `Games.Serialization.handledEventTypes` includes `Game_romm_rom_id_set`.

Key files: `src/Shared/Shared.fs` (`PlaySessionSource`), `src/Server/Games.fs` (events,
commands, `evolve`, `decide`, `Serialization`), `src/Server/GameProjection.fs`
(`romm_rom_id` column, `Game_romm_rom_id_set` projection arm, `findByRommRomId`),
`src/Server/PlaySessionProjection.fs` (exhaustive source encode/decode). Tests:
`tests/Server.Tests/GamesTests.fs` (`rommPlaySessionDecideTests` covers every acceptance
criterion at the `decide`/`evolve` level, plus `Set_romm_rom_id`/serialization coverage),
`tests/Server.Tests/PlaytimeTrackerTests.fs` (`rommPlaySessionProjectionTests` covers the
DB round-trip and `findByRommRomId`); `ProjectionDriftTests.fs`/`GameFacetProjectionTests.fs`
updated only for the `PlaySessionRecordedData` record shape change (added
`RommSessionIds = Set.empty` to their existing literals).

`npm run build`, `dotnet run --project tests/Server.Tests/Server.Tests.fsproj` (1016 passed),
and `npm run test:client` (125 passed) all green — the client build confirms no non-exhaustive
match was left anywhere on the new `PlaySessionSource` case.

`integration-jkbm1` (the RomM HTTP adapter, blocked on this task) can now call
`Set_romm_rom_id`/`Record_romm_play_session` and `GameProjection.findByRommRomId` directly.
