---
id: games-fffvm
title: Re-sanitize existing game descriptions — a resumable, throttled backfill that re-fetches every already-cached game's description (Steam-linked via the storefront, RAWG-only via RAWG details) through games-r1tx4's sanitizer, so games imported before it gain paragraphs/emphasis and RAWG-only games get the description games-v4nqe silently dropped
status: backlog
type: chore
context: games
created: 2026-09-17
completed:
depends_on: [games-r1tx4]
blocks: []
tags: [games, steam, rawg, description, backfill, metadata-cache, integration]
related_adrs: [0043, 0045]
related_research: []
prior_art: [games-ev65k, games-b8xnw, games-a7dqx, games-v4nqe]
---

## Why

`games-r1tx4` sanitizes descriptions that arrive *after* it ships. Every row already in `game_metadata_cache.description` is `stripHtmlTags` output — tagless and newline-less — so `RichText.render` shows it as one paragraph, and the visible payoff of r1tx4 never reaches the games the user actually owns. A second population is worse off: RAWG-added games with no Steam auto-attach have had an **empty** description since games-v4nqe dropped the projection column without adding a creation-path cache write (r1tx4 closes that going forward, not retroactively).

## What

A fourth instance of the games BC's resumable throttled backfill shape (`GameFacetBackfill` → `GameDeckCompatBackfill` → `GameReleaseDateBackfill`, ADR-0043/ADR-0045):

- **Marker column.** Add `description_fetched_at TEXT` to `game_metadata_cache` (`ALTER TABLE … ADD COLUMN`, guarded like the existing cache-column additions). `NULL` = never fetched through the sanitizer. Every sanitized write site r1tx4 leaves behind (`upsertGameIdentityCard` from the creation paths, `updateGameIdentityCache`'s description slice) stamps it, so a game added after this ships is never re-fetched by the job.
- **Candidates.** `MetadataCache.findGamesNeedingDescriptionBackfill`: rows with `description_fetched_at IS NULL` and either a `steam_app_id` (→ `Steam.getSteamStoreDetails`, `Steam.storeDescription`, already throttled by integration-w7ktb's adapter-owned pacing) or a `rawg_id` (→ `Rawg.getGameDetails`, sanitized `description`, fallback `description_raw`). Steam wins when both exist. A row with neither is stamped without a fetch so it leaves the candidate set.
- **Job.** `GameDescriptionBackfill.runBackfill conn jobLock httpClient : Async<BackfillResult>` mirroring `GameReleaseDateBackfill.fs` — each row stamped as it lands (resumable), a fetch failure leaves the row unstamped for the next run, the `jobLock` held only around DB sections (administration-tj8n2).
- **Schedule.** `Composition.fs`: `description_backfill_hour` setting, default `8` — an hour clear of the release-date backfill (07:00) so the four Steam Store jobs stay spread out. Wire exactly the way games-ev65k wired its hour (including any operator "run now" trigger it exposed, if one exists — mirror, don't invent).
- **Short description.** When the Steam fetch succeeds, also refresh `short_description` (plain text) and stamp; RAWG-only rows leave `short_description` untouched.

## Acceptance criteria

- [ ] `game_metadata_cache` gains `description_fetched_at`; the migration is idempotent on an existing DB and a fresh DB (both `MetadataCache` schema test paths).
- [ ] Every description write site from r1tx4 stamps `description_fetched_at`; an Expecto case adding a game via `addGameFromSteam` shows the new row is **not** returned by `findGamesNeedingDescriptionBackfill`.
- [ ] `findGamesNeedingDescriptionBackfill` returns legacy rows (`description_fetched_at IS NULL`) with a Steam app id or a RAWG id, and nothing else.
- [ ] `GameDescriptionBackfill.runBackfill` with a stubbed Steam storefront + RAWG details `HttpClient` (the `GameReleaseDateBackfillTests.fs` pattern) rewrites a legacy flattened Steam-linked row with the sanitized description, fills an empty RAWG-only row from RAWG's `description`, stamps both, and leaves a row whose fetch failed unstamped.
- [ ] A row with neither id is stamped without any HTTP call (the stub records zero requests for it).
- [ ] `Composition.fs` schedules the job at `description_backfill_hour` (default 08:00) in the same shape as `releaseDateBackfillHour`.
- [ ] No event, no projection column, no `GameProjection.handleEvent` arm is touched.
- [ ] `npm run build`, `npm test` green (no client change expected; `npm run test:client` still green).

## Notes

- Split out of `games-r1tx4` at refinement (2026-09-17) so the sanitizer/renderer task stays a pure mirror of books-nvnyk and the re-fetch — a scheduled job with its own schema column and throttle budget — is its own worker's whole job. Waits in `backlog/` until r1tx4 is in `done/` (promote gate).
- Order of magnitude: the library's Steam-linked games number in the low hundreds (cf. the 204 `Game_play_time_set` streams games-h4mrd reconstructed); at the storefront throttle that is one or two nightly runs, which is why resumability matters more than speed.
- `Rawg.getGameDetails` decodes both `description` (HTML) and `description_raw`; today only `description_raw` is read anywhere. r1tx4 makes `Rawg.previewGame` keep plain `description_raw` — this task must not change the preview either.
- Do not add an event for the refresh (ADR-0043: third-party cache data is not event-worthy) and never write through the ProjectionHandler (ADR-0045).
- Fixtures only, never the live DB (project standing rule); the worker never runs the job against `mediatheca.db`.
