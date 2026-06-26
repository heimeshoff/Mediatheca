---
id: integration-m4k7p
title: Materialize a missing season/episode from Jellyfin when TMDB lacks it
status: backlog
type: feature
context: integration
created: 2026-06-26
completed:
depends_on: []
blocks: []
tags: [jellyfin, tmdb, series, sync, metadata, fallback, materialize, season]
related_adrs: []
related_research: [tv-series-metadata-fallback-sources-2026-06-26]
prior_art: [integration-005, integration-006]
---

## Why

The app surfaces series episodes **only** from TMDB. `SeriesRefresh.fetchFromTmdb`
reads `details.Seasons` straight from TMDB (`src/Server/SeriesRefresh.fs:74`) and upserts
only what TMDB returns; the Jellyfin sync records **watch history** for *already-existing*
episodes (`JellyfinImport.syncSeriesWatchHistory` → `Mark_episode_watched`) but never
creates season/episode metadata. So when TMDB lags on a new season — community-edited, a
season only appears once a volunteer adds it — there is **no path at all** for the episodes
to enter the app, even though they are sitting on the user's Jellyfin server.

Real, reproduced case (2026-06-26): *Interview with the Vampire* season 3 has three
episodes on the user's Jellyfin server, but TMDB still lists only two seasons. Result:

- **Manual "Refresh from TMDB"** returns nothing (it ignores status, so its silence
  *confirms* TMDB genuinely has no S3).
- **Nightly refresh** re-fetches TMDB → `NewEpisodeCount = 0` → `Series.decide` emits no
  event (`src/Server/Series.fs:518`).
- **Jellyfin sync** only writes watch rows for *played* episodes and never materializes the
  season metadata.

[[integration-006]] fixed *candidate selection* (an `Ended` series now re-enters the nightly
TMDB candidate set when engaged-with), but that does nothing here because the data isn't in
TMDB to find. [[integration-005]] researched the fix and **decided the direction** —
"Jellyfin-as-source, supplementing TMDB" — but its closing note ("Next step: capture the
follow-up implementation task") was never acted on. This is that task.

## What

When a Jellyfin sync sees an episode (or whole season) the TMDB-fed projection lacks,
**materialize** the corresponding season/episode metadata from the Jellyfin data the sync
already fetches — keeping TMDB authoritative, so the nightly refresh later backfills/enriches
those rows once a volunteer adds the season upstream.

Direction and shape are fixed by the [[integration-005]] spike + its research report
(`tv-series-metadata-fallback-sources-2026-06-26`):

- **Source:** the user's already-integrated Jellyfin library — zero new external dependency,
  reuses `src/Server/Jellyfin.fs` (auth + re-auth policy already exist).
- **Mapping is essentially free** — `/Shows/{id}/Episodes` returns `ParentIndexNumber`→
  SeasonNumber, `IndexNumber`→EpisodeNumber, `PremiereDate`→AirDate, `Name`/`Overview`/
  `RunTimeTicks`, `ImageTags.Primary`→still. Maps onto the existing
  `Series.SeasonImportData` / `EpisodeImportData` types with no remap layer (Jellyfin uses
  aired S/E numbering, matching the TMDB-seeded `series_episodes` rows).
- **Supplement, do not replace** TMDB — mark materialized rows so the nightly TMDB refresh
  enriches them later (richer imagery, ratings, overviews) rather than fighting them.

## Acceptance criteria

- [ ] During a Jellyfin sync, when the fetched Jellyfin episodes for a matched series include
      a (season, episode) the projection has no metadata row for, the season/episode is
      materialized into the series' projection (it now appears in the app as an episode of
      that series), not just recorded as watch progress.
- [ ] A newly-materialized season/episode is flagged as Jellyfin-sourced / pending TMDB
      enrichment, so a subsequent TMDB refresh that *does* deliver the season **enriches**
      (does not duplicate) the materialized row. Decide and document the dedup/enrichment key
      (TMDB id vs aired S/E number).
- [ ] The *Interview with the Vampire* S3 case is covered end-to-end: with TMDB still at two
      seasons and three S3 episodes present on Jellyfin, a sync makes S3E1–E3 appear in the
      app, with watch state correctly attached for any of them already played on Jellyfin.
- [ ] Materialization is fault-isolated like the rest of the Phase-2 sync (a bad episode
      records an error and continues — does not abort the run), consistent with
      [[integration-001]].
- [ ] Tests exercise: (a) episode present on Jellyfin but absent from projection →
      materialized; (b) episode already in projection → not duplicated; (c) later TMDB refresh
      enriches the materialized row rather than creating a second one.

## Notes

- **Adapter widening (from [[integration-005]] §Notes, not a blocker):** the episode decoder
  currently reads only `Tmdb`/`Imdb` provider IDs and requests `Fields=ProviderIds`
  (`Jellyfin.fs:220-230`). This task adds `PremiereDate` + `ImageTags` to the decoder and
  widens the episode `Fields=` query so the materialized rows carry air date + still.
- **Watched vs present:** today's sync loop only iterates *played* episodes
  (`JellyfinImport.fs:56`, gated on `epPlayed`). Materialization must consider episodes that
  are **present but unwatched** too (the user may not have watched all three S3 episodes yet),
  so this likely widens what the sync enumerates — or adds a parallel "present on server"
  pass. Resolve during refinement.
- **Provenance caveat (research Open Question 4):** the recommendation rests on the empirical
  fact that the user's Jellyfin holds the missing season, not on TheTVDB provenance. If the
  Jellyfin library runs only the default TMDB scraper it could share TMDB's lag on a *future*
  title — worth a one-line check of the Jellyfin library provider config, but it does not
  block this task (the fallback still strictly improves on TMDB-only and costs nothing new).
- **Where it slots in:** `Api.runJellyfinImport` Phase 2 already fetches episodes per matched
  series into `seriesBatch` (`src/Server/Api.fs:925-939`) before delegating watch-history
  writes to `JellyfinImport.syncSeriesWatchHistory`. The materialization step reuses that same
  fetched batch — diff it against the projection's known (season, episode) set before/alongside
  the watch-history write.
- **Sizing (from spike):** reuse-Jellyfin-adapter path ≈ **S** for the watch-attach case; the
  metadata-materialization + enrichment-dedup is the new work here, likely **S–M**.
- Composes with [[integration-006]]: 006 makes TMDB's eventual season auto-discovered once it
  exists; this task covers the window (possibly permanent) where TMDB never delivers it.
- Refinement candidate: whether the "mark for later TMDB enrichment" flag deserves an ADR
  (new projection column + event-shape question) or stays an inline implementation detail.
