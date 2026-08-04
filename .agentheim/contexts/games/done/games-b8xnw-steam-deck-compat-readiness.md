---
id: games-b8xnw
title: Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge
status: done
type: feature
context: games
created: 2026-08-04
completed: 2026-08-04
depends_on: [games-a7dqx, design-system-001]
blocks: []
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0053, 0059]
related_research: []
prior_art: []
---

## Why

Marco is interested in Steam Deck compatibility (Verified / Playable / Unsupported) per game —
decision 6 of the games-a7dqx ideation session (2026-08-04). Not in the standard `appdetails`
response; requires the separate unofficial `ajaxgetdeckappcompatibilityreport` endpoint. Scoped out
of games-a7dqx at refinement: it's a new UI feature (a badge/filter, not a stop-emitting refactor),
it needs its own endpoint with its own rate-limit/throttle behavior, and it can reuse the resumable
throttled-backfill infrastructure games-a7dqx builds for play facets rather than inventing a second
one from scratch — hence the `depends_on`.

## What

- Fetch `ajaxgetdeckappcompatibilityreport` per Steam appId (unofficial endpoint — verify response
  shape and rate-limit behavior empirically during implementation; it is not part of Valve's
  documented Web API and may need cookie/session handling different from `store.steampowered.com`'s
  `appdetails`).
- Cache the result as a typed column on `game_metadata_cache` (`deck_compat: TEXT`,
  `Verified | Playable | Unsupported | Unknown`) — third-party data, re-fetchable, no event
  (ADR-0043; same tier as the play facets this task depends on).
- Reuse games-a7dqx's resumable throttled-backfill job shape (walk `fetched_at IS NULL` or a
  similar cohort marker) rather than building a second background-job mechanism.
