---
id: integration-005
title: Spike — fallback metadata source when TMDB lags on new seasons
status: done
type: spike
context: integration
created: 2026-06-25
completed: 2026-06-26
depends_on: []
blocks: []
tags: [tmdb, metadata, series, sync, fallback, tvdb, trakt, jellyfin]
related_adrs: []
related_research: [tv-series-metadata-fallback-sources-2026-06-26]
prior_art: []
---

## Why

TMDB is community-edited, so a new season only appears once a volunteer adds it.
Real case that triggered this: *Interview with the Vampire* season 3 is airing and
visible on IMDb (and already present on the user's Jellyfin server), but TMDB still
lists only two seasons. Today the app is fully TMDB-bound for series metadata —
`SeriesRefresh.fetchFromTmdb` reads `details.Seasons` straight from TMDB
(`src/Server/SeriesRefresh.fs:74`) and upserts only what TMDB returns. If TMDB has no
season 3, neither the nightly job nor a manual "Refresh from TMDB" can surface it, and
any Jellyfin watch of an S3 episode has no projection row to attach to.

This spike answered "which source, and at what cost" before an implementation task is
captured. **Research is complete and the direction is decided — see Resolution.**

## What

Compared the realistic fallback metadata sources for TV series and produced a
recommendation a follow-up implementation task can build against. Candidates were
evaluated on an **even footing**:

- **TheTVDB** — TV-first, historically strongest/earliest new-season coverage.
- **Trakt** — aggregator with its own API.
- **Jellyfin (already integrated)** — materialize the episode from Jellyfin when the
  sync reports one the projection lacks (no new external dependency).
- **OMDb — ruled out up front** (IMDb-wrapper, single-maintainer service, weak TV
  season/episode coverage); not evaluated in depth.

The output is a research report + a recommended direction, not code.

## Acceptance criteria

- [x] A research report in `.agentheim/knowledge/research/` comparing TheTVDB, Trakt,
      and Jellyfin-as-source (OMDb dismissed) on: new-season latency vs TMDB, API access /
      key terms / rate limits, and episode-level metadata quality.
      → `knowledge/research/tv-series-metadata-fallback-sources-2026-06-26.md`
- [x] An explicit recommendation of one primary direction (with rationale), including
      whether it supplements TMDB as a fallback or could replace it for series.
      → **Jellyfin-as-source, supplementing (not replacing) TMDB.**
- [x] A note on how the recommended source's season/episode shape maps onto the existing
      `RefreshFetchResult` / `Series.SeasonImportData` / `EpisodeImportData` types.
      → Direct field map, no remap layer (report § Candidate 3 / mapping).
- [x] A sizing note for the follow-up implementation task.
      → reuse Jellyfin adapter ≈ S; new TheTVDB adapter + key ≈ M–L (report § (c)).

## Resolution

**Recommendation: adopt Jellyfin-as-source as the fallback, SUPPLEMENTING TMDB (not
replacing it).** Materialize a season/episode from Jellyfin only when the sync reports an
item the TMDB-fed projection lacks; keep TMDB authoritative (richer imagery, ratings,
overviews, multi-language) and let the nightly refresh backfill the Jellyfin-materialized
rows once volunteers add the season upstream.

Why it wins:
- **Targets the real failure mode** — the missing season is always one the user is
  watching, hence always already in their Jellyfin library.
- **Zero new external dependency** — adapter, auth, and re-auth policy already exist
  (`src/Server/Jellyfin.fs`); no key, subscription, attribution, or rate-limit surface.
- **No numbering remap** — Jellyfin uses aired S/E numbering matching the existing
  TMDB-seeded `series_episodes` rows. TheTVDB would require pinning Aired Order to avoid
  silent divergence (Aired vs DVD vs Absolute).
- **Mapping is essentially free** — `/Shows/{id}/Episodes` already returns
  `IndexNumber`→EpisodeNumber, `ParentIndexNumber`→SeasonNumber, `PremiereDate`→AirDate,
  `Name`/`Overview`/`RunTimeTicks`, `ImageTags.Primary`→still.

Runners-up: **Trakt is disqualified** — by its own FAQ it sources most TV info from TMDB
(~24h refresh), so it inherits TMDB's lag and can never be fresher for this problem.
**TheTVDB is the better raw source** but costs a net-new adapter + subscription/PIN auth +
attribution + season-order handling, to obtain data the user already holds locally —
revisit only for series *not* in Jellyfin (e.g. wishlist/discovery).

## Notes

- **Caveat for the follow-up task (report Open Question 4):** the recommendation rests on
  the *empirical* fact that the user's Jellyfin holds the missing season, **not** on
  TheTVDB provenance — TheTVDB is an *optional* Jellyfin plugin, while TMDB is Jellyfin's
  built-in default TV provider. If the user's Jellyfin runs only the default TMDB scraper,
  it could share TMDB's lag on a *future* title. A one-line check of the user's Jellyfin
  library provider config is worth doing before relying on Jellyfin to *always* lead TMDB.
  (Does not change the recommendation — the fallback still strictly improves on TMDB-only
  and costs nothing new.)
- **Adapter widening needed (not a blocker):** the episode decoder currently reads only
  `Tmdb`/`Imdb` provider IDs and requests `Fields=ProviderIds` (`Jellyfin.fs:220-230`);
  the follow-up adds `PremiereDate` + `ImageTags` to the decoder and widens the episode
  `Fields=` query.
- **Next step:** capture the follow-up implementation task — "materialize missing
  season/episode from Jellyfin when the sync reports a row the TMDB projection lacks
  (mark for later TMDB enrichment)."
- Sibling task: [[integration-006]] (nightly refresh skips `Ended` series) — independent of
  which source we pick, but part of the same "TMDB didn't deliver" concern.
- The user explicitly chose "research first" over committing to TVDB / manual-add /
  Jellyfin up front (modeling session 2026-06-25); this spike honoured that.
