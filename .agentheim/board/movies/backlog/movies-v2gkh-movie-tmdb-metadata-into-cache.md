---
id: movies-v2gkh
title: Move Movie TMDB metadata into the cache — cut over the day a movie-refresh feature is actually built, since Movies are deterministic today and have no out-of-band writer at all
status: backlog
type: refactor
context: movies
created: 2026-08-01
completed:
depends_on: [administration-c3nvp]
blocks: []
tags: [movies, metadata, cache, tmdb]
related_adrs: [0012]
related_research: []
prior_art: []
---

## Why

`movie_list.tmdb_rating`, `movie_detail.tmdb_rating`, `overview`, `runtime`, `genres`,
`backdrop_ref`, `production_countries` are all TMDB-sourced and all fail the re-derivability test in
`infrastructure-e4kwm`.

But Movies are **deterministic today and have no out-of-band writer at all** — every value rides in
the `Movie_added_to_library` payload and nothing ever refreshes it. The defect is purely latent: the
data is frozen at add time and silently stale, not drifting.

Cutting over now buys zero behaviour change, zero drift reduction and zero data-loss protection, at
the cost of a full read-path refactor of `MovieProjection.getBySlug` / `getAll`.

## What

`movie_metadata_cache` already ships empty and unread from `administration-c3nvp`, registered as
`Cache "(none yet)"`. Cut over when a movie-refresh feature is actually built, at which point this is
a prerequisite rather than speculative infrastructure.

Follow the `series-q8jwc` shape: join in the query function, not at the API layer; identity-card fields
(`name`, `year`, `poster_ref`, `genres`) stay as projection columns under the identity-card clause;
`COALESCE(cache, projection)` on those, nullable reads on cache-only fields.

## Acceptance criteria

_Written 2026-08-04 following the shipped series-q8jwc / games-a7dqx cutover shape; the
trigger condition in Notes still governs when this is promotable._

- [ ] `movie_metadata_cache` gains typed columns for `tmdb_rating`, `overview`, `runtime`,
      `backdrop_ref`, `production_countries` (every image ref individually SELECTable —
      ADR-0045, no EAV/JSON blob), seeded once from current projection values.
- [ ] Identity-card fields (`name`, `year`, `poster_ref`, `genres`) stay projection columns;
      reads use `COALESCE(cache, projection)` on them and nullable cache reads on
      cache-only fields, joined in `MovieProjection.getBySlug`/`getAll`'s query functions,
      not at the API layer.
- [ ] The four-part tolerance rule is applied to the demoted TMDB-sourced events: codec
      kept, aggregate arm explicit no-op, projection arm deleted with columns dropped,
      commands deleted so the compiler finds every emission site.
- [ ] No `ProjectionHandler` reads the cache (`grep -rn "MetadataCache" src/Server/*Projection.fs`
      stays empty — c3nvp's hard constraint).
- [ ] `checkProjectionDrift` reports zero discrepancies for `MovieProjection` after cutover.
- [ ] `npm test` and `npm run build` pass.

## Notes

**Backlog only — do not promote until a movie-refresh feature is scheduled.** (Re-confirmed
at the 2026-08-04 refinement pass: still no out-of-band writer for Movies, so cutting over
now buys nothing — the moment a movie-refresh feature is captured, this task becomes its
`depends_on` prerequisite and gets promoted alongside it.)

**Product call to surface before it is discovered by surprise:** seeding the cache from current
projection values preserves exactly what is displayed today, but the *first* movie refresh will then
produce a visible mass update of years-stale data across the whole library.
