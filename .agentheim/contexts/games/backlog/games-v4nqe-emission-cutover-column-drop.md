---
id: games-v4nqe
title: Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)
status: backlog
type: refactor
context: games
created: 2026-08-04
completed:
depends_on: [games-a7dqx]
blocks: [administration-z6ymt, games-j6wkr]
tags: [games, metadata, cache, steam, event-log, migration]
related_adrs: [0043, 0045, 0048, 0053, 0054]
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
