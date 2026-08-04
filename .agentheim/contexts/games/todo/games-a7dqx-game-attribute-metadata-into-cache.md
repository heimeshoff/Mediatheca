---
id: games-a7dqx
title: Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI)
status: todo
type: refactor
context: games
created: 2026-08-01
completed:
depends_on: [administration-c3nvp, games-w4tzc]
blocks: [games-v4nqe, games-b8xnw]
tags: [games, metadata, cache, steam, event-log]
related_adrs: [0012, 0042, 0043, 0045, 0048, 0050, 0053]
related_research: []
prior_art: [administration-qk3f7]
---

## Why

Live event counts, out of 17,638 total: `Game_play_mode_added` — **7668 across 896 games** (8.6
each, up to 56 for No Man's Sky). These are literally `details.Categories` from the Steam Store
API (`PlaytimeTracker.fs:550, 583-584`): "Single-player", "Multi-player", "PvP", "Full controller
support". Plus `Game_description_set` 133, `Game_short_description_set` 16, `Game_website_url_set`
61, `Game_hltb_hours_set` 34, `Game_steam_last_played_set` 160 (redundant once
`Play_session_recorded` exists — `MAX(date)` is already how `GameProjection` computes "last
played" elsewhere), and `Game_categorized` (genres, dead code — zero UI call sites). All fail the
re-derivability test in `infrastructure-e4kwm` — third-party facts, re-fetchable at any time. The
harm today is bloat and bad modeling, not broken determinism (`GameProjection` is a pure function
of the log; drift is already 0).

**This task was originally scoped as one refactor covering the whole cutover.** A worker bounced
it after a full read-only file survey confirmed the combined diff (new domain types, schema
migration with load-bearing drop ordering, ~10 projection query rewrites, a live-verified facet
derivation function, ~18 `Api.fs`/`PlaytimeTracker` call-site conversions across 5 structurally
distinct flows, a new background job, and a full client UI rewrite) is unmanageable for one
worker pass — see hazard 2 in the original file (superseded by this split; retained history, not
re-litigated here). Marco approved splitting along the seam the bounce note recommended, refined
during split-planning to resolve one coupling problem the naive three-way split didn't fully work
out: `Shared.fs`'s `GameListItem`/`GameDetail.PlayModes: string list` field cannot become
`PlayFacets` and simultaneously have its backing `game_detail.play_modes` column dropped without
either (a) breaking client compilation or (b) leaving the DTO change straddling two tasks. This
task (**split 1 of 3**) is scoped to be **strictly additive and safe**: it changes no existing
Shared DTO field, drops no column, deletes no command, and stops no emission. Everything in this
task can land and the app keeps compiling, testing, and booting exactly as before, with new
capability sitting alongside the old, unused by anything yet.

- **games-v4nqe** (split 2 of 3) converts every emission site, deletes the demoted commands, drops
  the columns, and finalizes the `Shared.fs` DTO change — using the domain model, derivation
  function, and cache schema this task builds.
- **games-j6wkr** (split 3 of 3) rewrites the client UI (picker deletion, badges, Auto/On/Off
  controls, list filters) against games-v4nqe's finalized contract.

The event disposition table (which event/command gets which treatment) is games-v4nqe's document —
that task is where the four-part rule (codec kept / evolve arm → no-op / projection arm deleted /
command deleted) actually executes. This task only needs to know that a manual-override
counterpart, `Game_play_facets_overridden`, is being added net-new alongside the existing
`Game_play_mode_added`/`Game_play_mode_removed` (which this task does not touch).

## Play-mode model (ideation session 2026-08-04) — the target shape this task's derivation builds toward

Analysis of the live DB (1024 games): 302 distinct play-mode strings, breaking into five buckets —
~14 canonical multiplayer/structure tags, Steam platform features (Family Sharing 864,
Achievements 680, Cloud 581, Trading Cards 453, …), input/display hardware (controller support,
Remote Play on TV/Tablet/Phone, VR, HDR), Steam accessibility tags (~17), and **~250 localized
duplicates** of all of the above ("Семейный доступ" = "家庭共享" = "Aile Paylaşımı" = Family
Sharing).

**Root cause of the localization mess:** `Steam.fs:554` fetches `appdetails` without
`&l=english`, and `decodeCategoryDescription` (`Steam.fs:149`) keeps only the localized
`description`, discarding the stable numeric category `id` Steam sends alongside it.

### Decisions (Marco, 2026-08-04) — the ones this task implements

1. **Raw tag strings die entirely.** Replace with typed facets, derived from Steam category
   **ids** (fetch with `&l=english` as belt-and-braces; verify the id→facet table against a
   sample fetch during implementation — do not trust a hardcoded list blind):

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
   → the online facet). Only 44 games affected; couch badges stay trustworthy because they only
   light up on explicit split/shared-screen tags.

3. **Everything else is thrown away**: platform features, accessibility tags, controller support,
   Remote Play on TV/Tablet/Phone, HDR. Not cached, not displayed.

5. **Manual editing survives as facet toggles** — needed for non-Steam games and for correcting
   Steam's data. Manual overrides win over refetch. ADR-0053 records the full pattern: one
   `Game_play_facets_overridden of PlayFacetsOverride` event carrying the whole all-`Option`
   override record (`None` = defer to cache, `Some v` = overrule — `Some NoVr` is a real
   statement), a pure `PlayFacets.merge` (override wins where set, cache fills the rest) composed
   at query time, aggregate stays cache-blind, clearing = sending `None`. This task builds the
   event, command, aggregate state, and merge function; games-v4nqe/games-j6wkr wire it into the public
   DTO and UI. See ADR-0053 for the full type sketches, `decide`/`evolve` arms, and rejected
   alternatives.

6. **Steam Deck readiness** — split into follow-up task `games-b8xnw`, which `depends_on` this
   task specifically because it reuses the resumable throttled-backfill job this task builds.
   Unaffected by this split.

(Decision 4, the UI shape, belongs to games-j6wkr — not reproduced here.)

## What

### Schema (additive only — no drops, no renames)

- `game_metadata_cache` gains (idempotent `ALTER TABLE ADD COLUMN`, matching
  `MetadataCache.initialize`'s existing try/with idiom): `genres TEXT`, `facet_solo INTEGER`,
  `facet_coop_couch INTEGER`, `facet_coop_online INTEGER`, `facet_versus_couch INTEGER`,
  `facet_versus_online INTEGER`, `facet_remote_play_together INTEGER`, `facet_vr TEXT`,
  `steam_category_ids TEXT` (JSON int array). The function's shape mirrors
  `upsertSeriesMetadata` (`MetadataCache.fs`) — a new `upsertGameMetadata`/facet-write function.
  Note: this upsert function may support writing `genres` from day one (mirroring the general
  shape), but nothing calls it with a genres value until games-v4nqe — genres has no data source
  this task can safely populate (see "What this task deliberately does NOT do," below).
- `game_detail` gains 7 nullable columns `facet_override_solo` … `facet_override_vr`, same
  idempotent idiom as `GameProjection.createTables`'s existing migrations. These are written by
  this task's new `GameProjection.handleEvent` arm for `Game_play_facets_overridden` — a
  `Projected`-tier write (`game_detail` is `Projected`-classified), not a cache write, so ADR-0045's
  "no `ProjectionHandler` touches the cache tier" constraint is respected: only
  `game_metadata_cache` is the cache tier here, and nothing in this task writes to it from a
  `ProjectionHandler`.

### Domain (ADR-0053, server-internal — no `Shared.fs` DTO field renamed or removed)

- `Game_play_facets_overridden of PlayFacetsOverride` event and `Override_play_facets` command
  per ADR-0053: all-`Option` record, equality-checked no-op in `decide`. `ActiveGame`'s in-memory
  aggregate state gains `PlayFacetsOverride: PlayFacetsOverride`. Replacing
  `ActiveGame.PlayModes: Set<string>` is explicitly **out of scope for this task**: that field is
  still written by the still-live `Game_play_mode_added`/`Game_play_mode_removed` events, which
  this task does not touch. (Re-read: ADR-0053's phrase "`ActiveGame.PlayFacetsOverride` replaces
  `PlayModes: Set<string>`" describes the *end state* after games-v4nqe deletes the old
  add/remove-play-mode commands and their aggregate field. This task adds the new field
  alongside the old one; games-v4nqe removes the old one.)
- The aggregate stays cache-blind by construction (ADR-0053) — no read path into
  `game_metadata_cache` from `decide`.

### Shared.fs additions — new types and one new API method only, nothing existing changed

- New types: `PlayFacets`, `PlayFacetsOverride`, `VrSupport`. Purely additive.
- New API method `overrideGamePlayFacets: string -> PlayFacetsOverride -> Async<Result<unit, string>>`
  dispatching `Override_play_facets`. Purely additive — nothing currently calls it, so no client
  code is affected. `GameListItem`/`GameDetail`'s existing `PlayModes: string list` field, and the
  existing `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` methods, are **untouched by
  this task** — that rename/removal is games-v4nqe's job, entirely, so the compile-coupled DTO
  change lives in exactly one task rather than straddling this one and the next.

### Facet derivation

- `Steam.decodeCategoryDescription` (or its replacement) decodes both `id: int` and
  `description: string` from each Steam category object (currently discards `id` —
  `Steam.fs:149-152`).
- The `appdetails` fetch URLs (`Steam.fs:554`, `:610`, `:641`, `:810`) append `&l=english`.
- A pure, unit-tested `deriveFacets: int list -> PlayFacets` implements the id→facet table (Solo /
  CoopCouch / CoopOnline / VersusCouch / VersusOnline / RemotePlayTogether / Vr) including the
  umbrella-resolves-to-online rule for bare "Co-op"/"Multi-player"/"PvP".
- `PlayFacets.merge : PlayFacets -> PlayFacetsOverride -> PlayFacets` — pure, unit-tested (override
  wins where set, cache fills the rest). A `GameProjection` helper composes it from
  `game_metadata_cache` + `game_detail`'s new override columns for a given game (unit/integration
  tested) — **not yet wired into the public `getAll`/`getBySlug` DTO assembly**; that wiring, and
  the corresponding `GameListItem`/`GameDetail.PlayFacets` field, is games-v4nqe's job, because it's
  inseparable from the DTO rename it also owns.

### Safe read-composition switches (field shape unchanged — not compile-coupled)

These fields are **already seeded** in `game_metadata_cache` by `administration-c3nvp`
(description/short_description/website_url/hltb hours), or are independently derivable from our
own event-sourced history (steam_last_played). Switching their read source in `GameProjection.fs`
changes no `Shared.fs` field name or shape, so it is safe to do now, ahead of games-v4nqe's write
cutover — this mirrors the honest-degradation stance ADR-0048/`series-q8jwc` took (straight
nullable reads from the cache, no `COALESCE` fallback to the old projection column, so a genuine
cache miss shows `None`/`""` rather than a frozen, silently-wrong value):

- `getBySlug`'s `SteamLastPlayed` (and `GameDetail.SteamLastPlayed`) computes
  `(SELECT MAX(date) FROM game_play_session WHERE game_slug = @slug)`; `None` for a game whose
  only history is dateless `Prior_play_time_recorded` (accepted, pre-existing gap).
- `getGamesCompletedPerYear`/`getGamesBeatenThisYear` drop the
  `COALESCE(..., gd.steam_last_played)` fallback — plain `MAX(date)` over `game_play_session`.
- Every reader touching `description`/`short_description`/`website_url`/`hltb_hours`/
  `hltb_main_plus_hours`/`hltb_completionist_hours` — at minimum `getAll`, `getBySlug`, and
  whichever of `getRecentlyAddedGames`, `getGamesRecentlyPlayed`, `getBacklogStats`,
  `getInFocusEstimate`, `getHltbComparisons` actually reference these fields (grep to confirm; not
  every one of the ten readers named in the original survey touches every field) — switch to
  `game_metadata_cache` reads, straight nullable, no fallback.
- **`genres` and the play-facet fields are explicitly NOT switched in this task** — see next
  section.

### What this task deliberately does NOT do (and why)

- **Does not touch `genres`.** `game_detail.genres` is the only populated source right now;
  `game_metadata_cache.genres` starts empty and its only planned fill mechanisms — the one-time
  copy-before-drop step, and the creation-path cache-write — are both games-v4nqe's work (the copy
  step must run immediately before the column drop in the same migration sequence, which is
  games-v4nqe's migration, not this task's). Switching `getAll`/`getBySlug`/
  `getGameGenreDistribution` to read `game_metadata_cache.genres` in this task, before that column
  has any data, would make every game's genres display go blank the moment this task deploys —
  an unforced, avoidable regression. Left on `game_detail.genres`, unread by this task.
