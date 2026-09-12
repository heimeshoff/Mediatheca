---
id: games-v4nqe
title: Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)
status: done
type: refactor
context: games
created: 2026-08-04
completed: 2026-08-04
depends_on: [games-a7dqx]
blocks: [administration-z6ymt, games-j6wkr]
tags: [games, metadata, cache, steam, event-log, migration]
related_adrs: [0043, 0045, 0048, 0053, 0054, 0055]
related_research: []
prior_art: [games-a7dqx, series-r2xhv, series-d5tpn]
---

## Why

`games-a7dqx` (split 1 of 3) built the cache schema, the ADR-0053 override event/command, the
`deriveFacets`/`PlayFacets.merge` functions, and a resumable backfill job — all additive, nothing
existing changed. This task is the actual stop-emitting cutover: it converts every live emission
site that still writes `Game_play_mode_added`/`removed`, `Set_description`, `Set_short_description`,
`Set_website_url`, `Set_hltb_hours`, `Set_steam_last_played`, and `Categorize_game` into writes
against `game_metadata_cache` instead, deletes the now-dead commands (letting the compiler find
every remaining emission site — the same technique `Games.fs`'s `Game_store_added`/
`Game_store_removed` already demonstrate as a four-part no-op precedent), drops the
now-fully-unread projection columns, and finalizes the `Shared.fs` DTO change from
`PlayModes: string list` to `PlayFacets`/`PlayFacetsOverride`.

**Why the DTO change and the forced client deletion live here, not in games-j6wkr:** deleting
`Add_play_mode`/`Remove_play_mode` from `GameCommand` means the server-side implementations of
`getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` can no longer dispatch to anything —
those `IMediathecaApi` members must be removed. Removing them breaks the client's `PlayModePicker`
(`GameDetail/Views.fs:331`) at compile time. Per this decomposition's hard constraint ("each task
must compile, pass tests, and boot independently"), this task must also perform the *mechanical*
deletion of the now-uncompilable client code (the picker, its call site, `ShowPlayModePicker`/
`AllPlayModes` state, the `getAllPlayModes` dispatch) — not the *new* UI (badges, Auto/On/Off
controls, filters), which is games-j6wkr's distinct, positive-value work. This mirrors how the
original single-task file already bundled "`PlayModePicker`... deleted" under the same "UI"
heading as the DTO change — that pairing was never really separable from the command deletion, it
was always task2's, not task3's, forced consequence.

**Transitional consequence, named explicitly:** after this task lands and before games-j6wkr lands,
the app has **no play-mode UI at all** — no picker, no badges, no filters. This is a real,
visible-but-bounded regression window, immediately closed by games-j6wkr, the next task in the
chain. It is the same *kind* of accepted gap ADR-0048 named for the Series BC's two-task split
(`series-r2xhv` → `series-q8jwc`), just at the UI layer instead of the read-composition layer, and
bounded the same way: by the very next task, not left open-ended.

## Event disposition table

Four-part rule: **codec kept** / **aggregate `evolve` arm → explicit no-op** / **projection arm
deleted + column dropped** / **command deleted**.

| Event | Command | Disposition | Notes |
|---|---|---|---|
| `Game_play_mode_added` / `Game_play_mode_removed` | `Add_play_mode` / `Remove_play_mode` | Demoted, four-part rule | Superseded by `Game_play_facets_overridden` (games-a7dqx). `ActiveGame.PlayModes: Set<string>` deleted outright (legacy arms return `state`, matching the `Game_store_added` precedent). `game_detail.play_modes` dropped. |
| `Game_description_set` | `Set_description` | Demoted, four-part rule | Column also written by `Game_added_to_library`'s arm — see hazard 1. |
| `Game_short_description_set` | `Set_short_description` | Demoted, four-part rule | Same creation-carried caveat. |
| `Game_website_url_set` | `Set_website_url` | Demoted, four-part rule | Same creation-carried caveat. |
| `Game_hltb_hours_set` | `Set_hltb_hours` | Demoted, four-part rule | Not creation-carried. `setGameHltbHours` (`Api.fs:3001`) has zero client call sites — delete, don't convert. `fetchHltbData` (`Api.fs:4339`) becomes a cache write. No override event needed. |
| `Game_steam_last_played_set` | `Set_steam_last_played` | Demoted, **derived not cached** | Redundant with `game_play_session`. Column dropped; reads already switched to `MAX(date)` by games-a7dqx. |
| `Game_categorized` | `Categorize_game` | Demoted, four-part rule | Dead code (verified — zero call sites in `src/Client`, zero dispatch sites outside `Games.fs`). `game_list.genres`/`game_detail.genres` dropped; the cache's `genres` column already shipped **unpopulated** in games-a7dqx (`MetadataCache.fs` migration) — this task only seeds it from `game_detail` **before** the drop, no `ADD COLUMN` needed. `GameAddedData.Genres` payload unchanged. |

