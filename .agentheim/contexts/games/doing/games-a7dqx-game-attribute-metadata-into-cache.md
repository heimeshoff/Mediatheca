---
id: games-a7dqx
title: Move Game attribute metadata into the cache and stop emitting it — 7668 Game_play_mode_added events are literally Steam Store category tags and make up 43% of the entire event log
status: doing
type: refactor
context: games
created: 2026-08-01
completed:
depends_on: [administration-c3nvp, games-w4tzc, design-system-001]
blocks: [administration-z6ymt, games-b8xnw]
tags: [games, metadata, cache, steam, rawg, hltb, event-log]
related_adrs: [0012, 0042, 0043, 0045, 0048, 0050, 0053]
related_research: []
prior_art: [administration-qk3f7]
---

## Why

Live event counts, out of 17,638 total:

- `Game_play_mode_added` — **7668 across 896 games** (8.6 each, up to 56 for No Man's Sky). These are
  literally `details.Categories` from the Steam Store API (`PlaytimeTracker.fs:550, 583-584`):
  "Single-player", "Multi-player", "PvP", "Full controller support".
- `Game_short_description_set` 133, `Game_description_set` 16, `Game_website_url_set` 61,
  `Game_hltb_hours_set` 34.
- `Game_steam_last_played_set` 160 — **redundant** once `Play_session_recorded` exists: "last played"
  is `MAX(date)`, which `GameProjection` **already computes that way** at lines 870, 877 and 902.
  Recommend dropping the column and deriving.

All fail the re-derivability test in `infrastructure-e4kwm` — they are a third party's description of
the work, re-fetchable at any time.

**The harm today is bloat and bad modeling, not broken determinism**: `GameProjection` is a pure
function of the log, so drift for Games is already 0. That is why this was captured to backlog, not
the now-slice.

## What

- Cache the demoted fields in `game_metadata_cache` (built by `administration-c3nvp`).
- Apply the same four-part tolerance rule as `series-r2xhv`: **codec kept** (so the Health tab,
  ADR-0029 NDJSON round-trips and the ADR-0032 composer stay intact), **aggregate arm becomes an
  explicit no-op**, **projection arm deleted and column dropped**, **command deleted so the compiler
  finds every emission site**.
- Cache-join in `GameProjection.getBySlug`'s DTO assembly (`GameProjection.fs:442-520`), exactly the way
  `resolveFriendRefs` already joins `friend_list` (lines 395-409) — **and in the list DTO assembly
  (`getAll`)**, since genres and the facet filters live on the list page too.
- **Write and stop-emitting must be one task** — stopping emission before the cache exists means new
  games get no play modes at all.
- **Scope note (refinement 2026-08-04):** the emission sites are NOT just `PlaytimeTracker.fs` —
  `Api.fs` carries the majority (Steam family-import flow ~427-582/660-677, `attachSteamToGameCore`
  ~1068, the Steam-sync/enrichment flow ~3500-3770, `setGameHltbHours`/`fetchHltbData`). All
  enumerated in the acceptance criteria so none are discovered mid-implementation.

## Play-mode model (ideation session 2026-08-04)

Analysis of the live DB (1024 games): 302 distinct play-mode strings, breaking into five buckets —
~14 canonical multiplayer/structure tags, Steam platform features (Family Sharing 864, Achievements
680, Cloud 581, Trading Cards 453, …), input/display hardware (controller support, Remote Play on
TV/Tablet/Phone, VR, HDR), Steam accessibility tags (~17), and **~250 localized duplicates** of all
of the above ("Семейный доступ" = "家庭共享" = "Aile Paylaşımı" = Family Sharing).

**Root cause of the localization mess:** `Steam.fs:554` fetches `appdetails` without `&l=english`,
and `decodeCategoryDescription` (`Steam.fs:149`) keeps only the localized `description`, discarding
the stable numeric category `id` Steam sends alongside it.

### Decisions (Marco, 2026-08-04)

1. **Raw tag strings die entirely.** Replace with typed facets, derived from Steam category **ids**
   (fetch with `&l=english` as belt-and-braces; verify the id→facet table against a sample fetch
   during implementation — do not trust a hardcoded list blind):

   ```fsharp
   type PlayFacets = {
       Solo: bool               // Single-player
       CoopCouch: bool          // Shared/Split Screen Co-op; also Co-op ∧ Shared/Split Screen
       CoopOnline: bool         // Online Co-op; LAN Co-op folds in here
       VersusCouch: bool        // Shared/Split Screen PvP; also PvP ∧ Shared/Split Screen
       VersusOnline: bool       // Online PvP; LAN PvP, Cross-Platform, MMO fold in here
       RemotePlayTogether: bool // one-copy couch game playable online
       Vr: VrSupport            // VrOnly | VrSupported | NoVr — kept per Marco
   }
   ```

2. **Umbrella tags without locality detail resolve to online** (bare "Co-op"/"Multi-player"/"PvP"
   → the online facet). Only 44 games are affected; couch badges stay trustworthy because they
   only light up on explicit split/shared-screen tags.

3. **Everything else is thrown away**: platform features, accessibility tags, controller support,
   Remote Play on TV/Tablet/Phone, HDR. Not cached, not displayed.

4. **UI:** the 302-value play-mode picker (`GameDetail/Views.fs:331`, `getAllPlayModes` API) is
   deleted. Game cards/detail show ~4 badges — Solo · Co-op · Versus · Couch — with the
   online/couch distinction as sub-label. Games list gains facet filters (e.g. couch co-op).

5. **Manual editing survives as facet toggles** (typed per-facet override controls replacing the
   picker) — needed for non-Steam games and for correcting Steam's data. Manual overrides win over
   refetch. Note the doctrine split (ADR-0043): Steam-derived facets are third-party cache, but a
   manual override is *Marco's judgment* — re-derivability fails, so overrides stay
   **event-sourced**. **ADR-0053** (minted at refinement, 2026-08-04) records the full pattern:
   one `Game_play_facets_overridden of PlayFacetsOverride` event carrying the whole all-`Option`
   override record (`None` = defer to cache, `Some v` = overrule — `Some NoVr` is a real
   statement), a pure `PlayFacets.merge` (override wins where set, cache fills the rest) composed
   at query time, aggregate stays cache-blind, clearing = sending `None`. See the ADR for payload
   shape, `decide`/`evolve` arms, and rejected alternatives.

6. **Steam Deck readiness** (Verified/Playable/Unsupported) — **split into follow-up task
   `games-b8xnw`** (refiner's scoping call, 2026-08-04): separate unofficial endpoint
   (`ajaxgetdeckappcompatibilityreport`), a new UI feature rather than part of this stop-emitting
   refactor, and it reuses the resumable throttled-backfill infrastructure this task builds.

### Resolved at refinement (2026-08-04)

- **`Game_categorized` (genres) → cache.** The `Categorize_game` command is dead code — zero call
  sites in `src/Client` (no genre-editing UI exists) and zero dispatch sites in `src/Server`
  outside `Games.fs`. Per Marco's recorded rule ("keep as event if the UI exposes genre editing;
  move to cache if not"), genres move to `game_metadata_cache`. Genres reach the projection only
  via the `Game_added_to_library` creation payload (`Api.fs:633, 3651`), so the creation code path
  writes them to the cache instead (see hazard 1 below). Note: this is deliberately *stronger*
  than ADR-0043's identity-card clause strictly requires — recorded here so a future reader
  doesn't misread it as a modeling error.
- **Override-control UX (Marco, 2026-08-04):** per-facet **Auto/On/Off segmented controls** —
  Auto shows the Steam-derived cached value (dimmed/annotated), On/Off are explicit overrides;
  VR gets the four-option variant Auto / No VR / Supported / VR only. Clearing an override =
  selecting Auto (sends `None` for that field; no extra event needed, per ADR-0053).

## Event disposition table (refinement 2026-08-04)

Four-part rule: **codec kept** / **aggregate `evolve` arm → explicit no-op** / **projection arm
deleted + column dropped** / **command deleted**.

| Event | Command | Disposition | Notes |
|---|---|---|---|
| `Game_play_mode_added` / `Game_play_mode_removed` | `Add_play_mode` / `Remove_play_mode` | Demoted, four-part rule | Superseded by `Game_play_facets_overridden`. `ActiveGame.PlayModes: Set<string>` deleted outright (legacy arms return `state`, matching the `Game_store_added` precedent). `game_detail.play_modes` dropped. |
| `Game_description_set` | `Set_description` | Demoted, four-part rule | Column also written by `Game_added_to_library`'s arm — see hazard 1. |
| `Game_short_description_set` | `Set_short_description` | Demoted, four-part rule | Same creation-carried caveat. |
| `Game_website_url_set` | `Set_website_url` | Demoted, four-part rule | Same creation-carried caveat. |
| `Game_hltb_hours_set` | `Set_hltb_hours` | Demoted, four-part rule | Not creation-carried. `setGameHltbHours` (`Api.fs:3001`) has zero client call sites — delete, don't convert. `fetchHltbData` (`Api.fs:4339`) becomes a cache write. No override event needed (hazard 5). |
| `Game_steam_last_played_set` | `Set_steam_last_played` | Demoted, **derived not cached** | Redundant with `game_play_session` (our own event-sourced history), not third-party cache. Column dropped; reads become `MAX(date)` subquery. |
| `Game_categorized` | `Categorize_game` | Demoted, four-part rule | Dead code (verified). `game_list.genres`/`game_detail.genres` dropped; cache gains `genres`, seeded from `game_detail` **before** the drop. `GameAddedData.Genres` payload unchanged. |

**Confirmed out of scope:** `Game_steam_library_date_set` stays evented (first-sighting fact Steam
cannot be re-queried for — passes ADR-0043 re-derivability). `Game_rawg_id_set` /
`Game_steam_app_id_set` stay evented (the *link* is our decision, per ADR-0043's boundary call).

## Cutover & refetch plan (Marco, 2026-08-04)

Three legs, two of which already have mechanisms:

1. **Purge the ~7668 existing `Game_play_mode_added` events** — already covered by
   `administration-z6ymt` (ADR-0038 wipe-first import; depends on this task; builder-gated,
   stays its own task — live-DB actions are builder/conductor-only).

2. **Initial facet backfill for existing games.** Verified 2026-08-04: every game with play modes
   has a `steam_app_id` (1019 refetchable, **zero** manually-tagged games without one), so a full
   refetch can rebuild everything and no manual-tag migration is needed. Mechanism:
   - `game_metadata_cache.fetched_at` is `NULL` for seeded-never-fetched rows by c3nvp's design —
     *"exactly the cohort a first refresh should prioritize."* The backfill is a throttled
     background job walking `fetched_at IS NULL` (Steam appdetails is rate-limited, ~200 req/5min
     unofficially; ~1019 games ≈ 30+ min — must be resumable, never a blocking startup step).
   - **No seeding of facets from the old raw strings** (Marco declined the instant-UI option):
     facet columns start empty at cutover and fill as the backfill walks the library. Play-mode
     badges are simply absent until a game's refetch lands.

3. **Future imports derive facets at import time.** The `PlaytimeTracker` import path
   (`PlaytimeTracker.fs:333-334`) stops emitting events and instead writes to
   `game_metadata_cache`: the derived facet columns **plus the raw Steam category ids** (a small
   JSON int array). Storing the ids is deliberate: if the id→facet mapping has a bug or grows a
   new category, re-deriving is a pure offline pass over the cache — not another 1000-game
   refetch against Steam's rate limit. Same at manual single-game refresh.

## Acceptance criteria

### Schema / migration
- [ ] `game_metadata_cache` gains (idempotent `ALTER TABLE ADD COLUMN`, matching `MetadataCache.initialize`'s existing try/with idiom): `genres TEXT`, `facet_solo INTEGER`, `facet_coop_couch INTEGER`, `facet_coop_online INTEGER`, `facet_versus_couch INTEGER`, `facet_versus_online INTEGER`, `facet_remote_play_together INTEGER`, `facet_vr TEXT`, `steam_category_ids TEXT` (JSON int array).
- [ ] `game_detail` gains 7 nullable columns `facet_override_solo` … `facet_override_vr`, same idempotent idiom as `GameProjection.createTables`'s existing migrations.
- [ ] `game_list.genres`, `game_list.hltb_hours`, `game_detail.description`, `game_detail.short_description`, `game_detail.website_url`, `game_detail.genres`, `game_detail.hltb_hours`, `game_detail.hltb_main_plus_hours`, `game_detail.hltb_completionist_hours`, `game_detail.play_modes`, `game_detail.steam_last_played` are dropped (`ALTER TABLE ... DROP COLUMN`, SQLite ≥3.35).
- [ ] A one-time step copies `game_detail.genres` → `game_metadata_cache.genres` for every row where the cache's `genres` is still unset, run and completed **before** the column-drop step in the same migration sequence — ordering is load-bearing, the source disappears once dropped.

### Four-part rule per event group
- [ ] `Game_play_mode_added`/`Game_play_mode_removed`, `Game_description_set`, `Game_short_description_set`, `Game_website_url_set`, `Game_hltb_hours_set`, `Game_steam_last_played_set`, `Game_categorized`: codec (`serialize`/`deserialize`/`handledEventTypes`) unchanged; `evolve` arms are explicit no-ops (old streams replay without error or corrupted state); `GameProjection.handleEvent` arms deleted; `Add_play_mode`, `Remove_play_mode`, `Set_description`, `Set_short_description`, `Set_website_url`, `Set_hltb_hours`, `Set_steam_last_played`, `Categorize_game` commands deleted from `GameCommand`.
- [ ] `Game_added_to_library`'s `GameAddedData` payload is **unchanged** (still carries `Description`/`ShortDescription`/`WebsiteUrl`/`Genres`); only its `GameProjection.handleEvent` arm stops writing those four fields into the (now-dropped) projection columns.
- [ ] The creation code paths (`PlaytimeTracker.createGameFromSteam`, and any `Api.fs` game-creation flow) write description/short_description/website_url/genres directly into `game_metadata_cache` immediately after `Add_game` succeeds — the values are already known at creation time, so the cache row is not left NULL until a later refetch (applies only to these four fields, not to play facets, which deliberately start empty at cutover per decision 3).

### Facet derivation
- [ ] `Steam.decodeCategoryDescription` (or its replacement) decodes both `id: int` and `description: string` from each Steam category object.
- [ ] The `appdetails` fetch URLs (`Steam.fs:554`, `:610`, `:641`, and the `PlaytimeTracker`/`Api.fs` call sites) append `&l=english`.
- [ ] A pure, unit-tested `deriveFacets: int list -> PlayFacets` implements the id→facet table (Solo / CoopCouch / CoopOnline / VersusCouch / VersusOnline / RemotePlayTogether / Vr) including the umbrella-resolves-to-online rule for bare "Co-op"/"Multi-player"/"PvP".
- [ ] The id→facet table is verified against a live sample fetch during implementation (not shipped from an unverified guess) — worker records which ids were observed and matched, per decision 1.
- [ ] `steam_category_ids` (raw ids) is stored in `game_metadata_cache` on every fetch, so a mapping bug is a pure offline re-derive pass, never a re-fetch against Steam's rate limit.

### Manual override (ADR-0053)
- [ ] `Game_play_facets_overridden of PlayFacetsOverride` event and `Override_play_facets` command per ADR-0053: all-`Option` record, equality-checked no-op in `decide`, `ActiveGame.PlayFacetsOverride` replaces `PlayModes: Set<string>`.
- [ ] `PlayFacets.merge` is a pure function (override wins where set, cache fills the rest), unit-tested, composed at query time — never inside a `ProjectionHandler` (ADR-0045).
- [ ] A Steam refetch writes only the cache tier — no "don't clobber overrides" guard exists anywhere (structurally unnecessary per ADR-0053; adding one would imply the tiers can collide).

### Import / refresh paths write cache, not events
- [ ] `PlaytimeTracker.createGameFromSteam` (`PlaytimeTracker.fs:259-343`) stops calling `Add_play_mode`/`Set_steam_last_played`; writes derived facets, category ids, description, short_description, website_url, genres to `game_metadata_cache`.
- [ ] `Api.fs`'s Steam family-import flow (the blocks around `Api.fs:427-535`, `:550-582`, `:660-677` — `Set_steam_library_date` stays; `Set_short_description`/`Set_website_url`/`Add_play_mode` convert) writes cache directly.
- [ ] `Api.fs:attachSteamToGameCore` (~line 1068) stops calling `Set_description`/`Set_short_description`/`Set_website_url`/`Add_play_mode`; preserves its "only fill if currently empty" guard by reading `game_metadata_cache` instead of the dropped projection columns.
- [ ] `Api.fs`'s Steam-sync/enrichment flow (~lines 3500-3770, including `findGamesWithEmptyDescriptionAndSteamAppId`'s existing throttled backfill loop at `Async.Sleep 300`) converts the same way; `findGamesWithEmptyDescriptionAndSteamAppId` (`GameProjection.fs:652`) is rewritten to query `game_metadata_cache` for empty description, or explicitly retired in favor of the new facet-backfill job if the worker judges the two redundant (state which was chosen).
- [ ] `setGameHltbHours` (`Api.fs:3001`, verified zero client call sites) is deleted with `Set_hltb_hours`; `fetchHltbData` (`Api.fs:4339`) writes `game_metadata_cache` directly.
- [ ] `grep -rn "Games\.\(Add_play_mode\|Remove_play_mode\|Set_description\|Set_short_description\|Set_website_url\|Set_hltb_hours\|Set_steam_last_played\|Categorize_game\)" src/Server` returns zero matches once the commands are removed (pre-flight; the compiler enforces it structurally after removal from the DU).

### Resumable throttled backfill job
- [ ] A background job (same shape as existing `ScheduledJobs`/`Administration` job infrastructure — may adapt the existing `Async.Sleep 300` throttle pattern already used in `Api.fs`'s description backfill) walks `game_metadata_cache WHERE fetched_at IS NULL`, is never a blocking startup step, and is naturally resumable (successfully-fetched rows get `fetched_at` set, so the `WHERE` clause itself is the resume cursor — no separate cursor table needed).
- [ ] No seeding of facet columns from old raw play-mode strings at cutover — facets start empty/false and fill only as the backfill walks the library (decision 3, explicit).

### UI
- [ ] `GameDetail/Views.fs`'s `PlayModePicker` (~line 331) and its call site (~1926-1929), `GameDetail/Types.fs`'s `ShowPlayModePicker`/`AllPlayModes`, and `GameDetail/State.fs`'s `getAllPlayModes` dispatch (~58, ~121) are deleted.
- [ ] `Shared.fs`'s `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` are replaced by one `overrideGamePlayFacets: string -> PlayFacetsOverride -> Async<Result<unit, string>>`.
- [ ] Game cards and the detail page render up to 4 badges — Solo · Co-op · Versus · Couch — with online/couch sub-labels, from the merged `PlayFacets`. [human-eye]
- [ ] The games list gains facet filters (at least couch co-op) backed by the merge-rule `COALESCE(d.facet_override_x, c.facet_x, 0)` (a code comment distinguishes this merge-rule COALESCE from the staleness-masking COALESCE ADR-0048 rejected). [human-eye]
- [ ] The detail page exposes per-facet **Auto/On/Off segmented controls** (VR: Auto / No VR / Supported / VR only; Auto displays the Steam-derived cached value) — Marco's UX decision, refinement 2026-08-04. Controls render merged values but POST the override record, never the merged record (the ADR-0053 correctness trap).

### Derived last-played
- [ ] `GameProjection.getBySlug`'s `SteamLastPlayed` (and `GameDetail.SteamLastPlayed`) computes `(SELECT MAX(date) FROM game_play_session WHERE game_slug = @slug)`; `None` for a game whose only history is dateless `Prior_play_time_recorded` (accepted, pre-existing gap).
- [ ] `getGamesCompletedPerYear`/`getGamesBeatenThisYear` drop the `COALESCE(..., gd.steam_last_played)` fallback (the column is gone) — plain `MAX(date)` over `game_play_session`.

### Drift / rebuild / DTOs
- [ ] `checkProjectionDrift` (ADR-0031) stays zero for `GameProjection` after the column drops.
- [ ] A full `Drop; Init; replay` rebuild reproduces `game_list`/`game_detail` correctly; the demoted event types (`Game_categorized` included) still deserialize and their no-op `evolve` arms don't corrupt replay of pre-cutover streams.
- [ ] Every reader of a dropped column is updated: at minimum `getAll`, `getBySlug`, `getRecentlyAddedGames`, `getGamesRecentlyPlayed`, `getBacklogStats`, `getInFocusEstimate`, `getHltbComparisons`, `getGameGenreDistribution`, `getGamesCompletedPerYear`, `getGamesBeatenThisYear` in `GameProjection.fs` gain the `game_metadata_cache` join or `game_play_session` subquery in place of the dropped column.
- [ ] `Shared.fs`'s `GameListItem`/`GameDetail`: `PlayModes: string list` → `PlayFacets: PlayFacets` (+ `PlayFacetsOverride` on `GameDetail`, so the client can render merged values while posting overrides); `Genres`/`HltbHours` field shapes unchanged, only re-sourced.
- [ ] `Administration.tableRegistry` classifications for `game_list`/`game_detail` (Projected) and `game_metadata_cache` (Cache) are unchanged — column sets differ, classifications don't.
- [ ] `npm run build` passes (Fable compile gate) after the client-side deletions.

## Notes

**Sequencing constraint worth honouring:** this must not be scheduled *ahead* of the vision's Steam
Import Enhancement or HowLongToBeat Integration items — but both of those should land *after* it, or
they pour thousands more junk events into a log already 43% play-mode tags, making
`administration-z6ymt` larger.

**Hazards recorded at refinement (2026-08-04):**

1. **Identity-card write conflict:** `description`, `short_description`, `website_url`, `genres`
   are written by *two* sources today — `Game_added_to_library` (creation) and the demoted `Set_*`
   commands. Once the columns drop, the creation event's projection arm must also stop writing
   them, and the *creation code path itself* (not the `ProjectionHandler`, per ADR-0045) writes
   them into `game_metadata_cache`. A necessary consequence of the column drop, not a separate
   design choice — the payload schema of `GameAddedData` stays untouched.
2. **Diff surface is much larger than "the PlaytimeTracker import path"** — `Api.fs` carries most
   emission sites (family import, `attachSteamToGameCore`, sync/enrichment flow, HLTB endpoints).
   All enumerated in the acceptance criteria. If the worker finds the combined diff unmanageable
   mid-implementation, bouncing back to backlog with a note is preferable to a rushed partial
   cutover (the Series BC did this as two tasks: `series-q8jwc` read-composition, then
   `series-d5tpn` column-drop; Marco's explicit instruction keeps this one task).
3. **`Steam.decodeCategoryDescription` currently discards the numeric `id`** (`Steam.fs:149-152`)
   — the decoder itself changes shape, not just the caller.
4. **`setGameHltbHours` is dead code** (zero client call sites) — delete alongside
   `Set_hltb_hours`, don't convert.
5. **HLTB needs no override event** — verified no UI path exists for hand-typed HLTB hours; both
   call sites are third-party-fetch flows. Clean cache move; don't over-generalize the
   `PlayFacets` override pattern here.

**ADR-0053** (`0053-game-play-facets-cache-derived-event-sourced-override.md`) carries the full
`PlayFacets`/`PlayFacetsOverride` type sketches, `decide`/`evolve` arms, merge function, and
rejected alternatives — the worker implements from it, not from a paraphrase.
