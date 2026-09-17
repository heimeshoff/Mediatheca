---
id: 0081
title: Description backfill gets its own cursor column, its stamp lives outside the shared identity-card writer, and Steam wins over RAWG when both ids are present
scope: games
status: accepted
date: 2026-09-17
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-fffvm]
related_research: []
---

# ADR 0081: Description backfill gets its own cursor column, its stamp lives outside the shared identity-card writer, and Steam wins over RAWG when both ids are present

## Context

`games-fffvm` adds a fourth resumable throttled backfill to `game_metadata_cache`, following the
ADR-0043 (event-worthiness doctrine) / ADR-0045 (typed per-BC cache tier) precedent
`games-a7dqx`/`games-b8xnw`/`games-ev65k` already established for play facets, Deck-compatibility,
and release dates. The tier assignment itself was not a live question — a description is squarely a
third party's re-fetchable text, the same shape as the three prior cutovers. Three judgment calls the
task's refinement left to the worker do warrant a record:

1. **Where the stamp lives.** `MetadataCache.upsertGameIdentityCard` is shared by every creation path
   AND by `Api.fs`'s `updateGameIdentityCache`, which routes description-LESS refreshes (a
   short_description-only or website_url-only Steam re-enrich) through the exact same writer. Folding
   the stamp into `upsertGameIdentityCard` itself would have wrongly marked those refreshes as
   "description fetched," leaving a legacy flattened description a permanent false negative in the
   backfill's own candidate set.
2. **Which id wins when a game has both a `steam_app_id` and a `rawg_id`.** Unlike the facets/
   Deck-compat/release-date backfills (Steam-only), this task's candidate population spans two
   sources.
3. **What a blank RAWG API key means for a RAWG-only candidate.** Neither an error (nothing was
   attempted) nor a success (nothing was fetched) — but also not a reason to fail the whole run.

## Decision

### `description_fetched_at` is its own resume cursor, and the stamp lives in the caller, not the shared writer

`game_metadata_cache.description_fetched_at` is deliberately separate from `fetched_at`/
`deck_compat_fetched_at`/`release_date_fetched_at` — the same reasoning ADR-0059/ADR-0060 already
record for those: independently-scheduled jobs against independent re-fetch semantics must never
share a cursor column, or one job's stamp silently exempts another's work for the same game.

`MetadataCache.upsertGameIdentityCard` stays stamp-free. Instead:

- `Api.fs`'s private `updateGameIdentityCache` helper — already the read-modify-write wrapper every
  Steam re-enrich call site shares — now calls `MetadataCache.stampDescriptionFetched` **iff its
  `description: string option` parameter is `Some`**. A short_description-only or website_url-only
  refresh passes `None` for `description` and so never stamps.
- Every direct creation-path `MetadataCache.upsertGameIdentityCard` call site (the five creation
  paths plus `attachSteamToGameCore`) gets an explicit `MetadataCache.stampDescriptionFetched` call
  immediately after, since those calls always carry a genuine (possibly empty, when both Steam and
  RAWG failed) sanitizer-output description and bypass the shared helper entirely.

`stampDescriptionFetched` itself is a single-column `INSERT ... ON CONFLICT(game_slug) DO UPDATE`
naming only `description_fetched_at`, so it survives a row that doesn't exist yet at all — the
RAWG-only legacy population `games-v4nqe`'s seed never created a row for.

### Steam wins over RAWG when both ids are present; both ids are read from `game_detail`

`MetadataCache.findGamesNeedingDescriptionBackfill`'s candidate query is a LEFT JOIN (mirroring
`GameProjection.findGamesWithEmptyDescriptionAndSteamAppId`, not the facets/Deck-compat backfills'
INNER JOIN) so a game with no cache row at all is still a candidate:

```sql
SELECT gd.slug, gd.steam_app_id, gd.rawg_id
FROM game_detail gd
LEFT JOIN game_metadata_cache mc ON mc.game_slug = gd.slug
WHERE mc.description_fetched_at IS NULL
  AND (gd.steam_app_id IS NOT NULL OR gd.rawg_id IS NOT NULL)
```