**Confirmed out of scope:** `Game_steam_library_date_set` stays evented (first-sighting fact Steam
cannot be re-queried for). `Game_rawg_id_set`/`Game_steam_app_id_set` stay evented (the *link* is
our decision, per ADR-0043's boundary call).

## What

### Emission-site conversion (18 call sites, 5 structurally distinct flows)

- `PlaytimeTracker.createGameFromSteam` (`PlaytimeTracker.fs:259-343`) stops calling
  `Add_play_mode`/`Set_steam_last_played`; writes derived facets, category ids, description,
  short_description, website_url, genres to `game_metadata_cache` using games-a7dqx's
  `FacetDerivation.deriveFacets` + `MetadataCache.upsertGameFacets` for the facet/category-id
  slice, plus a **new identity-card upsert this task authors** for the
  description/short_description/website_url/genres slice — a7dqx shipped no such writer
  (`upsertGameFacets` deliberately excludes those columns; see its doc comment). The new
  helper must follow the same `INSERT ... ON CONFLICT DO UPDATE` slice discipline — never
  `INSERT OR REPLACE`, which would silently null the facet columns of an existing row.
- `Api.fs`'s Steam family-import flow (`Api.fs:427-535`, `:550-582`, `:660-677` —
  `Set_steam_library_date` stays; `Set_short_description`/`Set_website_url`/`Add_play_mode`
  convert) writes cache directly.
- `Api.fs:attachSteamToGameCore` (~line 1068) stops calling
  `Set_description`/`Set_short_description`/`Set_website_url`/`Add_play_mode`; preserves its
  "only fill if currently empty" guard by reading `game_metadata_cache` instead of the (about to
  be dropped) projection columns.
- `Api.fs`'s Steam-sync/enrichment flow (~lines 3500-3770, including
  `findGamesWithEmptyDescriptionAndSteamAppId`'s existing throttled backfill loop at
  `Async.Sleep 300`) converts the same way; `findGamesWithEmptyDescriptionAndSteamAppId`
  (`GameProjection.fs:819` post-a7dqx) is rewritten to query `game_metadata_cache` for empty description, or
  explicitly retired in favor of games-a7dqx's facet-backfill job if the worker judges the two
  redundant (state which was chosen).
- `setGameHltbHours` (`Api.fs:3001`, zero client call sites) is deleted with `Set_hltb_hours`;
  `fetchHltbData` (`Api.fs:4339`) writes `game_metadata_cache` directly.
- **Identity-card write conflict** (hazard 1, inherited from the original file): `description`,
  `short_description`, `website_url`, `genres` are written by *two* sources today —
  `Game_added_to_library` (creation) and the now-demoted `Set_*` commands. Once the columns drop,
  the creation event's projection arm must also stop writing them, and the *creation code path
  itself* (`PlaytimeTracker.createGameFromSteam`, and any `Api.fs` game-creation flow), not the
  `ProjectionHandler` (ADR-0045), writes them into `game_metadata_cache` immediately after
  `Add_game` succeeds. `GameAddedData`'s payload schema stays untouched — this is a necessary
  consequence of the column drop, not a separate design choice.
- Every converted call site that fetches Steam `appdetails` stores `steam_category_ids` on that
  fetch, using games-a7dqx's decoder and `deriveFacets` (games-a7dqx's own backfill job already
  does this for its own fetches; this task extends the same discipline to these 18 call sites).
- Every converted flow writes only the cache tier — no "don't clobber overrides" guard exists or
  is needed (ADR-0053), verified for each of the 18 call sites.
- `grep -rn "Games\.\(Add_play_mode\|Remove_play_mode\|Set_description\|Set_short_description\|Set_website_url\|Set_hltb_hours\|Set_steam_last_played\|Categorize_game\)" src/Server` returns
  zero matches once the commands are removed (pre-flight; the compiler enforces it structurally
  after removal from the DU).

