---
id: games-fffvm
title: Re-sanitize existing game descriptions — a resumable, throttled backfill that re-fetches every already-cached game's description (Steam-linked via the storefront, RAWG-only via RAWG details) through games-r1tx4's sanitizer, so games imported before it gain paragraphs/emphasis and RAWG-only games get the description games-v4nqe silently dropped
status: done
type: chore
context: games
created: 2026-09-17
completed:
depends_on: [games-r1tx4]
blocks: []
tags: [games, steam, rawg, description, backfill, metadata-cache, integration]
related_adrs: [0043, 0045, 0081]
related_research: []
prior_art: [games-ev65k, games-b8xnw, games-a7dqx, games-v4nqe, games-r1tx4]
---

## Why

`games-r1tx4` (shipped 2026-09-17) sanitizes descriptions that arrive *after* it. Every row already in `game_metadata_cache.description` is old `stripHtmlTags` output — tagless and newline-less — so `RichText.render` shows it as one flat paragraph, and the visible payoff of r1tx4 never reaches the games the user actually owns. A second population is worse off: RAWG-added games with no Steam auto-attach have had an **empty** description since games-v4nqe dropped the projection column without adding a creation-path cache write. r1tx4 closed that going forward (its RAWG `addGame` path now writes the identity card), not retroactively — and those legacy RAWG-only games typically have **no `game_metadata_cache` row at all** (the v4nqe seed only created rows for games that existed at seed time).

The Steam library import's existing "backfill empty descriptions" loop (`GameProjection.findGamesWithEmptyDescriptionAndSteamAppId`, Api.fs) does not cover either population: it only fires during a manual import, only for Steam-linked games, and only when *both* description and short_description are empty — a flattened-but-present description never qualifies.

## What

A fourth instance of the games BC's resumable throttled backfill shape (`GameFacetBackfill` → `GameDeckCompatBackfill` → `GameReleaseDateBackfill`, ADR-0043/ADR-0045). `GameReleaseDateBackfill.fs` + `GameReleaseDateBackfillTests.fs` are the closest template; mirror their shape, don't invent.

### Marker column and the stamping rule

- Add `description_fetched_at TEXT` to `game_metadata_cache` via the same guarded, idempotent `ALTER TABLE … ADD COLUMN` block `MetadataCache.initialize` already uses for `deck_compat_fetched_at`/`release_date_fetched_at`. `NULL` = the description on this row (or the absence of a row) has never been written from sanitizer output. It is its **own** cursor, deliberately separate from `fetched_at`/`deck_compat_fetched_at`/`release_date_fetched_at` (same reasoning as those: independent jobs, independent endpoints).
- **Stamp exactly when a sanitizer-output description is written to the cache; never on a short_description/website_url-only refresh.** The sites that write a sanitized description today (all post-r1tx4):
  - `MetadataCache.upsertGameIdentityCard` calls on the five creation paths — `runSteamFamilyImport`'s new-game branch, `addGameFromSteamCore`, the Steam library import's new-game branch (all Api.fs), `addGame`'s RAWG path (Api.fs), and `PlaytimeTracker`'s scheduled-sync new-game path;
  - `attachSteamToGameCore` (Api.fs) — attaches Steam to an existing game and writes the card;
  - the Steam library import's empty-description loop — `updateGameIdentityCache conn slug (Some desc) None None` (Api.fs).
  The sites that must **not** stamp: every `updateGameIdentityCache conn slug None (Some shortDesc) None` / `… None None (Some websiteUrl)` call (`runSteamFamilyImport`'s `FullReenrich` branch and the Steam library import's re-enrich branch). Those leave a legacy flattened description in place, so the row must stay a candidate.
- Mechanism is the worker's call, with one constraint: `upsertGameIdentityCard` itself must stay stamp-free, because `updateGameIdentityCache` routes description-less refreshes through it. Two workable shapes: a separate `MetadataCache.stampDescriptionFetched conn slug` (`INSERT … ON CONFLICT(game_slug) DO UPDATE SET description_fetched_at = …`, so a game with no cache row yet gets one) called right after each sanitized write, or a `description: string option` parameter on the helper that stamps iff `Some`. Either way the stamp must survive a missing row.

### Candidates

`MetadataCache.findGamesNeedingDescriptionBackfill conn : (string * DescriptionSource) list` (or an equivalent tuple), where the source is `SteamApp of int | RawgGame of int`:

