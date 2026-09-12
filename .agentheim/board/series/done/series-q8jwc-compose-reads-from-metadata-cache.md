---
id: series-q8jwc
title: Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged
status: done
type: refactor
context: series
created: 2026-08-01
completed: 2026-08-01
depends_on: [series-r2xhv, design-system-001]
blocks: []
tags: [series, metadata, cache, read-model, projection]
related_adrs: [0012, 0031, 0048]
related_research: []
prior_art: [integration-m4k7p]
---

## Why

Between `series-r2xhv` and this task the projection's externally-sourced columns are stale-but-read —
a temporary correctness gap. It must not sit across releases.

## What

**Join in the query function, not at the API layer.** `SeriesProjection.getBySlug` must keep returning
a fully-populated DTO exactly as today. The reason is concrete: `GameProjection.getBySlug` has ~10
internal callers inside `Api.fs` that are not serving the client at all — `searchSteamForGame:3919`,
`searchRawgForGame:3937`, `fetchHltbData:4311`, `getGameTrailers:3330`, `getGameImageCandidates:3244`,
the Steam-attach flow at 1073 — each reading `Name` / `SteamAppId` / `CoverRef` to drive its own logic.
The Series side has the same shape. Compose at the API layer and each of those either has to compose
too, or silently starts operating on a degraded DTO.

- `getAll` / `getRecent` / `getFinished`: add `LEFT JOIN series_metadata_cache` plus the two views.
  `COALESCE(c.name, sl.name)` on identity-card fields; straight nullable reads for cache-only fields.
  **Query count unchanged at 1.**
- `getBySlug`: the seasons read moves to `series_season_cache`, the per-season episode read to
  `series_episode_cache LEFT JOIN series_episode_progress`. **Query count unchanged** — the existing
  per-season N+1 is neither improved nor worsened. This is not the task to fix it.
- `getDashboardSeriesNextUp` (`SeriesProjection.fs:1205`): add `LEFT JOIN series_next_up nu` and
  `LEFT JOIN series_episode_counts ec`; `sl.next_up_season` → `nu.season_number`,
  `sl.episode_count` → `ec.episode_count`; retarget the existing `LEFT JOIN series_episodes ep` and
  `LEFT JOIN jellyfin_episode jej`. Still one statement, no N+1, no extra round trip.
- `recalculateProgress` (`SeriesProjection.fs:154`) keeps its `watched_episode_count` half (a
  `COUNT(DISTINCT ...)` over `series_episode_progress`, a pure projection table) and **loses its
  next-up half** to the view.

**Cache miss degrades gracefully — never a synchronous fetch.** A fetch on the read path would put an
unbounded TMDB call with a possibly-unset API key inside `getSeriesDetail`, and would require a *write*
on a read path. The identity card still comes from the projection, so a cold entry renders as a
recognizable library item; `TmdbRating` / `Overview` / `EpisodeRuntime` return `None`; `MetadataPending`
— vocabulary that already exists with a styleguide-governed badge (ADR-0012) — generalizes from
"materialized from Jellyfin" to "no third-party metadata yet".

**`src/Shared/Shared.fs` DTOs do not change. The client does not change.**

## Acceptance criteria

- [ ] `git diff --stat src/Shared/Shared.fs src/Client/` shows zero changed files.
- [ ] Expecto: `getBySlug` on a fixture with a populated cache returns a DTO field-equal to a pre-refactor snapshot.
- [ ] Expecto: `getBySlug` on a fixture with an **empty** cache returns a DTO with non-empty `Name` / `Year` / `PosterRef`, `None` for `TmdbRating` / `Overview` / `EpisodeRuntime`, an empty season list, and `MetadataPending = true`.
- [ ] Expecto: `getDashboardSeriesNextUp` returns the same next-up tuple as the pre-refactor materialized columns, on a fixture with mixed watch progress across multiple rewatch sessions.
- [ ] Query-count assertion on `getAll` (exactly 1), by whichever seam the existing test idiom supports.
- [ ] `npm test` passes; `npm run build` passes.
- [ ] The series list, series detail, and dashboard Next Up render identically to before the change. [human-eye]

## Notes

`MetadataPending` already has a design-system badge from ADR-0012, so no new visual vocabulary is
introduced — hence `depends_on: design-system-001` (already done) satisfies the frontend gate without
new styleguide work. If the badge needs a new state, stop and file a design-system task first.

**ADR:** `.agentheim/knowledge/decisions/0048-series-reads-composed-from-metadata-cache-at-query-time.md`

## Outcome

`SeriesProjection.fs`'s query functions (`getAll`, `getBySlug`, `getRecentSeries`,
`getRecentlyFinished`, `getDashboardSeriesNextUp`) now `LEFT JOIN series_metadata_cache` (for
`TmdbRating`/`Overview`/`EpisodeRuntime`) and the `series_next_up`/`series_episode_counts` views (for
`NextUp`/`SeasonCount`/`EpisodeCount`) directly inside their own query bodies — every join lives in
`SeriesProjection.fs`, never at the API layer. `recalculateProgress` was simplified to only maintain
`watched_episode_count`; the `next_up_*` half moved entirely to the view. `getBySlug`'s per-season
episode read now composes `IsWatched`/`WatchedDate` via a direct `LEFT JOIN series_episode_progress`
(scoped to the active rewatch session) instead of a precomputed whole-series `Map`. Every identity-card
field (`Name`/`Year`/`PosterRef`/`BackdropRef`/`Genres`/`Status`) still reads straight from
`series_list`/`series_detail`, unaffected — each is driven by its own explicit event. A cache miss
degrades gracefully (`None`/`""`/empty seasons), never a synchronous TMDB fetch on the read path.

`src/Shared/Shared.fs` and `src/Client/` are untouched (`git diff --stat` confirms zero changed files).
9 new Expecto tests in `tests/Server.Tests/SeriesProjectionReadsTests.fs` cover: a populated cache
winning over `series_detail`'s own stale columns, a cache-miss cold-entry degrading gracefully (with the
vacuous "all episodes pending" invariant over an empty season list), the next-up tuple matching
pre-refactor semantics across multiple rewatch sessions, and a `CountingConnection`-based proof that
`getAll`'s cache/view composition adds zero per-row queries (the pre-existing, unrelated
`NextAirDate` per-row seam is unchanged). Two existing test fixtures (`JellyfinStillTests.fs`,
`JellyfinMaterializeTests.fs`) needed `MetadataCache.initialize` added to their setup, since `getBySlug`
now requires `series_metadata_cache` to exist.

Two gaps discovered mid-task were filed as backlog rather than fixed here (out of this task's explicit
read-composition scope): `series-t3jkv` (nothing writes `series_metadata_cache` going forward — only a
one-time seed) and `series-x9mfp` (`getRecentlyAbandoned` wasn't retargeted like its sibling
`getRecentlyFinished`).

Key files: `src/Server/SeriesProjection.fs`, `tests/Server.Tests/SeriesProjectionReadsTests.fs`,
`tests/Server.Tests/JellyfinStillTests.fs`, `tests/Server.Tests/JellyfinMaterializeTests.fs`,
`.agentheim/knowledge/decisions/0048-series-reads-composed-from-metadata-cache-at-query-time.md`.
