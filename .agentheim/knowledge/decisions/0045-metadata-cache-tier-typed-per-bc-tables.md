---
id: 0045
title: Third-party metadata lives in per-BC typed cache tables that survive projection rebuild
scope: administration
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [administration-c3nvp]
related_research: []
---

# ADR 0045: Third-party metadata lives in per-BC typed cache tables that survive projection rebuild

## Context

ADR-0043 (event-worthiness doctrine, global scope) established the test — an event records an
observation of the user's own engagement, a cache records a third party's description — and named
the defect: `SeriesRefresh.applyToProjection` writes TMDB-fetched episode/season metadata directly
into projection tables with no backing event, so a projection rebuild silently loses 780 refreshes'
and 23 Jellyfin-materialized episodes' worth of state. The doctrine identifies the same latent shape
in Movies (no refresh feature yet, but nothing prevents the same mistake) and in Games (`game_detail`'s
RAWG/HowLongToBeat fields, currently event-carried and therefore technically passing the identity-card
clause today, but flagged `Cache` in ADR-0043's classification table on doctrinal grounds regardless).

`src/Server/ImageStore.fs` and `src/Server/JellyfinStore.fs` already prove out the correct shape for
this kind of data in this codebase: durable, non-projection, slug-addressed tables, seeded once from
projections (`JellyfinStore.migrateFromProjections`), read at query time by whichever module needs
them, and — critically — never touched by a `ProjectionHandler`'s `Init`/`Drop`, so `Drop; Init;
replay` (projection rebuild) and the ADR-0031 drift detector's shadow replay never see them at all.

Three BC-scoped cutover tasks (`series-m7fdk`, `movies-v2gkh`, `games-a7dqx`) each need this tier to
already exist before they can move their own fields into it. This task builds the tier and its seeding
machinery; it does not perform any cutover itself.

## Decision

### Typed DDL, never EAV or a JSON blob

`MetadataCache.fs` creates `game_metadata_cache` and `movie_metadata_cache`, each with **every field —
including every image ref — as a real, individually-`SELECT`able typed column**. An EAV table or a
JSON blob column was considered and rejected: `Administration.getReferencedImageRefs` (ADR-0025) reads
image refs via `SELECT DISTINCT <column> FROM <table>` against a hand-maintained
`(table, column)` registry (`imageRefColumns`) — a ref hidden inside an EAV row or a JSON blob would be
invisible to that scan, and the ADR-0025 orphan purge would then hard-delete every poster/cover backed
by a cache-only ref the very first time it ran after a cutover. Typed columns are the only shape that
keeps that registry (and the purge's safety) honest across the cutover tasks.

- `game_metadata_cache (game_slug PK, description, short_description, website_url, cover_ref,
  backdrop_ref, rawg_id, rawg_rating, hltb_hours, hltb_main_plus_hours, hltb_completionist_hours,
  fetched_at)` — mirrors `game_detail`'s existing RAWG/HowLongToBeat-sourced columns, giving
  `games-a7dqx` a real, already-seeded table to cut its read/write paths over to.
- `movie_metadata_cache (movie_slug PK, fetched_at)` — **ships empty and unread**, four lines of DDL
  that make the taxonomy honest ahead of schedule. Movies have no out-of-band metadata writer today
  (nothing refreshes movie metadata after import), so there is nothing yet to give this table a real
  column for; `movies-v2gkh` is the deferred cutover, gated on a movie-refresh feature actually being
  built.

### `fetched_at` is nullable, and NULL is meaningful

Every cache table's `fetched_at TEXT` column is nullable. It cannot be `NOT NULL` given the
`ALTER TABLE ... ADD COLUMN` migration idiom this codebase already uses to grow tables over time
(`GameProjection.fs`, `SeriesProjection.fs`) — a column added later to existing rows has no way to
retroactively supply a value. NULL is not a gap to tolerate but the intended initial state: "seeded
from the projection, never actually fetched" is exactly the cohort a first genuine refresh should
prioritize.

### Seeding is a one-time, marker-gated copy — not a swallowed exception

`MetadataCache.seedFromProjections` copies `game_detail`'s current column values into
`game_metadata_cache` via `INSERT OR IGNORE ... SELECT ...`, following the
`JellyfinStore.migrateFromProjections` template, but gated on an explicit `SettingsStore` marker
(`metadata_cache_seeded`) rather than that function's permanently-swallowed `try/with`. The marker
matters for two reasons: it makes the seed a genuine one-time operation (a second game added to the
library after the marker is set is deliberately **not** backfilled into the cache — the cache's
column values from that point on are the cutover BC's responsibility, not this seed step's), and it
makes retirement explicit and greppable (deleting the call site and the key, once every reader has cut
over, is a two-line diff instead of an archaeology exercise). `movie_metadata_cache` is never seeded —
it has no source columns to seed from yet.

Call-site ordering is load-bearing: `MetadataCache.initialize` runs at the same point as
`JellyfinStore.initialize` (early startup, schema-only), but `MetadataCache.seedFromProjections` must
run *after* `Projection.startAllProjections`, since `game_detail` is a `GameProjection`-owned table
that doesn't exist until that projection's `Init` has run.

### Hard constraint: no `ProjectionHandler` ever reads or writes this tier

Both cache tables are registered `Cache` in `Administration.tableRegistry` (ADR-0044), never
`Projected` — so `projectionTables` (derived from the registry) never lists them, `checkProjectionDrift`
never diffs them, and `Projection.rebuildProjection`'s `Drop; Init; replay` never touches them. This is
enforced by construction, not just by the registry entry: no `*Projection.fs` file references
`MetadataCache` at all (verified: `grep -rn "MetadataCache" src/Server/*Projection.fs` returns zero
matches). Injecting a cache-reader seam into a `ProjectionHandler` would degrade ADR-0031's "read-only
against live holds by construction" property to a code-review convention, and would let a nightly
RAWG/TMDB refresh race the drift check into false positives — the cache has no checkpoint, so
`Administration.isAnyProjectionDirty` has no way to detect that it's mid-write. The three per-BC
cutover tasks join the cache at their own query layer instead (`series-q8jwc`'s shape: join in the
query function, never at the API layer).

## Consequences

### Positive
- Gives `series-m7fdk`, `movies-v2gkh`, and `games-a7dqx` a concrete, tested table to cut their own
  fields over to, instead of each BC re-deriving the `ImageStore`/`JellyfinStore` shape independently.
- `game_metadata_cache` is seeded and ready — `games-a7dqx`'s cutover diff can be read/write-path-only,
  no schema work.
- Keeps the ADR-0025 orphan purge safe across every planned cutover: every image ref this tier will
  ever hold is a typed column from day one.

### Negative / accepted tradeoff
- `game_metadata_cache`'s column set is a prediction of what `games-a7dqx` will need, not that task's
  own refined acceptance criteria (which do not yet exist — it is still `backlog`). If that task's
  refinement lands on a different field set (e.g. `play_modes`, currently a JSON array and out of
  scope here), a follow-up `ALTER TABLE ADD COLUMN` will be needed — an accepted cost given the
  alternative (leaving the table schema-less until that task starts) would remove the very thing this
  task exists to hand it.
- `movie_metadata_cache` is dead weight (an unread table) until `movies-v2gkh` ships. Accepted per that
  task's own reasoning: cutting over now buys zero behavior change at the cost of a full read-path
  refactor for data that isn't drifting yet.

## Alternatives considered

- **EAV table (`entity_slug, field_name, field_value`) or a single JSON blob column per slug** —
  rejected: both hide image refs from `Administration.getReferencedImageRefs`'s typed-column scan,
  which would turn the ADR-0025 orphan purge into a data-loss hazard the instant a cutover task starts
  writing refs into the cache.
- **Seed via the same permanently-swallowed `try/with` `JellyfinStore.migrateFromProjections` uses** —
  rejected: that shape can never be distinguished from "hasn't run yet" vs. "ran and found nothing",
  and re-scans `game_detail` on every single startup indefinitely. An explicit marker makes the seed
  genuinely one-time and its eventual retirement a two-line diff.
- **Let a `ProjectionHandler` read the cache directly for its own DTO assembly** — rejected: this is
  the hard constraint above; it would make ADR-0031's read-only-by-construction guarantee a
  code-review property and open the drift-check false-positive hazard described there.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
  — the doctrine this tier implements.
- `.agentheim/knowledge/decisions/0044-every-durable-table-classified-projected-cache-imperative.md`
  — the registry both new tables are classified `Cache` in.
- `.agentheim/knowledge/decisions/0025-image-cache-orphan-detection-guard.md` — `imageRefColumns`,
  `getReferencedImageRefs`; the reason typed DDL is non-negotiable.
- `.agentheim/knowledge/decisions/0031-projection-drift-detector-throwaway-shadow-connection.md` —
  the "read-only against live holds by construction" property this tier must not degrade.
- `.agentheim/knowledge/decisions/0033-per-request-connection-factory.md` — why `initialize` is a
  one-time startup step on the bootstrap connection, not a per-request call.
- `src/Server/MetadataCache.fs`, `src/Server/Composition.fs`, `src/Server/Administration.fs`
  (`tableRegistry`) — the code this ADR describes.
- `tests/Server.Tests/MetadataCacheTests.fs`, `tests/Server.Tests/TableClassificationTests.fs` —
  schema/idempotence/seeding/drift/rebuild coverage.
- `.agentheim/contexts/series/todo/series-m7fdk-rename-episode-tree-into-cache.md`,
  `.agentheim/contexts/movies/backlog/movies-v2gkh-movie-tmdb-metadata-into-cache.md`,
  `.agentheim/contexts/games/backlog/games-a7dqx-game-attribute-metadata-into-cache.md` — the deferred
  per-BC cutover tasks this tier exists for.
- `src/Server/ImageStore.fs`, `src/Server/JellyfinStore.fs` — the precedent shape this tier follows.