- **Does not switch reads to the new `PlayFacets`/`PlayFacetsOverride` DTO fields** (they don't
  exist on `GameListItem`/`GameDetail` yet) or drop `game_detail.play_modes` — same reasoning:
  this task's own backfill job (below) starts most games' facet cache columns empty (decision 3,
  deliberate — no seeding from the old raw strings), and the old `Game_play_mode_added`/`removed`
  commands are still live and still the only thing populating anything play-mode-related that the
  *current* UI reads. Badges/picker keep working off the untouched old column until games-v4nqe
  cuts over.
- **Does not delete any command, drop any column, or touch `Api.fs`/`PlaytimeTracker`'s emission
  call sites.** All 18 of those (family import ~427-582/660-677, `attachSteamToGameCore` ~1068,
  sync/enrichment ~3500-3770, HLTB endpoints ~3001/4339) are untouched — games-v4nqe's diff.
- **Does not touch the client** (`GameDetail/Views.fs`, `Types.fs`, `State.fs`). No `[human-eye]`
  criteria in this task.

### Resumable throttled backfill job

- A background job (same shape as existing `ScheduledJobs`/`Administration` job infrastructure —
  may adapt the existing `Async.Sleep 300` throttle pattern already used in `Api.fs`'s description
  backfill) walks `game_metadata_cache WHERE fetched_at IS NULL`, fetches Steam `appdetails` (with
  `&l=english`), derives facets via `deriveFacets`, and writes the facet columns +
  `steam_category_ids` to `game_metadata_cache`. Never a blocking startup step. Naturally resumable
  — successfully-fetched rows get `fetched_at` set, so the `WHERE` clause is the resume cursor, no
  separate cursor table needed. ~1019 refetchable games, Steam's unofficial rate limit
  (~200 req/5min) implies ~30+ min to walk the library.
