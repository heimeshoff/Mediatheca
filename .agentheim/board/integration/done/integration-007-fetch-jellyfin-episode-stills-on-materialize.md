---
id: integration-007
title: Fetch Jellyfin episode stills when materializing a missing season
status: done
type: feature
context: integration
created: 2026-06-26
completed: 2026-08-01
depends_on: [integration-m4k7p]
blocks: []
tags: [jellyfin, series, materialize, images, still]
related_adrs: [0039, 0012, 0011, 0010]
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

- [x] During a Jellyfin sync, a materialized episode whose Jellyfin item has a primary image
      gets its still downloaded and `still_ref` set to
      `stills/{slug}-s%02de%02d-jellyfin.jpg`, with the file present at that path under
      `imageBasePath`.
- [x] A still-fetch failure of any kind (404/no image, non-2xx, thrown exception, write
      error) leaves `still_ref = NULL`, appends **nothing** to `materializeResult.Errors`,
      leaves `Failed = false`, and does not turn the sync into `SyncFailed`. Asserted against
      the pure seam with failing injected lambdas.
- [x] A later TMDB refresh still overwrites the still with TMDB's: because the Jellyfin file
      lives at a distinct `-jellyfin.jpg` path, `SeriesRefresh`'s `ImageStore.imageExists`
      short-circuit does not fire on TMDB's canonical path, TMDB downloads its own still, and
      `INSERT OR REPLACE` resets `still_ref` to `stills/{slug}-s%02de%02d.jpg`
      (m4k7p enrichment behaviour preserved).
- [x] The new adapter fetch reuses `Jellyfin.withReauthRetry`: a 401/403 on the image endpoint
      re-authenticates once, persists the fresh token, and retries exactly once; a second
      rejection degrades to `None` rather than looping (ADR 0011 policy unchanged).
- [x] `JellyfinImport.materializeMissingEpisodes` is unchanged — no edit to its signature or
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

## Outcome

Closed the ADR-0012 deferral: materialized episodes now get a real thumbnail instead of the
placeholder TV icon, fetched from Jellyfin best-effort at materialization time.

Implementation followed the pre-refined shape exactly:
- **`Jellyfin.fs`** — added `fetchImageBytesWithAuth` (binary sibling of `fetchJsonWithAuth`,
  same 401/403 -> `Unauthorized` mapping) and public `getPrimaryImageWithReauth`, built on the
  existing `withReauthRetry` (no new auth policy). URL:
  `/Items/{itemId}/Images/Primary?maxWidth=600&format=Jpg`.
- **`JellyfinImport.fs`** — added pure `fetchEpisodeStill (download) (save) slug season episode
  jellyfinId : string option`, composing download + save into a `stills/{slug}-sXXeYY-jellyfin.jpg`
  ref. Never throws; any failure (download `Error`, or an exception from either lambda) degrades
  to `None`. `materializeMissingEpisodes` itself is untouched.
- **`Api.fs:969-980`** — the stub `(fun _ _ _ _ -> None)` was replaced with
  `JellyfinImport.fetchEpisodeStill` wired to `Jellyfin.getPrimaryImageWithReauth` (run
  synchronously, same idiom as `Tmdb.downloadEpisodeStill`/`SeriesRefresh.fs`) and
  `ImageStore.saveImage`.