### Four-part rule execution

- Codec (`serialize`/`deserialize`/`handledEventTypes`) unchanged for all seven demoted event
  groups; `evolve` arms become explicit no-ops (old streams replay without error or corrupted
  state, matching the `Game_store_added` precedent); `GameProjection.handleEvent` arms deleted;
  `Add_play_mode`, `Remove_play_mode`, `Set_description`, `Set_short_description`,
  `Set_website_url`, `Set_hltb_hours`, `Set_steam_last_played`, `Categorize_game` deleted from
  `GameCommand`.
- `Game_added_to_library`'s `GameAddedData` payload is **unchanged**; only its
  `GameProjection.handleEvent` arm stops writing description/short_description/website_url/genres
  into the (now-dropped) projection columns.

### Schema — column drops (this task's migration, load-bearing ordering)

- A one-time step copies `game_detail.genres` → `game_metadata_cache.genres` for every row where
  the cache's `genres` is still unset, run and completed **before** the column-drop step in the
  same migration sequence — the source disappears once dropped.
- `game_list.genres`, `game_list.hltb_hours`, `game_detail.description`,
  `game_detail.short_description`, `game_detail.website_url`, `game_detail.genres`,
  `game_detail.hltb_hours`, `game_detail.hltb_main_plus_hours`,
  `game_detail.hltb_completionist_hours`, `game_detail.play_modes`, `game_detail.steam_last_played`
  are dropped (`ALTER TABLE ... DROP COLUMN`, SQLite ≥3.35).

### Remaining read composition — genres and play-facets

- `getAll`/`getBySlug`'s `Genres` field switches to `game_metadata_cache.genres` (populated by the
  copy step above, or by the creation-path cache write for games created after this task lands).
- `getAll`/`getBySlug`'s facet composition wires games-a7dqx's `PlayFacets.merge`/composition
  helper into the actual DTO assembly, producing the new `PlayFacets`/`PlayFacetsOverride` DTO
  fields (below), exactly the way `resolveFriendRefs` already joins `friend_list`
  (`GameProjection.fs:395-409`).
- `getGameGenreDistribution` and any other of the ten originally-surveyed readers
  (`getRecentlyAddedGames`, `getGamesRecentlyPlayed`, `getBacklogStats`, `getInFocusEstimate`,
  `getHltbComparisons`, `getGamesCompletedPerYear`, `getGamesBeatenThisYear`) still referencing
  `genres`/`play_modes` after games-a7dqx's pass are converted here (grep to confirm which ones
  actually touch these two fields — games-a7dqx already converted everything else these functions
  read).

### Shared.fs DTO finalization (the compile-coupled change — lives here, and only here)

- `Shared.fs`'s `GameListItem`/`GameDetail`: `PlayModes: string list` → `PlayFacets: PlayFacets`
  (+ `PlayFacetsOverride` on `GameDetail`, so the client can render merged values while posting
  overrides). `Genres`/`HltbHours` field shapes unchanged, only re-sourced (already true as of
  games-a7dqx for Hltb; genres re-sourced by this task).
- `getAllPlayModes`, `addGamePlayMode`, `removeGamePlayMode` removed from `IMediathecaApi` and
  its server implementation — superseded by `overrideGamePlayFacets` (added by games-a7dqx).

### Forced mechanical client deletion (compile-fix only — not games-j6wkr's new UI)

- `GameDetail/Views.fs`'s `PlayModePicker` (~line 331) and its call site (~1926-1929),
  `GameDetail/Types.fs`'s `ShowPlayModePicker`/`AllPlayModes`, and `GameDetail/State.fs`'s
  `getAllPlayModes` dispatch (~58, ~121) are deleted. No replacement badge/control is built here —
  that is games-j6wkr's scope. The game card/detail page simply shows no play-mode information for
  the duration between this task and games-j6wkr.
- `npm run build` passes after these deletions (Fable compile gate).

## Acceptance criteria

### Four-part rule per event group
- [ ] Codec unchanged; `evolve` arms are explicit no-ops for all seven demoted event groups;
      `GameProjection.handleEvent` arms deleted; `Add_play_mode`, `Remove_play_mode`,
      `Set_description`, `Set_short_description`, `Set_website_url`, `Set_hltb_hours`,
      `Set_steam_last_played`, `Categorize_game` deleted from `GameCommand`.