- `FROM game_detail gd LEFT JOIN game_metadata_cache mc ON mc.game_slug = gd.slug` — **left** join, because the RAWG-only legacy population has no cache row (mirror `findGamesWithEmptyDescriptionAndSteamAppId`, not `findGamesNeedingReleaseDateBackfill`'s inner join).
- `WHERE mc.description_fetched_at IS NULL` (true for a missing row too) `AND (gd.steam_app_id IS NOT NULL OR gd.rawg_id IS NOT NULL)`.
- Both ids come from **`game_detail`**. `game_metadata_cache.rawg_id` is a one-time seed copy from games-v4nqe's migration and is never updated afterwards (`Game_rawg_id_set` only writes `game_detail`); it must not be read here.
- Steam wins when both ids are present. No same-run fallback to RAWG when the Steam fetch fails — the row simply stays unstamped and is retried next run (keeps the job a straight mirror of the release-date job's failure handling).
- A game with neither id is not a candidate at all (the WHERE clause excludes it) — no stamp, no fetch. (This replaces the earlier draft's "stamp without a fetch" idea: there is nothing to stamp *from*, and the WHERE clause already keeps such rows out of the set.)

### Job

`GameDescriptionBackfill.runBackfill conn jobLock httpClient getRawgConfig : Async<BackfillResult>` mirroring `GameReleaseDateBackfill.fs`:

- `getRawgConfig: unit -> Rawg.RawgConfig` is the extra dependency the release-date job doesn't have; `Composition.fs` already has it in scope (it hands it to `PlaytimeTracker.runSync` and `Api.create`).
- Steam candidates: `Steam.getSteamStoreDetails httpClient appId` → on `Ok details`, write `Steam.storeDescription details` (already sanitized at decode time) as `description` and `details.ShortDescription` as `short_description` via the read-modify-write pattern (never a partial card that blanks `website_url`), then stamp. Pacing is inside `getSteamStoreDetails` (integration-w7ktb's adapter-owned throttle) — the job does not pace itself. `Error _` → leave unstamped, retry next run.
- RAWG candidates: `Rawg.getGameDetails httpClient (getRawgConfig()) rawgId` → write `DescriptionSanitizer.sanitize details.Description`, falling back to `details.DescriptionRaw` when `Description` is empty, as `description`; leave `short_description` untouched; stamp. If `RawgConfig.ApiKey` is blank, skip every RAWG candidate without an HTTP call and without stamping (count them separately in the summary — they are neither errors nor successes). `Rawg.fs` has no adapter-owned throttle; at low-hundreds volume none is needed, but the job must still be resumable (one stamp per row as it lands, never a batch stamp at the end).
- An `Ok` fetch that yields an empty description still stamps (the source genuinely has nothing; re-polling forever would be the release-date job's semantics, which this cursor deliberately doesn't share).
- `jobLock` (the shared `jobDbLock` `SemaphoreSlim`) is held only around DB sections, never across an awaited HTTP call — the `withLock` discipline the three sibling jobs use.
- `BackfillResult` = `{ Processed; Succeeded; Errors; Skipped }` (`Skipped` = RAWG rows skipped for a missing API key).

### Schedule

- `Composition.fs`: read `description_backfill_hour` the way `releaseDateBackfillHour` is read (`SettingsStore.getSetting` → `Int32.TryParse` → default), default **8** — an hour clear of the release-date backfill (07:00) so the Steam Store jobs stay spread out (04 playtime sync, 05 facets, 06 Deck-compat, 07 release date, 08 this).
- Append a `JobSpec` named `"Game description backfill"` to `scheduledJobs`, logging `[GameDescriptionBackfill] …` with a summary that includes the skipped count. Registering the spec is all the operator wiring there is: the Jobs tab's generic "Run now" and the `job_runs` recorder come from the registry (`tryStartJob`/`jobRunRecorder`); games-ev65k added no bespoke trigger, so neither does this.

### Out of scope

- No new event, no projection column, no `GameProjection.handleEvent` arm (ADR-0043: third-party cache data is not event-worthy; ADR-0045: never write through the ProjectionHandler).
- `Rawg.previewGame` keeps returning plain `description_raw` (r1tx4's decision) — untouched.
- `website_url` is not refreshed by this job.
- No client change. The detail page already renders the cache-backed description through `RichText.render`.

## Acceptance criteria

- [ ] `MetadataCache.initialize` adds `description_fetched_at` to `game_metadata_cache`; a `MetadataCacheTests.fs` case shows it is present after one `initialize` and unchanged after a second (the existing "adds the … columns, idempotently" pattern).
- [ ] Adding a game through `addGameFromSteam` and through `addGame` with a `RawgId` (the existing `AddGameFromSteamTests.fs`/`AddGameFromRawgTests.fs` fixtures) leaves each new row stamped: an Expecto case asserts neither slug is returned by `findGamesNeedingDescriptionBackfill`.
- [ ] A short_description-only refresh through `updateGameIdentityCache` (or whatever helper the Steam re-enrich branches call) on a legacy row does **not** stamp it: the row is still returned by `findGamesNeedingDescriptionBackfill` afterwards.
- [ ] `findGamesNeedingDescriptionBackfill` returns (a) a legacy row with a `game_metadata_cache` row and `description_fetched_at IS NULL` plus a `game_detail.steam_app_id`, (b) a game with a `game_detail.rawg_id` and **no** `game_metadata_cache` row at all, and (c) nothing for a stamped row or for a game with neither id. A game with both ids is returned once, as a Steam candidate.
- [ ] `GameDescriptionBackfill.runBackfill` with a stubbed `HttpClient` serving both the Steam `appdetails` shape and the RAWG `/games/{id}` shape (the `GameReleaseDateBackfillTests.fs` `StubHandler` pattern): rewrites a legacy flattened Steam-linked row with the sanitized description (keeps `<p>`/`<strong>`, drops `<h2>`/`<img>`) and refreshes its `short_description`; inserts a cache row for a RAWG-only game with no prior row, carrying RAWG's sanitized `description`; stamps both; and leaves a row whose Steam fetch returned an error unstamped with its old description intact.
- [ ] With a blank RAWG API key, a RAWG-only candidate is skipped: the stub records zero requests for it, it stays unstamped, and `BackfillResult.Skipped` counts it.
- [ ] A Steam `Ok` response with an empty `about_the_game`/`detailed_description` still stamps the row (it leaves the candidate set on the next call).
- [ ] The existing website_url on a legacy row survives the job's description write untouched.
- [ ] `Composition.fs` registers a `"Game description backfill"` `JobSpec` at `description_backfill_hour` (default 8), reading the setting in the same shape as `releaseDateBackfillHour`; no other job in `scheduledJobs` defaults to hour 8.
- [ ] `grep -rn "Game_description" src/Server/Games.fs` shows no new event case; `GameProjection.fs` is untouched by the diff.
- [ ] `npm run build`, `npm test`, `npm run test:client` green (no client change expected).
- [ ] After one scheduled or "Run now" pass on the live library, a Steam-linked game imported before r1tx4 shows paragraph breaks and emphasis on its detail page instead of one flat block, and a RAWG-only game that had no description now has one. [human-eye]

## Notes

- Split out of `games-r1tx4` at refinement (2026-09-17) so the sanitizer/renderer task stayed a pure mirror of books-nvnyk and the re-fetch — a scheduled job with its own schema column and throttle budget — is its own worker's whole job. r1tx4 shipped later the same day (Expecto 928/928), so the promote gate is satisfied.
- Refined 2026-09-17 against the shipped r1tx4 code. Three corrections to the first draft: (1) the RAWG id lives on `game_detail`, not the cache (the cache's `rawg_id` is a stale one-time seed); (2) legacy RAWG-only games have no cache row, so the candidate query is a LEFT JOIN and the stamp must upsert; (3) `upsertGameIdentityCard` is shared with description-less refreshes, so the stamp cannot live inside it. Also: the job needs `getRawgConfig`, and a blank RAWG key must skip rather than error.
- Order of magnitude: the library's Steam-linked games number in the low hundreds (cf. the 204 `Game_play_time_set` streams games-h4mrd reconstructed); at the storefront throttle that is one or two nightly runs, which is why resumability matters more than speed.
- Transitional population: games added between r1tx4 shipping and this task shipping are already sanitized but unstamped (the column didn't exist). They are re-fetched exactly once by the first run and stamped — idempotent, and cheap at the volume involved. Not worth a special case.
- `Rawg.getGameDetails` decodes both `description` (HTML) and `description_raw`; today only `description_raw` is read anywhere except r1tx4's `addGame` path. r1tx4 makes `Rawg.previewGame` keep plain `description_raw` — this task must not change the preview either.
- Do not add an event for the refresh (ADR-0043: third-party cache data is not event-worthy) and never write through the ProjectionHandler (ADR-0045).
- Fixtures only, never the live DB (project standing rule); the worker never runs the job against `mediatheca.db`. The `[human-eye]` criterion is the builder's, after deploy.
- Orchestrator not consulted at this refinement: no open domain question (cache-only, no aggregate or event change, ADR-0043/ADR-0045 already settle the tier); the refinement was a code-grounding pass over the shipped r1tx4 diff.

## Outcome

Added `GameDescriptionBackfill.fs`, a fourth resumable throttled backfill mirroring
`GameFacetBackfill.fs`/`GameDeckCompatBackfill.fs`/`GameReleaseDateBackfill.fs` (ADR-0043/ADR-0045,
this task's own ADR-0081). It re-fetches every already-cached game's description through
games-r1tx4's sanitizer — Steam-linked via `Steam.getSteamStoreDetails`/`Steam.storeDescription`
(already sanitized at decode time), RAWG-only via `Rawg.getGameDetails` (sanitized by the job itself,
`Description` falling back to `DescriptionRaw`) — so games imported before r1tx4 gain
paragraphs/emphasis on their detail page instead of one flat block, and RAWG-only games that had an
empty description since games-v4nqe's column drop now get one.

`MetadataCache.fs` grew `game_metadata_cache.description_fetched_at` (via the existing guarded,
idempotent `ALTER TABLE ... ADD COLUMN` block `initialize` already uses for the sibling cursors), its
own resume cursor deliberately separate from `fetched_at`/`deck_compat_fetched_at`/
`release_date_fetched_at`; `MetadataCache.stampDescriptionFetched` (a single-column upsert that
survives a missing row); `MetadataCache.DescriptionSource` (`SteamApp of int | RawgGame of int`); and
`MetadataCache.findGamesNeedingDescriptionBackfill` (a LEFT JOIN over `game_detail`/
`game_metadata_cache`, covering the RAWG-only legacy population with no cache row at all — both ids
read from `game_detail`, never the cache's stale one-time-seeded `rawg_id`; Steam wins when both ids
are present).

The stamp deliberately lives outside `upsertGameIdentityCard` (shared with description-less
refreshes) — `Api.fs`'s `updateGameIdentityCache` now stamps iff its `description` parameter is
`Some`, and every direct creation-path `upsertGameIdentityCard` call site (`runSteamFamilyImport`'s
new-game branch, `addGameFromSteamCore`, `attachSteamToGameCore`, the Steam library import's new-game
branch, `addGame`'s RAWG path, all in `Api.fs`; `PlaytimeTracker`'s scheduled-sync new-game path) gets
an explicit stamp call right after. `Composition.fs` registers the `"Game description backfill"`
`JobSpec` at `description_backfill_hour` (default 08:00 local, an hour clear of the release-date
backfill's 07:00), reading `getRawgConfig` from the same scope `PlaytimeTracker.runSync`/`Api.create`
already use.

No new event, no projection column, no `GameProjection.handleEvent` arm — `GameProjection.fs` is
untouched by the diff (confirmed via `git diff --stat`), and `grep -rn "Game_description" src/Server/Games.fs`
shows only the pre-existing legacy `Game_description_set` event, never a new one. No client change —
the detail page already renders the cache-backed description through `RichText.render`.

Tests (10 new, Expecto 928 -> 938, all green): `MetadataCacheTests.fs` (idempotent column addition;
`stampDescriptionFetched` creating a row for a missing slug and touching only its own column on an
existing one; `findGamesNeedingDescriptionBackfill`'s LEFT JOIN behavior across all five scenarios —
legacy Steam-linked row, RAWG-only row with no cache row, exclusions for a stamped row and a
neither-id game, and the both-ids-present-Steam-wins case). `GameDescriptionBackfillTests.fs` (a
`GameReleaseDateBackfillTests.fs`-style `StubHandler` serving both Steam `appdetails` and RAWG
`/games/{id}` shapes in one test: rewrites a legacy flattened Steam row keeping `<p>`/`<strong>` and
dropping `<h2>`/`<img>`, inserts a fresh RAWG-only row, leaves a Steam-error row unstamped with its
old description and website_url intact; a separate test for a blank-RAWG-key skip counted in
`BackfillResult.Skipped` with zero HTTP requests made; a separate test for an `Ok` empty-description
response still stamping). `AddGameFromSteamTests.fs`/`AddGameFromRawgTests.fs` each gained a
"freshly-created row is stamped" case. `SteamFamilyIncrementalImportTests.fs` gained a case proving a
`FullReenrich` short_description-only refresh on a legacy row never stamps
`description_fetched_at`, so the row survives as a candidate.

Gates: `npm run build` OK (Fable compiles clean), `dotnet` Expecto 938/938, `npm run test:client`
Vitest 117/117 (unchanged — no client diff).

Key files: `src/Server/GameDescriptionBackfill.fs`, `src/Server/MetadataCache.fs`, `src/Server/Api.fs`,
`src/Server/PlaytimeTracker.fs`, `src/Server/Composition.fs`, `src/Server/Server.fsproj`,
`tests/Server.Tests/GameDescriptionBackfillTests.fs`, `tests/Server.Tests/MetadataCacheTests.fs`,
`tests/Server.Tests/AddGameFromSteamTests.fs`, `tests/Server.Tests/AddGameFromRawgTests.fs`,
`tests/Server.Tests/SteamFamilyIncrementalImportTests.fs`, `tests/Server.Tests/Server.Tests.fsproj`.

The `[human-eye]` acceptance criterion (a live library pass showing paragraph breaks on a
pre-r1tx4-imported game and a filled-in RAWG-only description) is the builder's, after deploy — this
worker never touched the live database, per the project's standing fixtures-only rule.