- This job writes only `game_metadata_cache` — never `game_detail`'s override columns. No "don't
  clobber overrides" guard exists or is needed (structurally impossible for this job to touch the
  override tier, per ADR-0053).
- No seeding of facet columns from old raw play-mode strings — facets start empty/false and fill
  only as the backfill walks the library (decision 3, explicit). Because nothing reads these
  columns yet (see "does NOT do," above), this is entirely invisible until games-j6wkr's UI exists
  — but the job should run starting from this task's deployment, so that by the time games-j6wkr
  ships, most of the library is already populated.

## Transitional state after this task lands (name it, don't let it surprise anyone)

- **Old play-mode system is completely unaffected.** `Game_play_mode_added`/`removed` still fire,
  `game_detail.play_modes` still populates, the picker and badges (whatever the client currently
  renders) work exactly as before this task.
- **New play-facets system exists in parallel, running but unobserved.** The backfill job starts
  populating `game_metadata_cache`'s facet columns; the override event/command/API exist and are
  independently testable (e.g. via a direct RPC call or integration test), but nothing in the
  live UI reads or writes through them yet.
- **Description/website/HLTB/steam-last-played reads are now cache/derived-sourced.** Since these
  are already seeded (or independently computable), this is invisible under normal operation. The
  one exception: if any *new* `Set_description`/`Set_short_description`/`Set_website_url`/
  `Set_hltb_hours`/`Set_steam_last_played` command fires between this task landing and games-v4nqe
  converting its call site, the write still lands on the old (now-unread) `game_detail` column and
  will not appear in reads until games-v4nqe converts that call site to write the cache directly.
  This is the same class of gap ADR-0048 named for `series-r2xhv`→`series-q8jwc` (a temporary
  write/read tier mismatch), just mirrored — there, writes moved to the cache first and reads
  stayed on the stale column; here, reads move to the cache first and writes stay on the (about to
  be dropped) column. Both produce the same visible effect: a frozen value until the next task in
  the chain lands. **Bounded by games-v4nqe, the immediate next task.**