- [ ] `Game_added_to_library`'s `GameAddedData` payload is unchanged; only its
      `GameProjection.handleEvent` arm stops writing description/short_description/website_url/genres.
- [ ] The creation code paths (`PlaytimeTracker.createGameFromSteam`, and any `Api.fs`
      game-creation flow) write description/short_description/website_url/genres directly into
      `game_metadata_cache` immediately after `Add_game` succeeds.

### Import/refresh paths write cache, not events
- [ ] `PlaytimeTracker.createGameFromSteam` stops calling `Add_play_mode`/`Set_steam_last_played`;
      writes derived facets, category ids, description, short_description, website_url, genres.
- [ ] `Api.fs`'s Steam family-import flow writes cache directly.
- [ ] `attachSteamToGameCore` stops calling the four demoted setters/`Add_play_mode`; preserves its
      "only fill if currently empty" guard reading `game_metadata_cache`.
- [ ] `Api.fs`'s Steam-sync/enrichment flow converts the same way;
      `findGamesWithEmptyDescriptionAndSteamAppId` is rewritten to query the cache, or explicitly
      retired in favor of games-a7dqx's backfill job (state which was chosen).
- [ ] `setGameHltbHours` is deleted with `Set_hltb_hours`; `fetchHltbData` writes cache directly.
- [ ] `grep -rn "Games\.\(Add_play_mode\|Remove_play_mode\|Set_description\|Set_short_description\|Set_website_url\|Set_hltb_hours\|Set_steam_last_played\|Categorize_game\)" src/Server` returns
      zero matches.
- [ ] Every converted flow's Steam `appdetails` fetch stores `steam_category_ids`.
- [ ] Every converted flow writes only the cache tier — no "don't clobber overrides" guard exists.
- [ ] The new identity-card cache writer (description/short_description/website_url/genres)
      uses `INSERT ... ON CONFLICT DO UPDATE` scoped to its own column slice — never
      `INSERT OR REPLACE` — proven by a test showing facet/category-id/`fetched_at` values
      of an existing row survive an identity-card write.

### Schema / migration
- [ ] The one-time `game_detail.genres` → `game_metadata_cache.genres` copy runs and completes
      before the column-drop step, in the same migration sequence.
- [ ] `game_list.genres`, `game_list.hltb_hours`, `game_detail.description`,
      `game_detail.short_description`, `game_detail.website_url`, `game_detail.genres`,
      `game_detail.hltb_hours`, `game_detail.hltb_main_plus_hours`,
      `game_detail.hltb_completionist_hours`, `game_detail.play_modes`,
      `game_detail.steam_last_played` are dropped.

### Remaining read composition
- [ ] `getAll`/`getBySlug`'s `Genres` field reads from `game_metadata_cache.genres`.
- [ ] `getAll`/`getBySlug` wire games-a7dqx's `PlayFacets.merge`/composition helper into the DTO
      assembly, producing the merged `PlayFacets` value and the raw `PlayFacetsOverride`.
- [ ] `getGameGenreDistribution` and any remaining reader touching `genres`/`play_modes` (grep to
      confirm) are converted.

### Shared.fs / API / client
- [ ] `Shared.fs`'s `GameListItem`/`GameDetail`: `PlayModes: string list` → `PlayFacets: PlayFacets`
      (+ `PlayFacetsOverride` on `GameDetail`); `Genres`/`HltbHours` shapes unchanged.
- [ ] `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` removed from `IMediathecaApi` and
      its implementation.
- [ ] `GameDetail/Views.fs`'s `PlayModePicker` and call site, `GameDetail/Types.fs`'s
      `ShowPlayModePicker`/`AllPlayModes`, `GameDetail/State.fs`'s `getAllPlayModes` dispatch are
      deleted (compile-fix only, no replacement UI built here).
- [ ] `npm run build` passes after the client-side deletions.

### Drift / rebuild / DTOs
- [ ] `checkProjectionDrift` (ADR-0031) stays zero for `GameProjection` after the column drops.
- [ ] A full `Drop; Init; replay` rebuild reproduces `game_list`/`game_detail` correctly; the
      demoted event types (`Game_categorized` included) still deserialize and their no-op `evolve`
      arms don't corrupt replay of pre-cutover streams.
- [ ] `Administration.tableRegistry` classifications for `game_list`/`game_detail` (Projected) and
      `game_metadata_cache` (Cache) are unchanged — column sets differ, classifications don't.

## Notes

