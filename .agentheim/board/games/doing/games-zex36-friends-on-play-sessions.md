---
id: games-zex36
title: Games — attach friends to individual play sessions like Movies' watch sessions; adding one also adds them to the game's Played with set, and the Friend page shows the shared play dates
status: doing
type: feature
context: games
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [play-session, friends, game-detail, friend-detail, frontend]
related_adrs: [0091, 0050, 0088]
related_research: []
prior_art: [games-p6vkz, games-rmxg2, games-h4mrd]
---

## Why

The builder wants to record who was there for a specific gaming-day session, the same
way Movies records friends per watch session (`Friend_added_to_watch_session` /
`Friend_removed_from_watch_session`). Today Games only has a game-level **Played with**
set that is toggled by hand (`Add_played_with`/`Remove_played_with`). There is no way
to say "I played this with Alice on the 12th". The Friend detail page's list for games
is also a stub: `Api.fs` (~3217) hard-codes `Dates = []` for every game played with a
friend. Movies and Series show real per-session dates there
(`MovieProjection.getMoviesWatchedWithFriend`).

Builder decisions (2026-09-27):
- **Sessions imply Played with.** Adding a friend to a session also adds them to the
  game's Played with set. The relation only grows: removing them from a session later
  does not take them out of Played with. Only the existing manual `Remove_played_with`
  does that. Manual Played with stays for play from before session tracking.
- **The Friend page lists shared play sessions** (game + dates), like movies watched
  together.

## What

### Aggregate (`src/Server/Games.fs`)
- New events, unprefixed like the `Play_session_*` family:
  `Friend_added_to_play_session of day: string * friendSlug: string` /
  `Friend_removed_from_play_session of day: string * friendSlug: string`.
  JSON keys `"day"`/`"friendSlug"`, both `Required.Field`. Add both names to the
  known-event-types list (~line 936) and to `EventFormatting.fs`.
- New commands: `Add_friend_to_play_session of day * friendSlug` /
  `Remove_friend_from_play_session of day * friendSlug`.
- `ActiveGame` gains `PlaySessionFriends: Map<string, Set<string>>` (gaming day →
  friend slugs), initialized to `Map.empty` on `Game_added_to_library`. Two invariants:
  its keys are always a subset of `PlaySessions`' keys, and it never holds an empty set
  (a day that becomes empty is removed from the map).
- `evolve`:
  - `Friend_added_to_play_session`: add the friend to that day's set.
  - `Friend_removed_from_play_session`: remove the friend, and drop the day's entry if
    the set becomes empty.
  - `Play_session_moved (fromDay, toDay, _)`: extend the existing minutes-merge arm so
    the destination gets the **union** of both days' friend sets, and `fromDay`'s
    entry is removed.
  - `Play_session_removed (day, _)`: also remove `day` from `PlaySessionFriends`. This
    is load-bearing. Without it, a later session recorded on the same day would bring
    the old friends back.
  - `Play_session_minutes_corrected` / `Play_session_recorded` (Steam/Manual/RomM):
    **no change**. A sync delta merging into an existing day never touches its friends.
- `decide`:
  - `Add_friend_to_play_session`:
    - `Error "Play session not found"` if the day isn't in `PlaySessions`.
    - `Ok []` if the friend is already on the session. This path does not re-check
      `PlayedWith`.
    - Otherwise
      `Ok ([Friend_added_to_play_session (day, friendSlug)] @ (if PlayedWith contains friendSlug then [] else [Game_played_with friendSlug]))`.
      That is one pure decision, the same shape as `Record_play_session`'s
      `promotionEvents`.
  - `Remove_friend_from_play_session`:
    - Same "session not found" check.
    - Emits `Friend_removed_from_play_session` only if the friend is on the session,
      else `Ok []`.
    - Never touches `PlayedWith`.
  - No friend-existence check, consistent with `addGameFamilyOwner`/`addGamePlayedWith`.

### Projection (`src/Server/PlaySessionProjection.fs`)
- New table, owned by the same handler so it shares one checkpoint:
  ```sql
  CREATE TABLE IF NOT EXISTS game_play_session_friend (
      game_slug TEXT NOT NULL, date TEXT NOT NULL, friend_slug TEXT NOT NULL,
      PRIMARY KEY (game_slug, date, friend_slug));
  CREATE INDEX IF NOT EXISTS idx_play_session_friend_friend ON game_play_session_friend(friend_slug);
  ```
  It uses a natural primary key and no synthetic id, for the same drift-detector reason
  as `game_play_session`. Drop it in `Drop` too.
- Handlers, all idempotent under replay:
  - Added → `INSERT OR IGNORE`.
  - Removed → `DELETE`.
  - Moved → `INSERT OR IGNORE ... SELECT game_slug, @toDay, friend_slug ... WHERE date=@fromDay`,
    then `DELETE ... WHERE date=@fromDay`.
  - Session removed → `DELETE ... WHERE game_slug=@slug AND date=@day`.
