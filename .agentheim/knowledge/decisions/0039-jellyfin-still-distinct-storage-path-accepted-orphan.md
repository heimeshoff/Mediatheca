---
id: 0039
title: Jellyfin-materialized stills use a distinct storage path; the resulting post-enrichment orphan is accepted and reclaimed via the ADR-0025 orphan scan
scope: integration
status: accepted
date: 2026-08-01
related_tasks: [integration-007]
---

# ADR 0039: Jellyfin-materialized stills use a distinct storage path; the resulting post-enrichment orphan is accepted and reclaimed via the ADR-0025 orphan scan

## Context

ADR 0012 deferred episode stills for Jellyfin-materialized episodes (episodes
present on Jellyfin but missing from TMDB — the "metadata pending" case):
`JellyfinImport.materializeMissingEpisodes`' injected `fetchStill` seam
existed, but the v1 wiring in `Api.runJellyfinImport` passed a stub returning
`None`, so materialized rows carried `still_ref = NULL` until a later TMDB
refresh enriched them. integration-007 closes that deferral: it wires a real
Jellyfin image fetch (`Jellyfin.getPrimaryImageWithReauth`, built on the
ADR-0011 `withReauthRetry` re-auth policy) and a save step
(`JellyfinImport.fetchEpisodeStill`) behind the existing seam.

Two questions had to be answered while doing that: where to store the
Jellyfin-sourced file on disk, and what happens to that file once TMDB later
enriches the episode and takes over as the still's source of truth.

## Decision

### Store Jellyfin stills at a distinct path, never TMDB's canonical path

Jellyfin-materialized stills are stored at
`stills/{slug}-s%02de%02d-jellyfin.jpg` — a suffixed sibling of TMDB's
canonical `stills/{slug}-s%02de%02d.jpg`, not the same path.

This is load-bearing, not cosmetic. `SeriesRefresh.fs:99-110` short-circuits
its own TMDB still download on `ImageStore.imageExists imageBasePath ref`: if
a TMDB refresh sees a file already sitting at its canonical path, it skips
the download entirely and reuses whatever bytes are already there. If the
Jellyfin pass wrote to TMDB's canonical path, a later TMDB enrichment of that
same episode would find the path already occupied by Jellyfin's bytes, skip
its own fetch, and permanently keep the lower-resolution/differently-sourced
Jellyfin image under a ref that claims to be TMDB's. The distinct suffix
keeps TMDB's existence check missing on its own canonical path, so TMDB
always performs its own download and `INSERT OR REPLACE` always repoints
`still_ref` at the canonical file — the enrichment path from ADR 0012 keeps
working unmodified, with zero changes to `SeriesRefresh.fs` / `Tmdb.fs`.

**Alternatives considered and rejected:**
- **Write to TMDB's canonical path directly.** Rejected: this is exactly the
  collision above — it would silently and permanently defeat TMDB
  enrichment for the still, with no test-visible failure at the point a
  future edit removed the suffix.
- **Same canonical path, but delete-then-let-TMDB-redownload (cleanup on
  enrich).** Rejected: would require `SeriesRefresh.fs` (or the enrichment
  `INSERT OR REPLACE` write path) to learn about Jellyfin provenance and
  proactively evict the old file before its own download — coupling a TMDB
  adapter concern to Jellyfin's existence, for a problem the distinct-suffix
  approach avoids without touching `SeriesRefresh.fs` at all.
- **Pre-check `PrimaryImageTag` before attempting the fetch, to avoid
  fetching for episodes with no image.** Rejected as unnecessary complexity:
  materialization only runs for episodes missing from the projection (a
  handful per sync), so an unconditional fetch attempt is cheap, and it's
  robust against `ImageTags` not being populated on the
  `/Shows/{id}/Episodes` response Jellyfin returns. A missing image simply
  404s and the seam degrades to `None` (see below).

### The still fetch is strictly best-effort and never a sync error

Any failure in the fetch/save path (no primary image, non-2xx, decode
failure, thrown exception) degrades to `still_ref = NULL`; nothing is
appended to `materializeResult.Errors`, `Failed` stays `false`, and the sync
never becomes `SyncFailed` on account of a missing still. This mirrors ADR
0010's per-item fault isolation and keeps stills a pure enhancement over the
metadata materialization ADR 0012 already ships.

