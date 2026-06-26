---
id: 0012
title: Jellyfin materializes missing seasons as a projection-only supplement, TMDB stays authoritative
scope: integration
status: accepted
date: 2026-06-26
related_tasks: [integration-m4k7p]
---

# ADR 0012: Jellyfin materializes missing seasons as a projection-only supplement, TMDB stays authoritative

## Context
The app sources series episode metadata **only** from TMDB. When TMDB lags on a new
season (its catalog is community-edited, so a season can appear weeks late or never), the
episodes have no path into the app even when they sit on the user's Jellyfin server.
Reproduced live (2026-06-26): *Interview with the Vampire* S3 — three episodes on Jellyfin,
TMDB lists only two seasons; manual refresh, nightly refresh, and the Jellyfin watch-history
sync all fail to surface S3 (the spike integration-005 diagnosed why and chose the direction;
integration-006 fixed only candidate selection).

The direction — "Jellyfin as a self-healing supplement to TMDB" — was already decided in the
integration-005 spike. This ADR records the *shape* of the implementation, resolved against
the real code during refinement.

## Decision
Materialize missing season/episode metadata from the data the Jellyfin sync already fetches,
keeping TMDB authoritative.

- **Provenance is a projection column, not a new event.** Episode/season *metadata* is
  already a projection-level cache refreshed imperatively: `Series.evolve` for
  `Series_refreshed` deliberately ignores episode/season detail, and
  `SeriesRefresh.refreshOne` writes episodes via `applyToProjection` directly, separate from
  the event append. Materialization mirrors that — a projection write tagged with provenance.
  A `source TEXT NOT NULL DEFAULT 'tmdb'` column was added to `series_episodes` and
  `series_seasons` via the existing try/ALTER migration idiom. TMDB writes leave the default;
  the Jellyfin pass writes explicit `'jellyfin'`. **No event-stream change** — this is a
  deliberate divergence from the event-sourced watch-history path (`Mark_episode_watched`),
  justified because metadata is already a rebuildable read-model cache, not aggregate state.
- **Enrichment is silent and free.** Dedup/enrichment key is the existing PK
  `(series_slug, season_number, episode_number)` — a materialized row has no TMDB id, so the
  aired number-pair is the only viable join. When TMDB later publishes the season,
  `applyToProjection`'s `INSERT OR REPLACE` does **not** list `source` in its column set, so
  SQLite resets it to the `'tmdb'` DEFAULT on replace. The "metadata pending" flag flips to
  false with no second code path and no duplicate row. Watch progress is untouched (it lives
  in the separate `series_episode_progress` table). The Jellyfin write uses `INSERT OR IGNORE`
  so it can never clobber an authoritative TMDB row.
- **Source-of-truth precedence: TMDB authoritative, Jellyfin a stop-gap.** Jellyfin fills the
  gap until TMDB catches up (or permanently, if it never does). TMDB always wins on overwrite.
- **Synthetic season container is mandatory.** The detail read iterates `series_seasons` then
  queries episodes per season; an episode materialized into a season with no `series_seasons`
  row would orphan and never render. So a number-only synthetic season row
  (`source='jellyfin'`, name `"Season N"`) is upserted first for any new season.
- **Pure, injected-effect core.** `JellyfinImport.materializeMissingEpisodes` mirrors
  `syncSeriesWatchHistory`: it takes the existing-keys lookups, a best-effort still fetch, and
  the season/episode writers as lambdas, so it is unit-testable without HTTP or SQLite, and is
  fault-isolated per-series and per-episode (consistent with ADR 0010). It runs **before** the
  watch-history sync so a played materialized episode's progress attaches to an existing row
  and next-up recompute sees it. Not gated on `Played` — present-on-server is enough.
- **Client carries no provider knowledge.** The read derives a semantic
  `EpisodeDto.MetadataPending = (source = "jellyfin")`; the SeriesDetail episode list shows a
  subtle styleguide-governed "metadata pending" badge while pending.

## Consequences
- Episodes on Jellyfin but missing from TMDB now appear in the app at zero new external
  dependency cost, and self-heal (enrich + clear the badge) once TMDB adds the season.
- Materialized rows are projection-only: a full projection rebuild drops them and the next
  sync re-creates them — identical to how TMDB-refreshed episodes already behave.
- Still images are deferred (integration-007): the materialization seam fetches stills
  best-effort but the v1 wiring returns `None`, so materialized stills are `NULL` until TMDB
  enriches. This was sanctioned by the task's scope note.
- Provenance caveat: the fix rests on the empirical fact that the user's Jellyfin holds the
  missing season. If a future Jellyfin library runs only the default TMDB scraper it could
  share TMDB's lag — non-blocking, since the fallback still strictly improves on TMDB-only.
