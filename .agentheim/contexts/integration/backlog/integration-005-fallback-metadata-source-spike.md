---
id: integration-005
title: Spike — fallback metadata source when TMDB lags on new seasons
status: backlog
type: spike
context: integration
created: 2026-06-25
completed:
depends_on: []
blocks: []
tags: [tmdb, metadata, series, sync, fallback, tvdb, trakt, jellyfin]
related_adrs: []
related_research: []
prior_art: []
---

## Why

TMDB is community-edited, so a new season only appears once a volunteer adds it.
Real case that triggered this: *Interview with the Vampire* season 3 is airing and
visible on IMDb (and almost certainly already present on the user's Jellyfin server),
but TMDB still lists only two seasons. Today the app is fully TMDB-bound for series
metadata — `SeriesRefresh.fetchFromTmdb` reads `details.Seasons` straight from TMDB
(`src/Server/SeriesRefresh.fs:74`) and upserts only what TMDB returns. If TMDB has no
season 3, neither the nightly job nor a manual "Refresh from TMDB" can surface it, and
any Jellyfin watch of an S3 episode has no projection row to attach to.

We don't yet know enough about the alternative sources to commit to a direction. This
spike answers "which source, and at what cost" before an implementation task is captured.

## What

Compare the realistic alternative / fallback metadata sources for TV series and produce
a recommendation that a follow-up implementation task can build against. Candidates:

- **TheTVDB** — historically strongest/earliest TV coverage; assess API access tier,
  key terms, rate limits, episode-level metadata quality, and how its season/episode
  shape maps onto the existing `SeriesRefresh` / `Series.SeasonImportData` types.
- **Trakt** — aggregator with its own API; assess currency, auth model, coverage.
- **OMDb** — wraps IMDb-ish data; assess whether its TV season/episode coverage and
  single-maintainer service are dependable enough to bother with (suspected: weak).
- **Jellyfin (already integrated)** — since the user is *watching* S3 there, Jellyfin
  likely already holds the season/episode metadata via TheTVDB. Evaluate "materialize
  the episode from Jellyfin when the sync reports one the projection lacks" as the
  cheapest path (no new external dependency). This is the leading hypothesis to beat.

The output is a research report + a recommended direction, not code.

## Acceptance criteria

- [ ] A research report in `.agentheim/knowledge/research/` comparing TheTVDB, Trakt,
      OMDb, and Jellyfin-as-source on: new-season latency vs TMDB, API access / key
      terms / rate limits, and episode-level metadata quality.
- [ ] An explicit recommendation of one primary direction (with rationale), including
      whether it supplements TMDB as a fallback or could replace it for series.
- [ ] A note on how the recommended source's season/episode shape maps onto the
      existing `RefreshFetchResult` / `Series.SeasonImportData` / `EpisodeImportData`
      types so the follow-up task starts from a known integration point.
- [ ] A sizing note for the follow-up implementation task (new adapter + key vs.
      reusing the Jellyfin adapter).

## Notes

- Sibling task: [[integration-006]] (nightly refresh skips `Ended` series) — independent
  of which source we pick, but part of the same "TMDB didn't deliver" concern.
- Jellyfin-as-source is the cheapest hypothesis precisely because the gap shows up
  exactly for titles the user is actively watching — i.e. the ones in their Jellyfin.
- The user explicitly chose "research first" over committing to TVDB / manual-add /
  Jellyfin up front (modeling session 2026-06-25).
- This can be run via the `research` skill; fold its report slug back into
  `related_research` here when it lands.
