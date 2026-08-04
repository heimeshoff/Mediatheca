---
id: games-b8xnw
title: Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge
status: doing
type: feature
context: games
created: 2026-08-04
completed:
depends_on: [games-a7dqx, design-system-001]
blocks: []
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0053]
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
