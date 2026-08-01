---
id: administration-c3nvp
title: Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents
status: todo
type: feature
context: administration
created: 2026-08-01
completed:
depends_on: [infrastructure-e4kwm, administration-t9bzx]
blocks: []
tags: [metadata, cache, projection, determinism]
related_adrs: [0012, 0025, 0031, 0033]
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

- [ ] Expecto: after `initialize` on a fresh fixture, all cache tables exist with the declared primary keys, asserted via `PRAGMA table_info`.
- [ ] Expecto: `initialize` is idempotent — running it twice changes no schema and throws nothing.
- [ ] Expecto: `seedFromProjections` run twice inserts rows only on the first run and sets the `metadata_cache_seeded` marker.
- [ ] Expecto: `checkProjectionDrift` returns results identical to before with the cache tables present (they are classified `Cache` and are never diffed).
- [ ] Expecto: `Projection.rebuildProjection` over every handler leaves every cache table's row count unchanged.
- [ ] `grep -rn "MetadataCache" src/Server/*Projection.fs` returns zero matches — no projection handler reads the cache.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"Third-party metadata lives in per-BC typed cache tables that survive projection rebuild"*,
`scope: administration`. Records the EAV rejection with the image-orphan-purge data-loss argument, the
`ImageStore` / `JellyfinStore` precedents, the identity-card override vs cache-only field split, and
the hard constraint above.

Name it the **metadata cache**, never a "metadata projection". The fossil of the exact moment those
two concepts fused is in `.agentheim/contexts/administration/README.md` and ADR-0012: *"metadata is
already a projection-level cache."*
