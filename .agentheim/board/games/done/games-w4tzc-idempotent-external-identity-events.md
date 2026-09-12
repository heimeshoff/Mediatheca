---
id: games-w4tzc
title: Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards
status: done
type: bug
context: games
created: 2026-08-01
completed: 2026-08-01
depends_on: [infrastructure-e4kwm]
blocks: []
tags: [games, steam, idempotence, event-log]
related_adrs: [0042]
related_research: []
prior_art: [integration-004]
---

## Why

Under the `infrastructure-e4kwm` doctrine these three stay domain facts, not cache:

- `Game_steam_app_id_set` — 1019 events across 1019 streams. The *link* between a library game and a
  Steam appId is our decision, and `PlaytimeTracker.findByName` (`src/Server/PlaytimeTracker.fs:636`)
  makes it by **fuzzy name match**, so corrections must be auditable.
- `Game_family_owner_added` — 964 events across 908 streams. Dual-sourced (`Api.fs:442-470` Steam
  family import, `Api.fs:3110-3122` explicit UI action), and the stored value is a **Friends-BC slug**
  reached through a user-maintained `steamIdToFriendSlug` map. Steam alone cannot reconstruct it.
- `Game_steam_library_date_set` — 909 events across 908 streams.

They are correctly events. But ~1.1 events per game for values that never change is an **idempotence
bug**, not a doctrine violation. `Games.decide` already guards `Set_steam_library_date`
(`src/Server/Games.fs:294-296`); the other two do not.

Fully independent of the cache tier and of the Series and play-session chains.

## What

Add no-op-on-unchanged guards in `Games.decide` for `Set_steam_app_id` and `Add_family_owner`, matching
the existing `Set_steam_library_date` idiom at `Games.fs:294-296`.

## Acceptance criteria

- [ ] Expecto: dispatching `Set_steam_app_id` twice with the same value emits one event; with a different value emits two.
- [ ] Expecto: dispatching `Add_family_owner` twice with the same friend slug emits one event; with a different slug emits two.
- [ ] Expecto: `Games.reconstitute` yields an identical state for a stream with and without the duplicate events.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

This does not remove the ~1000 existing duplicates — that is `administration-z6ymt`'s job, deferred.
It stops the bleeding.

## Outcome

Investigated `Games.decide` (`src/Server/Games.fs`) before writing any production code, per this
worker's TDD discipline. Both guards described in the "What" section **already existed**:

- `Set_steam_app_id` (`Games.fs:272-274`, `if game.SteamAppId = Some steamAppId then Ok [] else ...`)
  — introduced in commit `a4d16977` (2026-02-15), predating this session.
- `Add_family_owner` (`Games.fs:244-246`, `if game.FamilyOwners |> Set.contains friendSlug then Ok []
  else ...`) — introduced in commit `2dcfca42` (2026-02-15), predating this session.

Both guards are structurally identical to the `Set_steam_library_date` idiom this task cites as the
model (`Games.fs:294-296`). The event-count skew in the Why section (964/908 for family owner) reads
as legitimate multi-owner families (a game can have more than one family owner slug) rather than a
missing-guard symptom — `Set_steam_app_id`'s own count (1019/1019, exactly 1:1) confirms no live
duplication is occurring under current code.

No production code change was made — there was nothing to fix. Per the worker's TDD-skip provisions,
this counts as the acceptance criteria already being true rather than an under-refined task: all three
stated Expecto criteria are testable and were added/verified in
`tests/Server.Tests/GamesTests.fs` (some already existed, e.g. "Adding same family owner is
idempotent" and "Setting same steam app id is idempotent"; this task added the "different value ⇒
new event" and "reconstitute identical with/without duplicate" cases for both commands):

- `Adding a different family owner is not idempotent`
- `Reconstitute yields identical state with and without a duplicate family owner event`
- `Setting a different steam app id is not idempotent`
- `Reconstitute yields identical state with and without a duplicate steam app id event`

All 59 `Games` Expecto tests and the full 453-test suite pass (`npm test -- --sequenced`); `npm run
build` succeeds. The ~1000 historical duplicate events remain in the event log untouched, as intended
— `administration-z6ymt` still owns their cleanup.