## Acceptance criteria

### Schema
- [ ] `game_metadata_cache` gains `genres TEXT`, `facet_solo INTEGER`, `facet_coop_couch INTEGER`,
      `facet_coop_online INTEGER`, `facet_versus_couch INTEGER`, `facet_versus_online INTEGER`,
      `facet_remote_play_together INTEGER`, `facet_vr TEXT`, `steam_category_ids TEXT` — idempotent
      `ALTER TABLE ADD COLUMN`, matching `MetadataCache.initialize`'s try/with idiom.
- [ ] `game_detail` gains 7 nullable `facet_override_solo` … `facet_override_vr` columns, same
      idempotent idiom as `GameProjection.createTables`'s existing migrations.
- [ ] No existing column is dropped, renamed, or stops being written by this task.

### Domain (ADR-0053)
- [ ] `Game_play_facets_overridden of PlayFacetsOverride` event and `Override_play_facets` command
      exist per ADR-0053: all-`Option` record, equality-checked no-op in `decide`.
- [ ] `ActiveGame` gains `PlayFacetsOverride: PlayFacetsOverride`; `ActiveGame.PlayModes: Set<string>`
      is untouched (still written by the still-live `Add_play_mode`/`Remove_play_mode`).
- [ ] `GameProjection.handleEvent`'s new arm for `Game_play_facets_overridden` writes the 7
      `facet_override_*` columns on `game_detail` (a `Projected`-tier write, per ADR-0045).