When both ids are present, the row is classified `SteamApp` (Steam wins), never `RawgGame` — matching
every other creation-path preference in this codebase (Steam auto-attach after a RAWG import already
overwrites the RAWG-sourced description). There is no same-run fallback to RAWG if the Steam fetch
fails; the row simply stays unstamped and is retried next run, keeping the job a straight mirror of
the release-date job's failure handling.

Both ids come from `game_detail`, never `game_metadata_cache.rawg_id` — that column is a one-time
seed copy from `games-v4nqe`'s migration and is never updated afterwards (`Game_rawg_id_set` only
writes `game_detail`); reading it here would silently miss every game whose RAWG link was set after
the seed ran.

### A blank RAWG API key is a third, distinct outcome: `Skipped`

`BackfillResult` carries `Skipped` alongside `Processed`/`Succeeded`/`Errors`. A RAWG candidate with a
blank `RawgConfig.ApiKey` increments `Skipped` and makes no HTTP call at all — not an error (nothing
was attempted that could fail) and not a success (nothing was fetched). The row stays unstamped and
is retried on the next run once a key is configured.

### An `Ok` fetch with an empty description still stamps

Unlike the release-date backfill's steady-state re-poll (a release date is expected to *change* while
a game is unreleased), a description backfill has no such "not final yet" window — a successful fetch
that returns an empty description means the source genuinely has nothing right now, and re-polling
forever would just repeat the same empty result. The row is stamped and drops out of the cursor.

## Consequences

### Positive
- The stamp's placement (in the caller, never the shared writer) means no future description-less
  refresh call site can accidentally exempt a legacy row from the backfill just by sharing
  `upsertGameIdentityCard`.
- Steam-wins-over-RAWG keeps the candidate set deterministic and matches the existing auto-attach
  precedent, so a game's description source never flips unpredictably between runs.
- `Skipped` being distinct from `Errors`/`Succeeded` lets an operator tell "RAWG key missing" apart
  from "RAWG fetch genuinely failed" at a glance in the job's logged summary.

### Negative / accepted tradeoff
- The stamp now has two call sites (the shared helper's conditional stamp, and each creation path's
  explicit stamp) rather than one — a future new creation-path write site must remember to add the
  stamp itself, since it isn't automatic. Mitigated by the acceptance criteria's exhaustive site
  enumeration and this ADR's record of the reasoning.

## Alternatives considered

- **Folding the stamp into `upsertGameIdentityCard` itself** — rejected: that helper is shared with
  description-less refreshes via `updateGameIdentityCache`, which would wrongly mark a
  short_description/website_url-only refresh as "description fetched."
- **A `description: string option` parameter on `upsertGameIdentityCard` that stamps iff `Some`** —
  considered equivalent by the task's own refinement; not chosen only because it would have required
  changing the shared writer's signature and every one of its many call sites, whereas gating the
  stamp in the caller (`updateGameIdentityCache`) plus explicit stamps at the direct creation-path
  call sites touches the same set of sites with a smaller diff to the widely-shared helper.
- **Falling back to RAWG within the same run when a Steam fetch fails** — rejected: breaks the
  straight mirror of the release-date job's failure handling and adds a second fetch attempt per
  candidate for no clear benefit, since the row is retried next run regardless.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
  — the doctrine placing descriptions on the cache tier.
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the cache tier
  and its hard constraint (no `ProjectionHandler` touches it), unchanged by this ADR.
- `.agentheim/knowledge/decisions/0060-release-date-cache-partial-precision-sort-and-self-draining-backfill.md`
  — the sibling backfill this task's own-cursor-column reasoning and job shape follow.
- `src/Server/MetadataCache.fs` (`stampDescriptionFetched`, `findGamesNeedingDescriptionBackfill`,
  `DescriptionSource`), `src/Server/GameDescriptionBackfill.fs`, `src/Server/Api.fs`
  (`updateGameIdentityCache`) — the code this ADR describes.
