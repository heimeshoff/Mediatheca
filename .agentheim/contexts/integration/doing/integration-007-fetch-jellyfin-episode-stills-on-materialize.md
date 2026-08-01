---
id: integration-007
title: Fetch Jellyfin episode stills when materializing a missing season
status: doing
type: feature
context: integration
created: 2026-06-26
depends_on: [integration-m4k7p]
blocks: []
tags: [jellyfin, series, materialize, images, still]
related_adrs: [0012, 0011, 0010]
related_research: [tv-series-metadata-fallback-sources-2026-06-26]
prior_art: [integration-m4k7p]
---

## Why

[[integration-m4k7p]] materializes season/episode metadata from Jellyfin when TMDB lacks it,
but ships v1 with episode **stills deferred**: the materialization seam fetches stills
best-effort, yet the wiring in `Api.runJellyfinImport` passes
`(fun _slug _season _ep _jellyfinId -> None)` (`src/Server/Api.fs:974`), so materialized rows
have `still_ref = NULL` until TMDB later enriches them. ADR 0012 records this deferral. A
materialized episode therefore renders with the placeholder TV icon instead of a thumbnail.

## What

Implement the best-effort still fetch behind the existing seam. The pure core
(`JellyfinImport.materializeMissingEpisodes`) is **unchanged** — this task fills in the
injected `fetchStill` lambda and adds the one adapter function it needs.

### Resolved implementation shape (refined 2026-08-01 against the real code)

- **New Jellyfin adapter fetch — binary, not JSON.** `Jellyfin.fs` has only
  `fetchJsonWithAuth` (`:134`), which reads the body as a string. Add a sibling
  `fetchImageBytesWithAuth : HttpClient -> url -> token -> Async<Result<byte[], FetchError>>`
  that mirrors it exactly — same `authHeader token`, same `401/403 → Error Unauthorized`,
  same `non-success → Error (OtherFailure "HTTP %d")` — but reads
  `ReadAsByteArrayAsync()`. Then a public
  `getPrimaryImageWithReauth httpClient config persistAuth itemId : Async<Result<byte[], string>>`
  built on `withReauthRetry` (`:167`), following `getEpisodesWithReauth` (`:287`) line for line.
  Reusing `withReauthRetry` is what satisfies the Notes' 401 concern (ADR 0011) — no new
  auth policy, no retry loop.
- **URL:** `{serverUrl.TrimEnd('/')}/Items/{itemId}/Images/Primary?maxWidth=600&format=Jpg`.
  `maxWidth` keeps bytes near TMDB's `w300`-class stills (600 for retina); `format=Jpg`
  guarantees the bytes match the `.jpg` extension we store under. An episode with no primary
  image returns 404 → `OtherFailure "HTTP 404"` → `None`. No need to pre-check
  `PrimaryImageTag`: materialization only runs for episodes *missing from the projection*
  (a handful per sync), so an unconditional attempt costs nothing and is robust against
  `ImageTags` not being populated on the `/Shows/{id}/Episodes` response.
- **Storage path must NOT collide with TMDB's — this is the load-bearing finding.**
  Store as **`stills/{slug}-s%02de%02d-jellyfin.jpg`**, *not* TMDB's
  `stills/{slug}-s%02de%02d.jpg`. Reason: `SeriesRefresh.fs:99-110` short-circuits its own
  download on `ImageStore.imageExists imageBasePath ref` — so if the Jellyfin pass wrote
  TMDB's canonical path, a later TMDB refresh would see the file present, **skip its own
  download, and keep the Jellyfin bytes permanently**, silently violating acceptance
  criterion 3. A distinct suffix keeps TMDB's existence check missing, so TMDB downloads its
  own still and `INSERT OR REPLACE` repoints `still_ref` at the canonical path. Zero changes
  to `SeriesRefresh.fs` / `Tmdb.fs`.
