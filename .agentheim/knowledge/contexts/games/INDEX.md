## ADRs scoped to this BC

<!-- adr-local:start -->
- **0087** -- Deck-compat backfill re-checks already-recorded verdicts on a per-verdict age limit (Unknown 30 days, Playable/Unsupported 90, Verified 180), capped at 25 per run oldest-first and counted after the failed-attempt backoff filter; the never-fetched cohort stays uncapped and the timer remains the only trigger. Amends ADR-0084. -- 2026-09-18 -- `../../decisions/0087-deck-compat-age-based-verdict-recheck.md`
- **0084** -- Deck-compat backfill failures back off exponentially, `min(2^attempts, 30)` days, uniformly for `Error` results and exceptions; only `upsertGameDeckCompat`'s success path resets the failure bookkeeping, and a recovered game may wait up to 30 days. Amends ADR-0059's "same retry semantics as GameFacetBackfill" claim. -- 2026-09-18 -- `../../decisions/0084-deck-compat-backfill-exponential-backoff-on-failure.md`
- **0081** -- The game-description backfill walks its own `description_fetched_at` cursor; the stamp lives in the callers (`updateGameIdentityCache` stamps iff `description` is `Some`, creation paths stamp explicitly), never inside the shared `upsertGameIdentityCard`; Steam wins over RAWG when both ids are present; a blank RAWG key is a distinct `Skipped` outcome -- 2026-09-17 -- `../../decisions/0081-description-backfill-own-cursor-stamp-outside-shared-writer-steam-wins.md`
- **0060** -- Steam release dates are cache-tier facts on their own `release_date_fetched_at` cursor; partial-precision dates ("October 2026", "2026") sort by first-of-period while display keeps the raw string, and the backfill re-polls a game only until it is confirmed released, so the candidate set drains itself -- 2026-08-07 -- `../../decisions/0060-release-date-cache-partial-precision-sort-and-self-draining-backfill.md`
- **0059** -- The unofficial `ajaxgetdeckappcompatibilityreport` endpoint is dead (bare 302 for every request shape); Steam Deck compatibility is scraped from the store app page's embedded `data-hardwarecompatibility` attribute, live-verified against six titles -- 2026-08-04 -- `../../decisions/0059-steam-deck-compat-endpoint-retired-html-scrape-replacement.md`
- **0057** -- Play facets UI: the 4-badge row is Solo/Co-op/Versus + a Couch summary badge with online/couch sub-labels; the ADR-0053 override trap is guarded by pure `Shared` record-update functions (`withSolo` et al.), Expecto-tested in place of absent client-test infra -- 2026-08-04 -- `../../decisions/0057-play-facets-ui-badge-mapping-and-override-trap-guard.md`
- **0055** -- Game genres stays an event-carried identity-card projection column (amends ADR-0043's Game row back into compliance); games-v4nqe's genres cache-cutover is reverted — `game_metadata_cache.genres` is kept but permanently unused -- 2026-08-04 -- `../../decisions/0055-game-genres-stays-event-carried-identity-card.md`
- **0054** -- The Steam category-id → PlayFacets derivation table is fixed from 13 live-verified appId fixtures (ids decoded with `&l=english`); bare umbrella ids resolve to the online facet -- 2026-08-04 -- `../../decisions/0054-steam-category-id-facet-table-live-verified.md`
- **0053** -- Game play facets are cache-derived from Steam; per-field manual overrides stay event-sourced (`Game_play_facets_overridden` carrying an all-`Option` record) and merge at query time via a pure `PlayFacets.merge` -- 2026-08-04 -- `../../decisions/0053-game-play-facets-cache-derived-event-sourced-override.md`
- **0050** -- Play sessions are first-class Games events keyed on (game, gaming day); pre-tracking playtime is its own dateless event; the Steam sync cursor is derived from the log via the two-fold aggregate (`TotalPlayTimeMinutes` / `SteamObservedMinutes`). -- 2026-08-01 -- `../../decisions/0050-play-sessions-first-class-events-two-fold-cursor.md`
- **0042** -- Games lifecycle remodeled to five states (Backlog/InFocus/Retired/Abandoned/Dismissed) — OnHold removed via parse-time upcast, Completed renamed Retired, Playing never added (InFocus covers it); DesignSystem.LifecycleStatus unifies 1:1 -- 2026-08-01 -- `knowledge/decisions/0042-games-lifecycle-remodeled-to-five-states.md`
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
<!-- no research touching this BC -->
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- no concept pages yet -->
<!-- concepts:end -->


## Pointers

- BC README (ubiquitous language, invariants): `README.md`
- Task board (tasks by status) for this BC: `../../../board/games/INDEX.md`