### The post-enrichment `-jellyfin.jpg` orphan is an accepted tradeoff, reclaimed via the ADR-0025 orphan scan, not proactively cleaned up

Once a TMDB refresh enriches a previously Jellyfin-materialized episode, its
`still_ref` is reset to the canonical TMDB path (per ADR 0012's
`INSERT OR REPLACE` enrichment behaviour) — the `-jellyfin.jpg` file on disk
is no longer referenced by any projection column. Nothing deletes it at
enrichment time.

This is a deliberate, accepted tradeoff, not an oversight: proactively
deleting the orphaned file at enrichment time would require
`SeriesRefresh.fs`'s TMDB write path to know about and reach into Jellyfin
storage conventions (the `-jellyfin.jpg` suffix), coupling a TMDB adapter
concern to Jellyfin provenance for no user-visible gain — the same reasoning
that rejected the "cleanup on enrich" alternative above. The per-file cost is
small (a few KB, JPEG at `maxWidth=600`) and bounded to only the episodes
Jellyfin materialized that TMDB *eventually* publishes — not every
materialized episode, and not a growing-without-bound set for any one
series.

**Interaction with ADR-0025 (Administration's image-cache orphan
scanner):** this is not a permanent leak. ADR-0025's orphan detection reads
live refs directly from projection columns, including
`series_episodes.still_ref` — after enrichment that column holds only the
canonical TMDB path, so the abandoned `-jellyfin.jpg` file is exactly the
kind of file ADR-0025's `/admin/images` tab is built to find and reclaim: it
will appear in a future orphan scan (once the six checkpoint-tracked
projections are not dirty) and can be purged by an operator through the
existing hard-delete flow. No new administration code is needed — the
orphan already falls inside ADR-0025's existing `imageRefColumns` coverage
of `series_episodes.still_ref`. Reclamation is manual/operator-triggered
rather than automatic, consistent with how ADR-0025 already treats every
other orphan source in the cache.

## Consequences

- Jellyfin-materialized episodes render a real thumbnail instead of the
  placeholder TV icon as soon as they're materialized, closing the ADR 0012
  deferral, without weakening TMDB's authority over the still once TMDB
  catches up.
- A small number of `-jellyfin.jpg` files accumulate on disk, unreferenced,
  for episodes Jellyfin materialized and TMDB later enriched. They are
  invisible to the app (no projection column points at them) but are
  reachable and reclaimable through Administration's orphan scan/purge
  (ADR-0025) — revisit only if this volume ever grows enough to matter.
- A future maintainer "tidying" the `-jellyfin.jpg` suffix to match TMDB's
  canonical naming would silently and permanently reintroduce the collision
  this ADR exists to prevent, with no test failing at the point of that
  edit; `tests/Server.Tests/JellyfinStillTests.fs` covers the *enrichment*
  behaviour under the current distinct path, but a renamed-to-canonical path
  would still pass those tests trivially differently — the guard against
  regression is this record, not a test that can distinguish "distinct path"
  from "same path" by construction.

## References
- `src/Server/Jellyfin.fs` — `fetchImageBytesWithAuth`, `getPrimaryImageWithReauth`.
- `src/Server/JellyfinImport.fs` — `fetchEpisodeStill`.
- `src/Server/Api.fs` — `runJellyfinImport` wiring (`:969-980`).
- `src/Server/SeriesRefresh.fs:99-110` — the `ImageStore.imageExists` short-circuit this ADR's path choice must not collide with.
- `tests/Server.Tests/JellyfinStillTests.fs` — fetch/save/enrichment test coverage.
- ADR 0012 — the deferral this task closes; its Consequences section is amended alongside this ADR.
- ADR 0011 — the re-auth policy `getPrimaryImageWithReauth` reuses.
- ADR 0010 — the per-item fault isolation policy the best-effort degrade follows.
- ADR 0025 — Administration's image-cache orphan detection/purge, which reclaims the accepted orphan.
