---
id: integration-m4k7p
title: Materialize a missing season/episode from Jellyfin when TMDB lacks it
status: done
type: feature
context: integration
created: 2026-06-26
completed: 2026-06-26
depends_on: [design-system-001]
blocks: []
tags: [jellyfin, tmdb, series, sync, metadata, fallback, materialize, season]
related_adrs: [0012]
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
those rows once a volunteer adds the season upstream. Materialized rows show a subtle
**"metadata pending"** badge until TMDB enriches them.

Direction and shape are fixed by the [[integration-005]] spike + its research report
(`tv-series-metadata-fallback-sources-2026-06-26`); the implementation shape below was
resolved against the real code during refinement (2026-06-26).

- **Source:** the user's already-integrated Jellyfin library — zero new external dependency,
  reuses `src/Server/Jellyfin.fs` (auth + re-auth policy already exist).
- **Mapping is essentially free** — `/Shows/{id}/Episodes` returns `ParentIndexNumber`→
  SeasonNumber, `IndexNumber`→EpisodeNumber, `PremiereDate`→AirDate, `Name`/`Overview`/
  `RunTimeTicks`, `ImageTags.Primary`→still. Jellyfin uses aired S/E numbering, matching the
  TMDB-seeded `series_episodes` rows — no remap layer.
- **Supplement, do not replace** TMDB — mark materialized rows `source='jellyfin'` so the
  nightly TMDB refresh enriches them later (richer imagery, ratings, overviews) rather than
  fighting them.

### Resolved implementation shape

- **Provenance = projection column, NOT a new event.** Episode/season *metadata* is already a
  projection-level cache refreshed imperatively — `Series.evolve` for `Series_refreshed`
  deliberately does not apply episode/season detail to aggregate state
  (`Series.fs:375-382`), and `SeriesRefresh.refreshOne` writes episodes via `applyToProjection`
  directly (`SeriesRefresh.fs:283`), separately from the event append. Materialization mirrors
  that: a projection write tagged with provenance. Add one column to each table via the
  existing try/ALTER migration idiom (`SeriesProjection.fs:94-111`):
  `ALTER TABLE series_episodes ADD COLUMN source TEXT NOT NULL DEFAULT 'tmdb';` and the same on
  `series_seasons`. TMDB writes leave the default; the Jellyfin pass writes explicit
  `'jellyfin'`. No event-stream change. (Projection-only rows drop on a full rebuild and
  re-create on the next sync — identical to how TMDB-refreshed episodes already behave.)
- **Dedup / enrichment key = the existing PK `(series_slug, season_number, episode_number)`**
  (`SeriesProjection.fs:71`). A materialized row has no TMDB id, so the aired number-pair is
  the only viable join key. **The flag clears for free:** TMDB's upsert
  (`INSERT OR REPLACE`, `SeriesRefresh.fs:240-242`) does not list `source` in its column set,
  so SQLite resets it to the `'tmdb'` DEFAULT on replace → `MetadataPending` flips to false,
  no duplicate, no second code path. **Leave the TMDB path untouched; only the Jellyfin write
  sets `source='jellyfin'`.** (Note: `existingEpisodeKeys` reads all rows incl. materialized
  ones, so a later TMDB season yields `NewEpisodeCount=0` and fires no `Series_refreshed`
  event — but `applyToProjection` runs unconditionally and overwrites in place. Enrichment is
  silent at projection level, which is what we want — "enrich, don't re-announce".)
- **Badge path (4 layers):** (1) `series_episodes.source` column; (2) read-model query
  `SeriesProjection.fs:880` selects `source`, derives `MetadataPending = (source = "jellyfin")`
  in the `EpisodeDto` builder (`:885-901`); (3) `EpisodeDto` gains `MetadataPending: bool`
  (`Shared.fs:450-460`) — a semantic bool, not the raw provider string, so the client carries
  no provider knowledge; (4) `SeriesDetail/Views.fs renderEpisode` (~`:627-681`) renders the
  badge beside the title block (`:668-681`). Visual treatment is governed by the styleguide
  (ADR 0009) — hence the `design-system-001` dependency.
