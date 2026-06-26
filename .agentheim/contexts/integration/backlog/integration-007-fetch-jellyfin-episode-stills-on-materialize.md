---
id: integration-007
title: Fetch Jellyfin episode stills when materializing a missing season
status: backlog
type: feature
context: integration
created: 2026-06-26
depends_on: [integration-m4k7p]
blocks: []
tags: [jellyfin, series, materialize, images, still]
related_adrs: [0012]
related_research: [tv-series-metadata-fallback-sources-2026-06-26]
prior_art: [integration-m4k7p]
---

## Why

[[integration-m4k7p]] materializes season/episode metadata from Jellyfin when TMDB lacks it,
but ships v1 with episode **stills deferred**: the materialization seam fetches stills
best-effort, yet the wiring in `Api.runJellyfinImport` passes
`(fun _slug _season _ep _jellyfinId -> None)`, so materialized rows have `still_ref = NULL`
until TMDB later enriches them. ADR 0012 records this deferral. A materialized episode
therefore renders with the placeholder TV icon instead of a thumbnail.

## What

Implement the best-effort still fetch: `GET {serverUrl}/Items/{jellyfinId}/Images/Primary`
(the `PrimaryImageTag` is already decoded on `JellyfinBaseItem` after m4k7p), store the bytes
via `ImageStore.saveImage` as `stills/{slug}-s%02de%02d.jpg` (mirror
`SeriesRefresh`/`Tmdb.downloadEpisodeStill` conventions), and return that relative path from
the injected `fetchStill` lambda. Keep it strictly best-effort: any HTTP/decode/write failure
degrades to `None` (NULL still) and must not fail the sync — the pure
`JellyfinImport.materializeMissingEpisodes` already wraps the call in try/with and treats
`None` as fine.

## Acceptance criteria

- [ ] During a Jellyfin sync, a materialized episode whose Jellyfin item has a primary image
      gets its still downloaded and `still_ref` set; the SeriesDetail episode renders the
      thumbnail.
- [ ] A still-fetch failure (no image, HTTP error, write error) leaves `still_ref = NULL` and
      does not turn the sync into `SyncFailed`.
- [ ] A later TMDB refresh still overwrites the still with TMDB's (existing m4k7p enrichment
      behaviour preserved).

## Notes

- The image endpoint needs the access token / re-auth path like other Jellyfin fetches — reuse
  the `Jellyfin.fs` auth helpers; consider a `withReauthRetry`-style wrapper if a 401 is
  plausible mid-run.
- Sizing: S.