- **Wiring** (`src/Server/Api.fs:974`) replaces the stub lambda with one that composes
  fetch → save → ref. `imageBasePath` is already in scope in `runJellyfinImport`. The seam is
  **synchronous** (`string -> int -> int -> string -> string option`), so the async fetch runs
  via `|> Async.RunSynchronously` — the same idiom `Tmdb.downloadEpisodeStill` (`Tmdb.fs:552`)
  and `SeriesRefresh.fs:108` already use.
- **Strictly best-effort, and structurally so.** Any HTTP/decode/write failure degrades to
  `None`. The lambda must not throw and must not append to `errors` — `materializeResult.Failed`
  and the sync's `SyncFailed` are driven solely by `materializeResult.Errors` (`Api.fs:981`),
  and the pure core already treats `None` as a non-error (`JellyfinImport.fs:171-173`).
- **Testable unit:** extract the compose step as a pure, injected-effect function in
  `JellyfinImport` — e.g.
  `fetchEpisodeStill (download: string -> Result<byte[], string>) (save: string -> byte[] -> unit) slug season episode jellyfinId : string option`
  — matching the BC's "pure orchestration over injected effects" idiom
  (`withReauthRetry`, `syncSeriesWatchHistory`). `Api.fs` injects the real HTTP + `ImageStore`;
  tests inject lambdas. This is what makes criteria 2 and 4 assertable without HTTP or SQLite.

## Acceptance criteria

- [ ] During a Jellyfin sync, a materialized episode whose Jellyfin item has a primary image
      gets its still downloaded and `still_ref` set to
      `stills/{slug}-s%02de%02d-jellyfin.jpg`, with the file present at that path under
      `imageBasePath`.
- [ ] A still-fetch failure of any kind (404/no image, non-2xx, thrown exception, write
      error) leaves `still_ref = NULL`, appends **nothing** to `materializeResult.Errors`,
      leaves `Failed = false`, and does not turn the sync into `SyncFailed`. Asserted against
      the pure seam with failing injected lambdas.
- [ ] A later TMDB refresh still overwrites the still with TMDB's: because the Jellyfin file
      lives at a distinct `-jellyfin.jpg` path, `SeriesRefresh`'s `ImageStore.imageExists`
      short-circuit does not fire on TMDB's canonical path, TMDB downloads its own still, and
      `INSERT OR REPLACE` resets `still_ref` to `stills/{slug}-s%02de%02d.jpg`
      (m4k7p enrichment behaviour preserved).
- [ ] The new adapter fetch reuses `Jellyfin.withReauthRetry`: a 401/403 on the image endpoint
      re-authenticates once, persists the fresh token, and retries exactly once; a second
      rejection degrades to `None` rather than looping (ADR 0011 policy unchanged).
- [ ] `JellyfinImport.materializeMissingEpisodes` is unchanged — no edit to its signature or
      body, and the existing 7 cases in `tests/Server.Tests/JellyfinMaterializeTests.fs` stay
      green. Full Expecto suite green; `npm run build` green.
- [ ] In the running app, a materialized episode (e.g. *Interview with the Vampire* S3) shows
      a real thumbnail instead of the placeholder TV icon. [human-eye]

## Notes

- **Accepted orphan.** When TMDB later enriches an episode, the `-jellyfin.jpg` file lingers
  on disk unreferenced (a few KB per materialized episode, only for episodes TMDB eventually
  publishes). Deliberately not cleaned up — doing so would couple `SeriesRefresh` to Jellyfin
  provenance for no user-visible gain. Revisit only if it ever grows.
- **Blocking I/O per materialized episode.** `Async.RunSynchronously` inside the sync loop
  blocks one image download at a time. Acceptable: the set is only the episodes *missing*
  from the projection, and it matches what `SeriesRefresh` already does per episode.
- Optionally short-circuit on `ImageStore.imageExists` for the `-jellyfin.jpg` path before
  downloading — cheap insurance against a re-fetch when a prior sync wrote the file but failed
  the row write. Not required for correctness.
- **No frontend work and no styleguide gate.** `still_ref` rendering already exists in
  `SeriesDetail/Views.fs` for TMDB stills; this task only makes the column non-NULL.
- Dependency [[integration-m4k7p]] is `done/` — gate satisfied.
- Sizing: **S**.