- `getGamePlaySessions` fills each row's `Friends` (join the new table).
- A Friend-page query shaped like `MovieProjection.getMoviesWatchedWithFriend`, grouped
  into `FriendWatchedItem` with real `Dates`. Wire it into `Api.fs` `getFriendMedia`
  (~3206-3222) in place of the hard-coded `Dates = []`.
- **Membership doesn't change:** a game still counts as "played with" through the
  `played_with` column. A manual Played with entry with no session still appears, with
  `Dates = []`. Session dates are only added on top for games that have them.

### Shared / API (`src/Shared/Shared.fs`, `src/Server/Api.fs`)
- `PlaySessionDto` gains `Friends: FriendRef list` (like `WatchSessionDto.Friends`).
- `IMediathecaApi` gains `addFriendToPlaySession` / `removeFriendFromPlaySession`:
  `string -> string -> string -> Async<Result<unit, string>>` (gameSlug, day,
  friendSlug), shaped like `addFriendToWatchSession`.
- The implementation follows the usual `executeCommand ... projectionHandlers` pattern
  (see `addGamePlayedWith`, ~4123).

### Client (`src/Client/Pages/GameDetail/`)
- Play History card: friend chips on each row, plus an add/remove picker. Reuse the
  MovieDetail watch-session friend-picker pattern and the paper-overlay dropdown
  (`DesignSystem.paperOverlay`/`paperDropdown`). No new visual vocabulary: if the reused
  pattern doesn't fit, stop and file a design-system task.
- `Types.fs`/`State.fs`: add/remove-friend messages that call the new APIs and refetch
  the sessions.

## Acceptance criteria
- [ ] Expecto: `Add_friend_to_play_session` on a day with no session returns `Error "Play session not found"`.
- [ ] Expecto: adding a friend who is neither on the session nor in `PlayedWith` returns `Ok [Friend_added_to_play_session (day, friendSlug); Game_played_with friendSlug]`, in that order.
- [ ] Expecto: adding a friend already in `PlayedWith` (but not on this session) returns only `Ok [Friend_added_to_play_session (day, friendSlug)]`.
- [ ] Expecto: adding a friend already on the session returns `Ok []`, even if `PlayedWith` no longer contains them (ADR-0091).
- [ ] Expecto: removing a friend who isn't on the session returns `Ok []`. Removing one who is emits `Friend_removed_from_play_session` and leaves `PlayedWith` untouched.
- [ ] Expecto: `Play_session_moved` carries the day's friends to the destination. Moving onto a day that already has friends combines both sets.
- [ ] Expecto: `Play_session_removed` drops the day's friends. A session recorded again on that day (manual, Steam, or RomM) starts with no friends.
- [ ] Expecto (sync regression): take a manual session with a friend, then apply a Steam observed-total delta and a RomM session that both merge into that day. The friend set is unchanged, and minutes are summed as before.
- [ ] Expecto: `game_play_session_friend` gives the right rows through add, remove, move (including onto a day that already has other friends), and session removal, and gives the same rows after a projection rebuild.
- [ ] Expecto: `checkProjectionDrift` reports no discrepancy for the new table.
- [ ] Expecto: `getFriendMedia` shows real dates for a game with shared sessions, and still lists a game where the friend is only in manual Played with, with `Dates = []`.
- [ ] Vitest (`*.test.fs`): GameDetail `State.update` handles the add/remove-friend-on-session messages (issues the API command, refetches on success).
- [ ] `npm run build`, `npm test`, and `npm run test:client` pass.
- [ ] The Play History card shows friend chips per session, and the add/remove picker works and matches the paper-overlay pattern. [human-eye]
- [ ] The Friend detail page shows real play dates for games shared through sessions. [human-eye]

## Notes
- **Read ADR-0091 first.** It records the parallel `PlaySessionFriends` map (instead of
  reshaping `PlaySessions`) and why `Game_played_with` is emitted by `decide` (instead
  of being folded into `evolve` or derived in a read model), along with the rejected
  alternatives. This intentionally differs from Movies, which folds its side effect
  into `evolve`.
- Accepted risk: after a manual `Remove_played_with`, `PlaySessionFriends` can hold
  friends who are no longer in `PlayedWith`. That is not cascaded.
- `mergeSession` in `PlaySessionProjection.fs` is known to be non-idempotent under a
  checkpoint rewind. Don't fix it here, and don't make it worse: the new handlers must
  stay idempotent.
- Open, out of scope: should adding a friend to a session also remove them from
  Want-to-play-with, the way Movies clears `Want_to_watch_with`? The builder hasn't
  asked for it. Leave it out.
- Sibling games-fbf3j (remove the 10-row Play History cap) touches the same card.
  Expect a trivial merge.