- [ ] The aggregate has no read path into `game_metadata_cache` (cache-blind by construction).

### Shared.fs / API (additive only)
- [ ] `PlayFacets`, `PlayFacetsOverride`, `VrSupport` types added to `Shared.fs`.
- [ ] `overrideGamePlayFacets: string -> PlayFacetsOverride -> Async<Result<unit, string>>` added
      to `IMediathecaApi` and implemented server-side, dispatching `Override_play_facets`.
- [ ] `GameListItem.PlayModes: string list` / `GameDetail.PlayModes: string list`,
      `getAllPlayModes`, `addGamePlayMode`, `removeGamePlayMode` are byte-identical to before this
      task (`git diff` on `Shared.fs` shows only additions, no changed lines in the existing
      members).
- [ ] `npm run build` passes unchanged (no client file is touched by this task).

### Facet derivation
- [ ] `Steam.decodeCategoryDescription` (or its replacement) decodes both `id: int` and
      `description: string` from each Steam category object.
- [ ] The `appdetails` fetch URLs (`Steam.fs:554`, `:610`, `:641`, `:810`) append `&l=english`.
- [ ] A pure, unit-tested `deriveFacets: int list -> PlayFacets` implements the id→facet table
      including the umbrella-resolves-to-online rule.
- [ ] The id→facet table is verified against a live sample fetch during implementation (not
      shipped from an unverified guess) — worker records which ids were observed and matched.
- [ ] `PlayFacets.merge` is a pure, unit-tested function (override wins where set, cache fills the
      rest). A `GameProjection` helper composes it from `game_metadata_cache` + `game_detail`'s
      override columns for a given slug (integration tested) — not yet wired into
      `getAll`/`getBySlug`'s public DTO assembly.

### Safe read-composition switches
- [ ] `getBySlug`'s `SteamLastPlayed` computes `MAX(date)` over `game_play_session`; `None` for a
      game whose only history is dateless prior playtime.
- [ ] `getGamesCompletedPerYear`/`getGamesBeatenThisYear` drop the `COALESCE(..., gd.steam_last_played)`
      fallback.
- [ ] Every reader touching description/short_description/website_url/hltb_hours* switches to
      `game_metadata_cache` reads, straight nullable, no fallback to the old projection column
      (honest-degradation stance, mirroring `series-q8jwc`/ADR-0048).