- **Enumeration is present-on-server, and the widening is free.** `Api.runJellyfinImport`
  Phase 2 already fetches *all* episodes per matched series into `seriesBatch` via
  `getEpisodesWithReauth` (`Api.fs:934`); only the watch-history loop filters to `Played`
  (`JellyfinImport.fs:56`). So no fetch widening — add a materialization pass over the same
  full batch, **before** `syncSeriesWatchHistory` (`Api.fs:957-962`) so the row exists when
  watch progress / next-up recompute.

## Acceptance criteria

- [ ] During a Jellyfin sync, for each matched series, every Jellyfin episode whose
      `(season, episode)` has no `series_episodes` row is materialized: a `series_episodes`
      row with `source='jellyfin'` is inserted, **and** if the season has no `series_seasons`
      row a synthetic one (`source='jellyfin'`, number-only name/overview) is inserted first.
      Fields map Name→name, Overview→overview, `RunTimeTicks`→runtime (ticks→min),
      `PremiereDate`→air_date, Primary image→still_ref (best-effort; NULL if unfetchable),
      tmdb_rating NULL. The episode then renders under that season in the app.
- [ ] Materialized rows carry `source='jellyfin'`; the series-detail read exposes
      `EpisodeDto.MetadataPending=true` for them and the SeriesDetail episode list shows a
      "metadata pending" badge (styleguide-governed) while pending; TMDB-sourced episodes show
      `MetadataPending=false`.
- [ ] Enrichment without duplication: when a later TMDB refresh returns the season,
      `applyToProjection`'s `INSERT OR REPLACE` on the `(slug, season, episode)` PK overwrites
      the materialized row, resetting `source` to `'tmdb'` (default) → `MetadataPending`
      becomes false, no duplicate row appears, and watch progress is preserved (it lives in
      the separate `series_episode_progress` table).
- [ ] *Interview with the Vampire* S3 end-to-end: with TMDB at two seasons and three S3
      episodes present on Jellyfin, a sync makes S3E1–E3 appear under a Season 3, each
      `MetadataPending`; any episode already played on Jellyfin shows as watched with the
      correct date.
- [ ] Materialization is fault-isolated like the rest of Phase 2: a bad episode (missing index
      numbers, image-fetch failure, write error) records an error and continues without
      aborting the run (consistent with [[integration-001]] / ADR 0010); an image-fetch
      failure degrades to `still_ref=NULL` rather than erroring.
- [ ] Tests exercise: (a) present-but-absent-from-projection → materialized, incl. the
      synthetic season row; (b) already-in-projection → not duplicated and `source` unchanged;
      (c) present-but-unwatched → still materialized (not gated on `Played`); (d) later TMDB
      refresh enriches in place and clears `MetadataPending`; (e) materialization runs before
      the watch-history write so a played materialized episode gets its progress attached.

## Notes