**Post-a7dqx reconciliation (2026-08-04, after split 1 landed):** verified against the shipped
foundation — `FacetDerivation.deriveFacets`, `PlayFacets.merge`, `MetadataCache.upsertGameFacets`,
and the `Game_play_facets_overridden`/`Override_play_facets` pair all exist as this task assumes.
Two corrections folded in above: the cache's `genres` column already exists (a7dqx shipped it
unpopulated — this task seeds it, no `ADD COLUMN`), and there is no `upsertGameMetadata` — the
identity-card writer is authored *here*, following `upsertGameFacets`'s ON-CONFLICT slice
discipline. ADR-0054 (fixed category-id → facet table) governs derivation.

**Fold-forward from the original file's worker survey (2026-08-04), scoped to this task:**
- `Api.fs` (4410 lines) — `grep -c` on the eight commands/functions slated for conversion returns
  **18 call sites spread across at least 5 structurally distinct flows** (family import
  ~427-582/660-677, `attachSteamToGameCore` ~1068, sync/enrichment ~3500-3770, HLTB endpoints
  ~3001/4339) — each flow needs independent conversion plus its own manual verification, since
  none of it is mechanical find-replace (the "only fill if currently empty" guard at
  `attachSteamToGameCore` in particular has to be re-pointed from a dropped projection column to
  the cache table without changing its guard semantics).
- `Games.fs` (854 lines) — `Game_store_added`/`Game_store_removed` already demonstrate the exact
  four-part no-op precedent to copy for the seven demoted event groups.
- Hazard 4 (original file): `setGameHltbHours` is dead code (zero client call sites) — delete
  alongside `Set_hltb_hours`, don't convert.
- Hazard 5 (original file): HLTB needs no override event — no UI path exists for hand-typed HLTB
  hours; both call sites are third-party-fetch flows. Clean cache move; don't over-generalize the
  `PlayFacets` override pattern here.

**Cutover & refetch leg 1 (from the original file's "Cutover & refetch plan"):** purging the
~7668 existing `Game_play_mode_added` events is `administration-z6ymt`'s job (already covered by
ADR-0038's wipe-first import), which `depends_on` this task specifically — the purge is only safe
once every demoted event type has been reduced to an explicit `evolve` no-op with its projection
arm removed, which is what this task (not games-a7dqx) accomplishes.

**Sequencing constraint (inherited from the original file):** this task must not be scheduled
*ahead* of the vision's Steam Import Enhancement or HowLongToBeat Integration items — both should
land after it, or they pour more junk events into a log this task is trying to shrink.

**This task's diff is still large** (the same 5-flow Api.fs surface the original bounce note
flagged), but is now bounded to emission conversion + column drop + DTO finalization + a
compile-fix client deletion — it does not also carry the new domain model/schema (games-a7dqx,
already landed) or the new UI construction (games-j6wkr, next).

## Outcome

Landed the full emission cutover: all 18 Steam/RAWG call sites across `Api.fs` (family
import, `attachSteamToGameCore`, `importSteamLibrary` matched-by-id/matched-by-name/create/
description-backfill) and `PlaytimeTracker.createGameFromSteam` now write
`game_metadata_cache` (via two new writers, `MetadataCache.upsertGameIdentityCard`/
`tryGetGameIdentityCard` for description/short_description/website_url/genres and
`upsertGameHltbHours` for HLTB) instead of dispatching the demoted commands. Two shared
Api.fs helpers (`updateGameIdentityCache`, a read-modify-write echoing untouched fields;
`updateGameFacetsFromCategoryIds`) collapsed the 18-site conversion into a manageable,
mechanical diff. `Categorize_game`, `Set_hltb_hours`, `Set_description`,
`Set_short_description`, `Set_website_url`, `Add_play_mode`, `Remove_play_mode`,
`Set_steam_last_played` are deleted from `GameCommand`; their events stay in the codec with
explicit no-op `evolve`/`GameProjection.handleEvent` arms (the `Game_store_added`
precedent) — the `grep -rn "Games\.(...)" src/Server` pre-flight returns zero matches.
`game_list.genres`/`hltb_hours` and `game_detail.description`/`short_description`/
`website_url`/`genres`/`hltb_hours`/`hltb_main_plus_hours`/`hltb_completionist_hours`/
`play_modes`/`steam_last_played` are dropped (`GameProjection.dropDeprecatedColumns`),
preceded by a one-time `game_detail.genres` -> `game_metadata_cache.genres` copy
(`GameProjection.copyGenresToMetadataCache`), both wired into `Composition.buildApp` in
the same seed-then-drop order the series precedent established. `getAll`/`getBySlug`/
`getRecentlyAddedGames` now read `Genres` from the cache and compose `PlayFacets`/
`PlayFacetsOverride` via shared row-readers (`readCachedPlayFacets`/
`readPlayFacetsOverrideRow`), avoiding an N+1 per-row facet query.
`findGamesWithEmptyDescriptionAndSteamAppId` was rewritten (not retired) to query the
cache. `Shared.fs`'s `GameListItem`/`GameDetail` carry `PlayFacets` (+ `PlayFacetsOverride`
on `GameDetail`); `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode`/
`setGameHltbHours` are removed from `IMediathecaApi`. Client: `PlayModePicker` and its
call sites, `ShowPlayModePicker`/`AllPlayModes`/`Add_play_mode`/`Remove_play_mode`/
`Toggle_play_mode_picker`/`Play_modes_loaded` are deleted (mechanical compile-fix only,
per this task's explicit scope note — no replacement UI, that is games-j6wkr's).