- The `-jellyfin.jpg` suffix (distinct from TMDB's canonical `stills/{slug}-sXXeYY.jpg`) is the
  load-bearing choice: it keeps `SeriesRefresh.fs:99-110`'s `ImageStore.imageExists`
  short-circuit from ever firing on a Jellyfin-sourced file, so a later TMDB refresh always
  downloads and repoints `still_ref` at its own canonical path — verified directly by a new
  test exercising the `INSERT OR REPLACE` enrichment path with a non-null Jellyfin `StillRef`
  present beforehand.

Tests: new `tests/Server.Tests/JellyfinStillTests.fs` (8 cases) — success/failure/zero-padding
for `fetchEpisodeStill` in isolation, two cases wiring it as `materializeMissingEpisodes`'
`fetchStill` parameter (success resolves `StillRef`; failure leaves it `None` with zero errors
and `Failed = false`), and the TMDB-overwrite-Jellyfin-still enrichment case. Full Expecto suite
green at 435 (up from 427); `npm run build` and `dotnet build src/Server/Server.fsproj` both
green. The existing 7 `JellyfinMaterializeTests.fs` cases are untouched and still pass.

**Iteration 2 correction:** the paragraph above was wrong — refining a decision in the task file
during planning does not substitute for recording it in the ADR corpus, which is what future
maintainers and the BC README actually point at. Wrote **ADR 0039**
(`.agentheim/knowledge/decisions/0039-jellyfin-still-distinct-storage-path-accepted-orphan.md`),
recording the distinct `-jellyfin.jpg` storage path (with the rejected alternatives: same path
as TMDB's canonical file, cleanup-on-enrich, pre-checking `PrimaryImageTag`) and the accepted,
reclaimable orphan tradeoff, including its interaction with Administration's ADR-0025 orphan
scanner (the abandoned `-jellyfin.jpg` file falls inside ADR-0025's existing `series_episodes
.still_ref` coverage, so it's found and purgeable through the existing `/admin/images` flow with
no new administration code). Also amended ADR 0012's Consequences in place — it previously
claimed materialized stills are `NULL` until TMDB enriches, which this task's diff made false;
it now records the deferral as closed and points at ADR 0039. Added the orphan-acceptance
sentence to this BC's README **Materialize** entry, which previously documented the distinct
path choice but not what happens to the file after enrichment.

The one `[human-eye]` criterion is left unchecked for the builder per ADR-0061 precedent.

Key files: `src/Server/Jellyfin.fs`, `src/Server/JellyfinImport.fs`, `src/Server/Api.fs`,
`tests/Server.Tests/JellyfinStillTests.fs`, `tests/Server.Tests/Server.Tests.fsproj`,
`.agentheim/contexts/integration/README.md`.

## Verifier note (iteration 1)

**VERDICT: FAIL** — checks 1-5 all passed; check 6 (ADRs for decisions) failed. No production
code change is needed — only the missing decision record.

**REASONS:**

- Check 6 (ADRs for decisions): the diff embeds two decisions a future maintainer would ask
  "why?" about, and `ADRS_WRITTEN: none`. (a) The storage-path choice
  `stills/{slug}-s%02de%02d-jellyfin.jpg` deliberately diverging from TMDB's canonical
  `stills/{slug}-s%02de%02d.jpg` (`src/Server/JellyfinImport.fs`, `fetchEpisodeStill`) — the task
  itself calls this "the load-bearing finding", and its whole purpose is to defeat
  `SeriesRefresh.fs:99-110`'s `ImageStore.imageExists` short-circuit. A future maintainer
  "tidying" the suffix would silently and permanently break acceptance criterion 3 with no
  test-visible failure at the point of edit. (b) The "accepted orphan" tradeoff —
  `-jellyfin.jpg` files left unreferenced on disk forever after TMDB enrichment — a cross-BC
  consequence that lands in Administration's orphan scanner (ADR-0025), recorded nowhere
  durable at all, not even in the BC README.
- The task's `## Outcome` explicitly declines the ADR on the grounds that the implementation
  shape "was already fully resolved during this task's 2026-08-01 refinement and recorded in
  this task file's 'Resolved implementation shape' section". That is task-file narration
  standing in for an ADR — the exact substitution check 6 forbids. The task file moves to
  `done/` and is rarely read again; the ADR corpus is what the README and future maintainers
  point at.
- The ADR corpus is now actively stale on this subject with nothing correcting it:
  `.agentheim/knowledge/decisions/0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md`
  still states "Still images are deferred (integration-007): the materialization seam fetches
  stills best-effort but the v1 wiring returns `None`, so materialized stills are `NULL` until
  TMDB enriches." That deferral is exactly what this diff closes, and no ADR records the
  closure. A maintainer reading ADR 0012 today is told the opposite of what the code now does.

**SUGGESTED_FIX:** Everything else in this task verifies cleanly — write only the missing
record. Either add a new ADR (scope `integration`, `related_tasks: [integration-007]`) recording
the distinct `-jellyfin.jpg` storage path (Decision: why not TMDB's canonical path; alternatives:
same path / cleanup-on-enrich / pre-check `PrimaryImageTag`) and the accepted-orphan tradeoff
with its ADR-0025 interaction, or amend ADR 0012 in place so its Consequences no longer claims
materialized stills are NULL. Add the orphan-acceptance sentence to the BC README's
**Materialize** entry too, since the README currently documents the path choice but not the
orphans. No production-code change is needed.

**ITERATION_HINT:** likely-fixable

**Measurements recorded by the verifier (not in dispute):** `npm test` from the worktree reports
**435 passed, 0 failed, 0 errored** (427 before + 8 added, matching `TESTS_ADDED: 8`);
`npm run build` green (35.9s, no Fable errors). Criterion 5's "`materializeMissingEpisodes`
unchanged" confirmed structurally — `git show HEAD --numstat` gives `33 0` for
`src/Server/JellyfinImport.fs` and `47 0` for `src/Server/Jellyfin.fs`, i.e. purely additive.
Criterion 6 is `[human-eye]` and was correctly left unchecked — **builder eye-check pending**,
not verified and not a FAIL reason.

**Noted weak spot (did not fail a check):** criterion 4 ("the new adapter fetch reuses
`Jellyfin.withReauthRetry`") has no new executable test. Accepted on the inspectable
single-expression delegation in `Jellyfin.fs` (`getPrimaryImageWithReauth` = `withReauthRetry
config.AccessToken (fun token -> fetchPrimaryImage ...) (reauthThunk httpClient config)
persistAuth`, identical in shape to `getEpisodesWithReauth`), plus the pre-existing six
`withReauthRetry` cases in `tests/Server.Tests/JellyfinReauthTests.fs` that govern the shared
policy, plus the new "download failure degrades to None" case covering the degrade half.
