---
id: 0040
title: Jellyfin still backfill for existing rows lives inside materializeMissingEpisodes, with no refetch guard
scope: integration
status: accepted
date: 2026-08-01
related_tasks: [integration-q7wv3]
---

# ADR 0040: Jellyfin still backfill for existing rows lives inside materializeMissingEpisodes, with no refetch guard

## Context

`integration-007` wired a real Jellyfin still fetch (`Jellyfin.getPrimaryImageWithReauth`
+ `JellyfinImport.fetchEpisodeStill`, ADR 0039's distinct `-jellyfin.jpg` path) into
`materializeMissingEpisodes`, but only reaches an episode row at the moment it is
**created**. Every row materialized by an earlier sync — while the fetch was still ADR
0012's stub — is permanently stuck at `still_ref = NULL`: `materializeMissingEpisodes`
skips any `(season, episode)` already present in `getExistingEpisodeKeys`, and
`materializeEpisode` is `INSERT OR IGNORE`, which cannot fill a column on an existing
row. Confirmed live: *Interview with the Vampire* S3 held seven such rows, all showing
the placeholder TV icon.

Two shape questions needed an answer: where does the backfill logic live, and what
happens to an episode Jellyfin genuinely has no image for.

## Decision

### The backfill lives inside `materializeMissingEpisodes`, as a widened skip predicate — not a separate sweep

The existing `if existingKeys |> Set.contains key then () else <materialize>` becomes
`if existingKeys |> Set.contains key then <backfill-if-candidate> else <materialize>`.
The `else` branch is byte-for-byte unchanged — a genuinely new episode still gets its
still at materialization time, in one pass.

Reasons a separate sweep was rejected:
- The Jellyfin item id the fetch needs (`ep.Id`) is already in hand in `seriesBatch`. A
  sweep driven by a `WHERE source='jellyfin' AND still_ref IS NULL` query would have to
  re-resolve it through `jellyfin_episode` — a table `clearAll` wipes and Phase 1 only
  repopulates for TMDB-matched series, i.e. a new dependency on conditionally-populated
  state, for data the batch already carries for free.
- A separate sweep needs its own fault isolation, result type, and `Api.fs` wiring,
  duplicating machinery this function already has and already tests.
- The bug *is* this function's skip predicate — there is no fix that doesn't touch it.

Concrete shape: `SeriesProjection.getJellyfinEpisodesMissingStill` (`SELECT
season_number, episode_number FROM series_episodes WHERE series_slug = @slug AND
source = 'jellyfin' AND still_ref IS NULL`) supplies the per-series candidate set
alongside the existing `getExistingEpisodeKeys`. The write path is a dedicated `UPDATE`
— `SeriesProjection.backfillEpisodeStill` — repeating `source = 'jellyfin' AND
still_ref IS NULL` **in the WHERE clause**, not only at candidate-selection time: a
TMDB refresh landing between the candidate SELECT and this UPDATE (which resets
`source` to `'tmdb'` via `SeriesRefresh`'s `INSERT OR REPLACE`, per ADR 0012) makes the
UPDATE a no-op instead of clobbering a row TMDB has since enriched.

`SeriesMaterializeResult` gained `StillsBackfilled: int`, distinct from
`EpisodesMaterialized`, so the backfill is observable in the sync result.

### No refetch guard — repetition across sync runs is an accepted, self-draining tradeoff

An episode Jellyfin genuinely has no primary image for is re-attempted on every sync,
forever. This was a deliberate choice, not an oversight:

- The candidate set is only Jellyfin-materialized rows, which exist only where TMDB
  lags a season — seven rows total across the whole library at the time of writing.
- The set drains on its own: a TMDB refresh's `INSERT OR REPLACE` omits the `source`
  column, resetting it to the `'tmdb'` default (ADR 0012) — a row leaves the candidate
  set once TMDB publishes the season, even if TMDB itself has no still either.
- The per-attempt cost is one LAN GET that 404s, already fault-isolated
  (`try fetchStill ... with _ -> None`) and already degrading to `None`. The sync is
  client-initiated behind a cooldown, not a tight background loop.
- Each candidate is attempted at most once per sync run — the repetition is across
  runs, not within one.

**Alternatives considered and rejected:**
- **Sentinel `still_ref` as a tried-and-failed marker.** Ruled out on hard evidence:
  `("series_episodes", "still_ref")` is entry 8 of ADR 0025's `imageRefColumns`
  registry, whose `getReferencedImageRefs` collects every non-null, non-empty value as
  a live reference. A sentinel string would register as a reference to a file that does
  not exist on disk, polluting a registry ADR 0025 calls LOAD-BEARING — the one column
  that looks like free storage for this marker is the one column that must not hold a
  non-path value.
- **A side table for permanently image-less episodes.** Correct, and the escalation
  path if the accepted repetition ever becomes visible in practice. Declined now as
  disproportionate: new projection state plus its own ADR, in a BC classified generic
  ("boring plumbing where boring choices are correct"), to save a handful of 404s
  against a LAN server.
- **Pre-filtering candidates on `PrimaryImageTag`** (already parsed and sitting unused
  in `seriesBatch`). Declined because whether `ImageTags` is populated on the
  `/Shows/{id}/Episodes` response is unverified, and the failure mode runs the wrong
  direction: if the field is unpopulated, every candidate reads `None`, the backfill
  silently skips everything, and the bug survives with no error and no 404 to notice.
  An unconditional attempt fails loudly-enough (a wasted GET) rather than quietly.

If the accepted repetition ever becomes a visible cost — a large image-less library
making the sequential `Async.RunSynchronously` fetches a drag on sync duration — the
escalation is the side table above, with its own ADR at that time.

## Consequences

- Episodes materialized before `integration-007`'s fetch was wired up now get a real
  thumbnail on the next Jellyfin sync, closing the gap that task's scope never reached.
- `materializeMissingEpisodes` gained two new injected-effect parameters
  (`getJellyfinEpisodesMissingStill`, `backfillStill`), extending — not replacing —
  the existing `fetchStill` seam from ADR 0039/integration-007.
- A permanently image-less Jellyfin-materialized episode costs one wasted LAN GET per
  sync, forever, until TMDB eventually enriches the row (with or without its own
  still). Accepted as bounded and cheap; revisit only if volume or visible sync latency
  ever makes it matter.
- Governing decisions unchanged: ADR 0012 (materialize-as-projection-supplement,
  TMDB stays authoritative — the WHERE-clause re-check is a direct application of its
  enrichment-resets-`source` behaviour), ADR 0039 (distinct `-jellyfin.jpg` path and
  its accepted orphan — unchanged, the backfill reuses the same `fetchEpisodeStill`),
  ADR 0025 (image-ref registry — the reason the sentinel alternative was ruled out),
  ADR 0011 (re-auth policy the fetch reuses unchanged). None is challenged; this ADR
  records the backfill's own shape and its no-refetch-guard tradeoff, which — unlike
  the storage-path decision in ADR 0039 — has no prior ADR covering it.

## References
- `src/Server/JellyfinImport.fs` — `materializeMissingEpisodes` (widened skip
  predicate), `SeriesMaterializeResult.StillsBackfilled`.
- `src/Server/SeriesProjection.fs` — `getJellyfinEpisodesMissingStill`,
  `backfillEpisodeStill`.
- `src/Server/Api.fs` — `runJellyfinImport` wiring.
- `tests/Server.Tests/JellyfinMaterializeTests.fs` — backfill test coverage,
  including a real-SQL test of the WHERE-clause re-check.
- ADR 0012 — the materialize-as-supplement decision this backfill completes.
- ADR 0039 — the still storage path and fetch this backfill reuses unchanged.
- ADR 0025 — the orphan-scan registry that rules out a sentinel `still_ref`.
- ADR 0011 — the re-auth policy `getPrimaryImageWithReauth` reuses.
