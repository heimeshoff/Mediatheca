---
id: administration-c3nvp
title: Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents
status: done
type: feature
context: administration
created: 2026-08-01
completed: 2026-08-01
depends_on: [infrastructure-e4kwm, administration-t9bzx]
blocks: []
tags: [metadata, cache, projection, determinism]
related_adrs: [0012, 0025, 0031, 0033, 0045]
related_research: []
prior_art: [administration-xx3mw, integration-m4k7p]
---

## Why

Third-party metadata needs a home that survives `handler.Drop; handler.Init; replay`. This tier is
not a new concept in this codebase — `src/Server/ImageStore.fs` and `src/Server/JellyfinStore.fs` are
both already exactly this shape: durable, non-projection, ref/slug-addressed, seeded once from
projections, joined back at read time. `JellyfinStore.migrateFromProjections` is a working, shipped
template for the seeding half.

## What

- New `src/Server/MetadataCache.fs`, inserted in `Server.fsproj` **immediately after
  `JellyfinStore.fs` (line 34)** — after `ImageStore` / `SettingsStore`, before every `*Projection.fs`
  (so query functions can call it) and before `SeriesRefresh.fs` (line 45, so it can write it).
  `JellyfinImport.fs` (line 30) sits earlier and does not need it: ADR-0012 already made
  `materializeMissingEpisodes` take its writers as injected lambdas, wired in `JellyfinSync.fs`.
- `MetadataCache.initialize (conn) : unit`, called from `Composition.buildApp` beside
  `JellyfinStore.initialize conn` (~line 109) and **never** from any `ProjectionHandler.Init`.
- Create `game_metadata_cache` and `movie_metadata_cache` with **typed DDL** — every image ref as a
  real, individually-`SELECT`able `TEXT` column. **Not EAV, not a JSON blob:** an EAV or blob table
  hides image refs from `Administration.getReferencedImageRefs`, and the ADR-0025 orphan purge would
  then hard-delete every poster.
- `movie_metadata_cache` ships **empty and unread** — four lines of DDL that make the taxonomy honest.
  Cutover is `movies-v2gkh`.
- `MetadataCache.seedFromProjections`, gated on a `SettingsStore` marker `metadata_cache_seeded` —
  **not** a permanently-swallowed `try/with`. The marker is what makes retirement explicit and greppable.
- `fetched_at TEXT` **nullable** on every cache table: it cannot be `NOT NULL` given the
  `ALTER TABLE ADD COLUMN` seeding path, and NULL carries real meaning — *"seeded from the projection,
  never actually fetched"* — exactly the cohort a first refresh should prioritize.
- Register every new table as `Cache` in `tableRegistry` (`administration-t9bzx`).

**Hard constraint, load-bearing:** a projection handler may **never** read the metadata cache.
Injecting a cache-reader seam into `ProjectionHandler` would degrade ADR-0031's "read-only against
live holds **by construction**" to a code-review property, and would let the nightly TMDB refresh race
the drift check into false positives (the cache has no checkpoint, so `isAnyProjectionDirty` cannot
detect it).

## Acceptance criteria

- [x] Expecto: after `initialize` on a fresh fixture, all cache tables exist with the declared primary keys, asserted via `PRAGMA table_info`.
- [x] Expecto: `initialize` is idempotent — running it twice changes no schema and throws nothing.
- [x] Expecto: `seedFromProjections` run twice inserts rows only on the first run and sets the `metadata_cache_seeded` marker.
- [x] Expecto: `checkProjectionDrift` returns results identical to before with the cache tables present (they are classified `Cache` and are never diffed).
- [x] Expecto: `Projection.rebuildProjection` over every handler leaves every cache table's row count unchanged.
- [x] `grep -rn "MetadataCache" src/Server/*Projection.fs` returns zero matches — no projection handler reads the cache.
- [x] `npm test` passes; `npm run build` passes.

## Outcome

Stood up `src/Server/MetadataCache.fs` (inserted in `Server.fsproj` immediately after
`JellyfinStore.fs`): `MetadataCache.initialize` creates `game_metadata_cache` (typed columns mirroring
`game_detail`'s RAWG/HowLongToBeat-sourced fields — description, short_description, website_url,
cover_ref, backdrop_ref, rawg_id, rawg_rating, hltb_hours, hltb_main_plus_hours,
hltb_completionist_hours, fetched_at) and `movie_metadata_cache` (movie_slug PK + fetched_at only —
ships empty and unread, per the task's Notes, until `movies-v2gkh`). `MetadataCache.seedFromProjections`
copies `game_detail`'s current values into `game_metadata_cache` once, gated on a `SettingsStore`
`metadata_cache_seeded` marker (never re-seeds after the marker is set, even if new games are added).
Wired into `Composition.buildApp`: `initialize` beside `JellyfinStore.initialize`; `seedFromProjections`
after `Projection.startAllProjections` (so `game_detail` is guaranteed to exist). Both tables registered
`Cache` in `Administration.tableRegistry` (`game_metadata_cache` → `"MetadataCache"`,
`movie_metadata_cache` → `"(none yet)"`); `TableClassificationTests.fs`'s `bootstrapEverything` updated
to call `MetadataCache.initialize` so the registry-coverage test stays honest. New
`tests/Server.Tests/MetadataCacheTests.fs` (5 tests) covers schema/idempotence/seeding-once-with-marker/
zero-drift-with-cache-present/rebuild-leaves-cache-untouched. `grep -rn "MetadataCache"
src/Server/*Projection.fs` returns zero matches. Full suite: 454/454 passing (`--sequenced`).
`npm run build` passes. ADR-0045 records the typed-DDL-not-EAV decision and the marker-gated seeding
choice; BC README's ubiquitous language gained a "Metadata cache tier" entry.

## Notes

**ADR:** *"Third-party metadata lives in per-BC typed cache tables that survive projection rebuild"*,
`scope: administration`. Records the EAV rejection with the image-orphan-purge data-loss argument, the
`ImageStore` / `JellyfinStore` precedents, the identity-card override vs cache-only field split, and
the hard constraint above.

Name it the **metadata cache**, never a "metadata projection". The fossil of the exact moment those
two concepts fused is in `.agentheim/contexts/administration/README.md` and ADR-0012: *"metadata is
already a projection-level cache."*