- **Write a short ADR during work** (`scope: integration`, in the style of `0011`): the
  source-of-truth precedence rule (TMDB authoritative, Jellyfin a self-healing supplement),
  the subtle enrichment mechanism (TMDB overwrites Jellyfin because `INSERT OR REPLACE` resets
  `source` to its DEFAULT), and the deliberate projection-only / no-new-event choice (a
  divergence from the event-sourced watch-history path). `work` will backlink it into
  `related_adrs`. (This resolves the capture's "Refinement candidate" note.)
- **Season-container gotcha (load-bearing).** The detail read iterates `series_seasons`
  (`SeriesProjection.fs:870-873`) and only then queries episodes per season (`:880`). An
  episode materialized into a season with **no `series_seasons` row is orphaned and never
  renders.** Materialization must upsert a synthetic season row for any new season (resolves
  research Open Question 5 — a number-only synthetic season is acceptable).
- **Decoder + `Fields=` widening.** `fetchEpisodeItems` requests only `Fields=ProviderIds`
  (`Jellyfin.fs:222`). Widen to `PremiereDate,Overview,RunTimeTicks` (the library fetch
  already uses this set — copy `Jellyfin.fs:210`). `Overview`/`RunTimeTicks` already decode on
  `JellyfinBaseItem`; add `PremiereDate` and `ImageTags.Primary` fields to the record +
  `decodeBaseItem` (`Jellyfin.fs:32-47`, `:77-92`) — they don't exist yet.
- **Still image is the only genuinely new I/O.** Fetch `/Items/{id}/Images/Primary`, store via
  the existing `ImageStore` as `still_ref` (mirror `SeriesRefresh`). Best-effort: store if
  present, tolerate failure → NULL (TMDB enriches later). Acceptable to ship v1 with stills
  deferred if it risks scope.
- **Fault-isolation home.** Add materialization as a pure, injected-effect function in
  `JellyfinImport` (same shape as `syncSeriesWatchHistory` — testable without HTTP/SQLite),
  wrapped per-series and per-episode.
- **Provenance caveat (research Open Question 4):** the recommendation rests on the empirical
  fact that the user's Jellyfin holds the missing season, not on TheTVDB provenance. If the
  Jellyfin library runs only the default TMDB scraper it could share TMDB's lag on a *future*
  title — non-blocking (the fallback still strictly improves on TMDB-only at zero new cost).
- **Sizing:** **M** (spike said S–M; the synthetic-season handling, decoder/image work, and
  the end-to-end badge nudge it to M).
- Composes with [[integration-006]]: 006 makes TMDB's eventual season auto-discovered once it
  exists; this task covers the window (possibly permanent) where TMDB never delivers it.
- **Frontend gate:** badge work `depends_on` the design-system styleguide (`design-system-001`,
  already done — gate satisfied).

## Outcome

The Jellyfin sync now materializes season/episode metadata for anything the TMDB-fed
projection lacks, keeping TMDB authoritative. Episodes present on the user's Jellyfin server
but missing from TMDB (e.g. *Interview with the Vampire* S3) now appear in the app, each
showing a subtle "metadata pending" badge until TMDB enriches them.

Implementation:
- **Provenance column, no new event.** Added `source TEXT NOT NULL DEFAULT 'tmdb'` to
  `series_episodes` and `series_seasons` (CREATE + try/ALTER migration). The Jellyfin pass
  writes `'jellyfin'`; TMDB's `INSERT OR REPLACE` omits `source` and resets it to the default,
  so enrichment clears the badge for free with no duplicate row and watch progress preserved.
  Decision recorded in ADR 0012.
- **Pure, fault-isolated core:** `JellyfinImport.materializeMissingEpisodes` (injected-effect,
  per-series + per-episode isolation, best-effort still fetch, not gated on `Played`). Wired
  into `Api.runJellyfinImport` Phase 2 **before** the watch-history sync. Synthetic number-only
  season rows are upserted first so episodes are not orphaned by the season-iterating read.
- **Read/badge path:** `EpisodeDto.MetadataPending` (semantic bool derived from `source`) in
  Shared + `SeriesProjection.getBySlug`; subtle styleguide-governed badge in
  `SeriesDetail/Views.fs`.
- **Jellyfin adapter:** decoder widened with `PremiereDate` + `PrimaryImageTag`,
  `fetchEpisodeItems` `Fields=` query widened to carry overview/runtime/premiere.

Stills deferred to backlog **integration-007** (seam present, v1 returns NULL).

Tests: `tests/Server.Tests/JellyfinMaterializeTests.fs` (7 cases covering criteria a–e plus
fault isolation and still-degradation). Full suite green at 278; `npm run build` green.

Key files: `src/Server/Jellyfin.fs`, `src/Server/JellyfinImport.fs`,
`src/Server/SeriesProjection.fs`, `src/Server/Api.fs`, `src/Shared/Shared.fs`,
`src/Client/Pages/SeriesDetail/Views.fs`,
`.agentheim/knowledge/decisions/0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md`.
