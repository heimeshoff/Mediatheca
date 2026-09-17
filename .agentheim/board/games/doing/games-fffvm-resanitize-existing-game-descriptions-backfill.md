---
id: games-fffvm
title: Re-sanitize existing game descriptions — a resumable, throttled backfill that re-fetches every already-cached game's description (Steam-linked via the storefront, RAWG-only via RAWG details) through games-r1tx4's sanitizer, so games imported before it gain paragraphs/emphasis and RAWG-only games get the description games-v4nqe silently dropped
status: doing
type: chore
context: games
created: 2026-09-17
completed:
depends_on: [games-r1tx4]
blocks: []
tags: [games, steam, rawg, description, backfill, metadata-cache, integration]
related_adrs: [0043, 0045]
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