- UI: a badge on the game card/detail page, likely alongside the four play-facet badges games-a7dqx
  adds. No manual override needed unless a correction use case surfaces later (Steam's own
  compatibility verdict, unlike play modes, isn't something Marco is likely to know better than
  Valve's own testing — revisit only if that assumption is wrong in practice).

## Acceptance criteria

- [ ] `ajaxgetdeckappcompatibilityreport`'s actual response shape is verified against a live fetch
      (not assumed from documentation, since it's unofficial) before the decoder is written.
- [ ] `game_metadata_cache` gains a `deck_compat` column, written only by the cache-tier backfill/
      refresh path, never by a `ProjectionHandler` (ADR-0045).
- [ ] The backfill job reuses games-a7dqx's resumable-walk shape; is throttled to the endpoint's
      observed rate limit; never a blocking startup step.
- [ ] A Deck-compatibility badge renders on the game card/detail page. [human-eye]
- [ ] `checkProjectionDrift` stays zero for `GameProjection` (the new column lives in the cache
      tier, never in a `Projected` table).

## Notes

Born from games-a7dqx decision 6 (2026-08-04). Stays in backlog until games-a7dqx lands — the
backfill-job reuse is the point of the dependency, not just sequencing.

Decisions recorded in ADR-0059.

## Outcome

Shipped Steam Deck compatibility (Verified/Playable/Unsupported/Unknown) as a
`game_metadata_cache`-only facet, read straight through into `GameListItem`/`GameDetail` and
rendered as a badge alongside the play-facet badges — no event, no override, no aggregate
involvement, matching ADR-0043's re-derivability doctrine exactly as the task called for.

**Live verification found the task's named endpoint dead.** `ajaxgetdeckappcompatibilityreport`
returns a bare `302` redirect for every request shape tried (GET/POST, with/without a session
cookie, with/without a browser User-Agent) — Valve has retired it. The verdict is still available,
scraped instead from the `data-hardwarecompatibility="{...}"` HTML attribute embedded in every
store app page, live-verified against six titles (Hades/Valheim/Elden Ring = Verified, Elite
Dangerous/Counter-Strike 2 = Playable, Beat Saber = Unsupported) — full detail and the mapping
table in ADR-0059. Mature-rated titles need Steam's age-gate cookies or the page redirects to
`/agecheck/` instead of rendering the attribute at all.

Key files:
- `src/Server/Steam.fs` — `getDeckCompatibility`/`decodeDeckCompatFromHtml`/`mapDeckCompatCategory`
  (the live HTML fetch, the pure HTML-attribute decoder, and the pure category-int mapper, each
  independently unit-tested).
- `src/Server/MetadataCache.fs` — `deck_compat`/`deck_compat_fetched_at` columns (the latter its
  own resume cursor, deliberately separate from the play-facets backfill's `fetched_at`),
  `upsertGameDeckCompat`/`readDeckCompat`/`findGamesNeedingDeckCompatBackfill`.
- `src/Server/GameDeckCompatBackfill.fs` — the resumable throttled backfill job, reusing
  `GameFacetBackfill.fs`'s shape (games-a7dqx `depends_on`).
- `src/Server/GameProjection.fs` — `getAll`/`getBySlug`/`getRecentlyAddedGames` all wire
  `DeckCompat` straight from the cache (no merge — read composition only).
- `src/Server/Composition.fs` — "Game Deck-compat backfill" scheduled job, 06:00 local (an hour
  clear of the play-facets backfill), never blocking startup (smoke-tested against a scratch
  `DATA_DIR`, confirmed the HTTP listener comes up before either backfill's catch-up run
  completes).
- `src/Shared/Shared.fs` — `DeckCompatibility` DU, `GameListItem.DeckCompat`/`GameDetail.DeckCompat`.
- `src/Client/Components/PlayFacetsDisplay.fs` — `deckCompatBadge` (colored chip, `Unknown` renders
  nothing), wired into `Pages/Games/Views.fs` and `Pages/GameDetail/Views.fs` alongside the
  existing play-facet badge row.
- ADR-0059 records the endpoint-retirement discovery, the live-verified mapping table, and the
  separate-cursor-column decision.

Tests: 20 new (`SteamDeckCompatTests.fs` — decode/map/fetch pure-function and stub-HTTP coverage;
`GameDeckCompatBackfillTests.fs` — resumable-cursor, retry-on-failure, cursor-independence, and
scope-discipline coverage mirroring `GameFacetBackfillTests.fs`; `GameDeckCompatProjectionTests.fs`
— honest-degradation, read-composition, and `checkProjectionDrift`-stays-zero coverage). Full
suite 637/637 passing. `npm run build` (Fable gate) green.

The badge-renders-on-card/detail acceptance criterion is marked `[human-eye]` — verified by code
review, the Fable build, and the projection-layer tests proving `DeckCompat` reaches the DTO
correctly; visual confirmation in a running browser is deferred to normal review (the same stance
`games-j6wkr`'s own Outcome took for its four play-facet badges).

## Verifier note (iteration 1)

REASONS:
- ADR-0045 (in this task's `related_adrs`, and named directly by acceptance criterion 2) states as its enforcement property: "This is enforced by construction, not just by the registry entry: no `*Projection.fs` file references `MetadataCache` at all (verified: `grep -rn "MetadataCache" src/Server/*Projection.fs` returns zero matches)". The diff falsifies that. `src/Server/GameProjection.fs:619`, `:717`, `:934` each now call `MetadataCache.readDeckCompat rd` — the first *code* (non-comment) references to `MetadataCache` from any `*Projection.fs` in the tree. ADR-0045's own "Alternatives considered" rejects exactly this seam, and its Decision warns that breaking it "would degrade ADR-0031's 'read-only against live holds by construction' property to a code-review convention."
- The deviation is silent and gratuitous, not a considered amendment. The immediate precedent for reading these same `game_metadata_cache` join columns is three private helpers in the same file — `GameProjection.fs:554 readCachedPlayFacets` and `:567 readPlayFacetsOverrideRow` — which prior cutover tasks (games-a7dqx/v4nqe) deliberately kept local precisely to hold the zero-grep property. `MetadataCache.readDeckCompat` is a pure `IDataReader` decoder used from nowhere else in the codebase (verified by grep), so nothing was gained by hoisting it into `MetadataCache`. ADR-0059 asserts compliance ("Per ADR-0043/ADR-0045 ... this is cache-tier only") without acknowledging the broken invariant, and no ADR supersedes or amends 0045.
- Secondary (criterion 3, partial): "throttled to the endpoint's observed rate limit" is unevidenced for the *new* source. `GameDeckCompatBackfill.fs:530` uses `Async.Sleep 300` justified only as "mirrors GameFacetBackfill's throttle"; ADR-0059 and `Steam.fs`'s module comment record the live verification of the response shape and the age-gate behavior but record no observed rate limit for `store.steampowered.com/app/<id>/`, and no test asserts the throttle.

SUGGESTED_FIX: Move the deck-compat reader into `GameProjection.fs` as a private helper alongside `readCachedPlayFacets` (dropping `MetadataCache.readDeckCompat`, or keeping it only for the backfill's own use), so `grep -rn "MetadataCache" src/Server/*Projection.fs` returns no code matches again; alternatively, if the seam is genuinely wanted, write an ADR that explicitly amends ADR-0045's by-construction clause and lands a mechanized check in its place. Separately, record the store-page fetch's observed rate-limit behavior in ADR-0059 (or state plainly that 300ms is inherited unmeasured) to close criterion 3.

ITERATION_HINT: likely-fixable

## Outcome (iteration 2)

Both verifier findings addressed directly, no redesign:

1. **`MetadataCache` seam removed from `GameProjection.fs`.** Added private
   `decodeDeckCompat`/`readDeckCompat` helpers to `GameProjection.fs`
   (alongside the existing `readCachedPlayFacets`/`readPlayFacetsOverrideRow`
   precedent, and mirroring how `decodeVrSupport` is already duplicated
   locally rather than shared across `Games.fs`/`GameProjection.fs`), and
   pointed the three call sites (`getAll`, `getBySlug`,
   `getRecentlyAddedGames`) at the local helper instead of
   `MetadataCache.readDeckCompat`. Deleted `MetadataCache.readDeckCompat`
   (public, called from nowhere but `GameProjection.fs` — verified by grep)
   and its now-dead-code private `decodeDeckCompat` twin from
   `MetadataCache.fs`; `MetadataCache.fs` keeps only `encodeDeckCompat`
   (used by `upsertGameDeckCompat`, the backfill's writer). Confirmed
   `grep -rn "MetadataCache" src/Server/*Projection.fs` introduces zero new
   matches from this task — the only remaining matches are pre-existing
   comments in `GameProjection.fs`/`SeriesProjection.fs` predating this
   task, none of them code references.
2. **ADR-0059 amended with an honest rate-limit statement.** Added a
   paragraph to the Decision section and a line in Consequences/Negative
   stating plainly that the 300ms throttle is inherited unmeasured from
   `GameFacetBackfill.fs` (which hits a different endpoint,
   `appdetails`, not the store page) rather than independently observed for
   `store.steampowered.com/app/<id>/`, and why that's an accepted default
   (resumable job, NULL-cursor-on-failure retry, single adjustable
   constant) rather than a fabricated observation.

No new tests were needed — both fixes are internal refactors/documentation
that don't change observable behavior (same read values, same throttle
value), so the existing 637 tests (including
`GameDeckCompatProjectionTests.fs`'s read-composition coverage) continue to
prove the behavior is unchanged. Full suite: 637/637 passing. `npm run
build` (Fable gate): green.

Key files changed this iteration:
- `src/Server/GameProjection.fs` — added private `decodeDeckCompat`/
  `readDeckCompat`; three call sites repointed from
  `MetadataCache.readDeckCompat` to the local helper.
- `src/Server/MetadataCache.fs` — removed `readDeckCompat` (public) and its
  dead-code `decodeDeckCompat` twin; kept `encodeDeckCompat` for the write
  path.
- `.agentheim/knowledge/decisions/0059-steam-deck-compat-endpoint-retired-html-scrape-replacement.md`
  — added the honest unmeasured-throttle statement to Decision and
  Consequences/Negative.