Fallout discovered mid-task and fixed: `MetadataCache.seedFromProjections`'s game-side
`INSERT` (a7dqx) unconditionally selected `description`/`short_description`/`website_url`/
`hltb_*` from `game_detail` — since this task's own DDL change means a *fresh* install's
`game_detail` never has those columns at all, that INSERT would have crashed every fresh
boot. Split into two steps: an always-safe seed of the columns this task does NOT drop
(`cover_ref`/`backdrop_ref`/`rawg_id`/`rawg_rating`), and a `try/with`-wrapped `UPDATE` for
the columns this task drops (same "defensive, tolerates a missing source column" idiom the
series half already used) — correct on both a genuine legacy-database upgrade (columns
still present at seed time, before the drop) and a fresh install (swallowed, nothing to
seed).

47 tests added/updated across `GamesTests.fs` (demoted-command removal, aggregate-layer
no-op proof for all seven demoted event groups) and `GameFacetProjectionTests.fs`
(identity-card writer slice discipline, the genres copy migration and its idempotency, the
column-drop migration, projection-layer no-op replay, `getAll`/`getBySlug` facet/override
DTO wiring); `MetadataCacheTests.fs` updated for the seed split. 632/632 tests pass;
`npm run build` (Fable compile gate) passes.

Key files: `src/Server/Games.fs`, `src/Server/GameProjection.fs`, `src/Server/MetadataCache.fs`,
`src/Server/PlaytimeTracker.fs`, `src/Server/Api.fs`, `src/Server/Composition.fs`,
`src/Shared/Shared.fs`, `src/Client/Pages/GameDetail/{Types,State,Views}.fs`,
`tests/Server.Tests/{GamesTests,GameFacetProjectionTests,MetadataCacheTests}.fs`,
`.agentheim/contexts/games/README.md`. No new ADR written — this task executes decisions
already recorded in ADR-0043/0045/0048/0053/0054; its own implementation choices (the
identity-card writer's read-modify-write echo pattern, the seed split) follow established
precedent (`upsertGameFacets`'s slice discipline, the series `try/with` idiom) closely
enough that a new ADR wasn't warranted.

## Outcome (iteration 2 — verification fix-up, 2026-08-04)

The verifier's iteration-1 finding was correct: `Genres` fails ADR-0043's re-derivability test in
this codebase specifically — RAWG genre search runs exactly once, at creation time, and none of the
18 converted Steam emission sites ever re-fetches it (`updateGameIdentityCache`'s genres slot was
always `None`). Building a real ongoing genre-refresh mechanism to retroactively justify the cache
move was judged out of scope for a fix-up iteration (see ADR-0055's "why not" section). Reverted:
`game_list.genres`/`game_detail.genres` are restored as event-carried projection columns, written by
`Game_added_to_library`'s `handleEvent` arm exactly as before this task; `dropDeprecatedColumns` no
longer names `genres`; `copyGenresToMetadataCache` and its `Composition.buildApp` call site are
deleted (the migration it performed is no longer needed); `getAll`/`getBySlug`/
`getRecentlyAddedGames`/`getGameGenreDistribution` read `genres` straight off the projection tables
again. `MetadataCache.GameIdentityCard` narrows from four fields to three
(`Description`/`ShortDescription`/`WebsiteUrl`) — genres is no longer part of the cache slice at all;
`game_metadata_cache.genres` (shipped unpopulated by `games-a7dqx`) is kept but permanently unused.
Every other part of iteration 1's diff — the description/short_description/website_url cache
cutover, the seven other demoted event groups, `PlayFacets`/`PlayFacetsOverride` DTO wiring, the
client `PlayModePicker` deletion — is unchanged.

ADR-0055 (amending ADR-0043) records the decision, including why route (a) (an ADR asserting genres
is durable without a real mechanism) was rejected in favor of this narrower revert, and updates the
BC README's "Identity card" entry to match ADR-0043's actual meaning of the term (a new "Metadata
cache slice" entry now names the three fields that genuinely moved to the cache).

Two tests removed (the `copyGenresToMetadataCache` migration tests — the migration itself is gone);
one test rewritten to prove genres survives a `Projection.rebuildProjection` round-trip instead of
proving a cache read; the demoted-events-replay test's assertion corrected (`Game_categorized`'s
no-op leaves `Genres` at `Game_added_to_library`'s payload value, not empty). 630/630 tests pass;
`npm run build` (Fable compile gate) passes.

Key files changed this iteration: `src/Server/GameProjection.fs`, `src/Server/MetadataCache.fs`,
`src/Server/Api.fs`, `src/Server/PlaytimeTracker.fs`, `src/Server/Composition.fs`,
`tests/Server.Tests/GameFacetProjectionTests.fs`, `tests/Server.Tests/MetadataCacheTests.fs`,
`.agentheim/contexts/games/README.md`,
`.agentheim/knowledge/decisions/0055-game-genres-stays-event-carried-identity-card.md`.

## Verifier note (iteration 1)

REASONS:
- Check 6b (honored related ADRs) — the diff contradicts `related_adrs: [0043]` and no superseding/amending ADR was written (`ADRS_WRITTEN: none`). ADR-0043's classification table states verbatim: "`name`, `year`, `poster_ref`/`cover_ref`, **`genres`** on Movie/Series/**Game** | **Cache — projection column, event-carried** | Rides in the `*_added_to_library` snapshot event; replay reproduces it deterministically. Passes the identity-card clause." This diff drops `game_list.genres` and `game_detail.genres` (`src/Server/GameProjection.fs:145`, `:149`), deletes the `genres` write from `GameProjection.handleEvent`'s `Game_added_to_library` arm, and re-sources `Genres` from `game_metadata_cache.genres` (`GameProjection.fs:584`, `:625`, `:892`, `:1030`) — a column no replay path ever writes. `GameAddedData.Genres` is still carried by every `Game_added_to_library` event (deliberately unchanged), so genres is now the one dropped field that is neither event-reproduced nor re-fetchable in practice.
- The same ADR-0043 section names this exact outcome as the defect the doctrine exists to prevent (its ADR-0012 retraction: "a projection rebuild silently losing metadata that should either be re-fetchable on demand (true cache, fine to lose) or **carried by an event (never lost)**"). No repopulation path survives the drop: `GameProjection.copyGenresToMetadataCache` (`GameProjection.fs:127-135`) is a one-time copy whose source column this task deletes, and every converted Steam-refresh call site passes `None` in `updateGameIdentityCache`'s genres slot (`src/Server/Api.fs:544`, `:546`, `:577`, `:579`, `:3518`, `:3520`, `:3661`, `:3663`, `:3665`) — only the two creation paths ever write genres. This lands immediately before `administration-z6ymt`, which this task `blocks` and whose Notes describe it performing ADR-0038's wipe-first event-log import.
- The house precedent runs the other way and is also in `related_adrs`: ADR-0048 states "**Identity-card fields stay on `series_list`/`series_detail`, read directly, never joined**: Name, Year, PosterRef, BackdropRef, **Genres**, …", and ADR-0051 resolved the analogous Series genres problem (a `Series_categorized` command with no live caller, drifted genres) by appending compensating `Series_categorized` events to keep genres event-carried — not by moving it to the cache tier. The departure from both is unrecorded.
- Check 6 (ADRs for decisions) — relatedly, the new `## Ubiquitous language` entry "**Identity card** (games-v4nqe) — description/short_description/website_url/genres, now cache-only" (`.agentheim/contexts/games/README.md:85-91`) inverts the meaning of "identity card" as ADR-0043 defines it (the clause naming fields that legitimately *remain* projection columns because an event carries them). Task-file/README narration is not a substitute for an ADR recording the change of meaning.

SUGGESTED_FIX: Write an ADR that amends/supersedes ADR-0043's `genres`-on-Game classification (id in its `supersedes`/amends field, ADR-0048/0051 named as the diverging precedent), stating explicitly how `Genres` survives a projection rebuild and an ADR-0038 wipe-first import now that no replay path writes it — or, if that cannot be justified, keep `genres` projection-sourced and event-carried and narrow the column-drop criterion accordingly. Everything else in the diff verified clean: `npm test` 632/632 pass (exit 0), `npm run build` passes (exit 0), the demoted-command grep pre-flight returns zero matches, and scope/README-sync checks pass.

ITERATION_HINT: likely-fixable

## Verifier note (iteration 2)

REASONS:
- Check 6b (honored related ADRs) — the diff contradicts ADR-0055, the ADR it wrote itself, at the one site that ADR's `## Decision` explicitly names. ADR-0055's "What changed back" bullet states the `Game_categorized` comment changes from "genres now cache-derived" to "genres stays sourced exclusively from `Game_added_to_library`'s payload." That change was executed at `src/Server/GameProjection.fs:278` but NOT at the aggregate's `evolve` arm, which still reads verbatim (`src/Server/Games.fs:251`): `| _, Game_categorized _ -> state // demoted (games-v4nqe, ADR-0043) — genres now cache-derived; legacy event, ignored`. The claim is false after this iteration, and it cites ADR-0043 as authority for a statement ADR-0043's own classification table contradicts. Half-reverted state in `Game_categorized`'s four-part disposition: behavior correct, in-source record of *why* is not.
- Same finding family, second site: `src/Server/MetadataCache.fs:475-477` — `upsertGameFacets`'s doc comment still reads "`genres` is deliberately not a parameter here — nothing populates it until games-v4nqe's creation-path cache-write exists". ADR-0055 decided that write will never exist and commits to the `genres` column being "kept, marked permanently unused". That marking was applied at the migration site (`MetadataCache.fs:511-520`) but not here.
- Nothing else found. Everything substantive verified clean this iteration: `npm test` 630/630 exit 0; `npm run build` exit 0; demoted-command grep zero; scope clean; `Administration.tableRegistry` unchanged; no live-DB access; the genres departure honestly recorded (task Outcome iteration 2 + ADR-0055, original criteria left intact); ADR-0055 well-formed (`amends: [0043]`); behavior consistent (createTables re-declares genres with defensive ALTER re-add, handleEvent writes both tables, dropDeprecatedColumns no longer names it, copyGenresToMetadataCache deleted, all genre readers on projection columns, GameIdentityCard narrowed to three fields, README "Identity card" matches ADR-0043). Drift-zero and rebuild criteria covered by passing tests.

SUGGESTED_FIX: Correct the two stale comments to match ADR-0055 — `src/Server/Games.fs:251` should say genres stays sourced exclusively from `Game_added_to_library`'s payload (citing ADR-0043/ADR-0055), not "genres now cache-derived"; `src/Server/MetadataCache.fs:475-477` should mark `game_metadata_cache.genres` permanently unused per ADR-0055 rather than promising a games-v4nqe creation-path writer. No behavioral change, no test change, and no ADR change is needed — re-run `npm test` and `npm run build` to confirm both still pass.

ITERATION_HINT: likely-fixable

## Outcome (iteration 3)

Fixed the two stale comments flagged in the iteration-2 verifier note, with no behavioral, test,
or ADR changes:

- `src/Server/Games.fs:251` — `Game_categorized`'s `evolve` arm comment now reads
  `// demoted (games-v4nqe, ADR-0043/ADR-0055) — genres stays sourced exclusively from
  Game_added_to_library's payload; legacy event, ignored`, matching the wording already applied
  at `GameProjection.fs:278` and ADR-0055's "What changed back" bullet.
- `src/Server/MetadataCache.fs:475-477` — `upsertGameFacets`'s doc comment now states ADR-0055
  decided the creation-path cache write will never exist and that `game_metadata_cache.genres`
  is kept but permanently unused, matching the phrasing at the migration site
  (`MetadataCache.fs:511-520`).

`npm test` (630/630 pass, exit 0) and `npm run build` (exit 0) both re-verified clean after the
edits.