- [ ] `genres` and all play-facet fields are confirmed **untouched** — still sourced from
      `game_detail.genres` / `game_detail.play_modes` respectively (grep confirms no reader was
      switched to `game_metadata_cache.genres` or the facet columns).

### Resumable throttled backfill job
- [ ] A background job walks `game_metadata_cache WHERE fetched_at IS NULL`, fetches Steam
      `appdetails` with `&l=english`, derives facets, writes facet columns + `steam_category_ids`.
      Never a blocking startup step; resumable via the `WHERE` clause itself (no separate cursor
      table).
- [ ] The job writes only `game_metadata_cache` — never `game_detail`'s override columns.
- [ ] No seeding of facet columns from old raw play-mode strings — facets start empty/false and
      fill only as the backfill walks the library.

### Drift / rebuild
- [ ] `checkProjectionDrift` (ADR-0031) stays zero for `GameProjection` after this task's schema
      and read changes land (no existing write path is altered, only added).
- [ ] A full `Drop; Init; replay` rebuild reproduces `game_list`/`game_detail` correctly, including
      correctly reconstructing `ActiveGame.PlayFacetsOverride` from any `Game_play_facets_overridden`
      events present in the log (none will exist pre-deploy, but the codec/evolve arm must be
      exercised by a test, not just declared).

## Notes

**Fold-forward from the original file's worker survey (2026-08-04), scoped to this task:**
- `Games.fs` (854 lines) — `Game_store_added`/`Game_store_removed` already demonstrate the
  four-part no-op precedent (relevant to games-v4nqe, not this task, but confirmed present as a
  copyable pattern for later). This task's own change to `Games.fs` is additive only: new event,
  new command, new `ActiveGame` field, new `decide`/`evolve` arms — nothing existing is touched.
- `GameProjection.fs` (1018 lines) — full schema (`game_list`/`game_detail` DDL, the
  `ALTER TABLE ADD COLUMN` migration idiom to copy for the new columns), `handleEvent`, and ~30
  query functions. At least 10 readers touch columns slated for eventual drop by games-v4nqe
  (`getAll`, `getBySlug`, `getRecentlyAddedGames`, `getGamesRecentlyPlayed`, `getBacklogStats`,
  `getInFocusEstimate`, `getHltbComparisons`, `getGameGenreDistribution`,
  `getGamesCompletedPerYear`, `getGamesBeatenThisYear`) — this task converts each one's
  description/website/hltb/steam-last-played usage where present; games-v4nqe converts whatever's
  left (genres, play-facets).
- `MetadataCache.fs` (366 lines) — confirmed schema, the `ALTER TABLE ADD COLUMN` try/with idiom to
  copy for the 8 new facet/genre/category-id columns, and `upsertSeriesMetadata`'s shape to mirror
  for the new `upsertGameMetadata`/facet-write function this task adds.
- `Steam.fs` — confirmed `decodeCategoryDescription` (line 149) discards `id`, and 4 separate
  `appdetails` fetch URL sites (lines 554, 610, 641, 810) all need `&l=english` appended.

**Sequencing constraint worth honouring (unchanged from the original file):** this must not be
scheduled *ahead* of the vision's Steam Import Enhancement or HowLongToBeat Integration items —
this task doesn't stop any emission itself, so it doesn't materially change that constraint's
urgency, but games-v4nqe (which does stop emission) inherits it more sharply.

**Hazard 3 from the original file** ("`Steam.decodeCategoryDescription` currently discards the
numeric `id`") is this task's to fix — folded into the Facet derivation section above.

**ADR-0053** (`0053-game-play-facets-cache-derived-event-sourced-override.md`) carries the full
`PlayFacets`/`PlayFacetsOverride` type sketches, `decide`/`evolve` arms, merge function, and
rejected alternatives — the worker implements from it, not from a paraphrase.

**Relationship to `games-b8xnw`:** unaffected by this split — `games-b8xnw` continues to
`depends_on: [games-a7dqx, design-system-001]` unchanged, since the resumable backfill
infrastructure it needs is exactly what this task builds.
