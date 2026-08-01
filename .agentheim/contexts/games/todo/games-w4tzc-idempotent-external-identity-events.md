---
id: games-w4tzc
title: Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards
status: todo
type: bug
context: games
created: 2026-08-01
completed:
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
