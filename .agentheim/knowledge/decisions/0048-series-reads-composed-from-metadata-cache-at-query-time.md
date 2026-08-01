---
id: 0048
title: Series read composition joins the metadata cache and views at query time, never at the API layer
scope: series
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
related_tasks: [series-q8jwc]
related_research: []
---

# ADR 0048: Series read composition joins the metadata cache and views at query time, never at the API layer

## Context

`series-r2xhv`/ADR-0047 cut `SeriesRefresh`'s write path over to cache-only writes and narrowed
`Series_refreshed`, but left `SeriesProjection`'s query functions reading `series_list`/`series_detail`'s
own `tmdb_rating`/`overview`/`episode_runtime`/`next_up_*`/`episode_count`/`season_count` columns
directly — a temporary correctness gap where those columns are stale (no longer written by a refresh)
but still read as if current. `series-m7fdk`/ADR-0046 built the `series_next_up`/`series_episode_counts`
views and the `series_metadata_cache` table this gap needs. This task (`series-q8jwc`) closes the gap by
retargeting the query functions.

`GameProjection.getBySlug` has roughly ten internal `Api.fs` callers reading fields off its DTO to drive
their own logic (`searchSteamForGame`, `searchRawgForGame`, `fetchHltbData`, `getGameTrailers`,
`getGameImageCandidates`, the Steam-attach flow, etc.) — the Series side has the same shape. The task
text was explicit that composing at the API layer instead of the query function would force each of
those callers to compose independently or silently start operating on a degraded DTO, so every join
lives inside `SeriesProjection.fs`'s own query functions; every Shared DTO and the whole client stay
byte-identical (`git diff --stat src/Shared/Shared.fs src/Client/` shows zero changed files).

## Decision

### Which fields move to the cache/views, and which stay projection-sourced

Fields are split by *why* they were ever correct, not by which table currently holds them:

- **Identity-card fields stay on `series_list`/`series_detail`, read directly, never joined**: `Name`,
  `Year`, `PosterRef`, `BackdropRef`, `Genres`, `Status`, `PersonalRating`, `IsAbandoned`, `InFocus`,
  `TmdbId`. Each is driven by its own explicit event (`Series_added_to_library`,
  `Series_poster_replaced`, `Series_backdrop_replaced`, `Series_categorized`,
  `Series_personal_rating_set`, the narrowed `Series_refreshed`) — none of them went stale when
  `series-r2xhv` cut the refresh's writes, because none of them were ever written by the refresh's
  now-deleted `UPDATE series_list`/`UPDATE series_detail` in the first place (or, for `Status`, the
  narrowed event still carries every transition into the log).
- **`TmdbRating`/`Overview`/`EpisodeRuntime` move to `series_metadata_cache`, straight nullable reads, no
  `COALESCE` fallback to the old column**: these are exactly the fields `series-r2xhv` stopped keeping
  fresh on `series_list`/`series_detail`. A `COALESCE(cache, stale_projection_column)` was considered
  and rejected — it would silently paper over a cache miss with a frozen, increasingly wrong value
  instead of honestly degrading to `None`/`""`, which is the behavior the task's acceptance criteria
  require ("an empty cache" test asserts `None`, not a fallback value).
- **`SeasonCount`/`EpisodeCount`/`NextUp` move to the `series_episode_counts`/`series_next_up` views**:
  same reasoning — `recalculateProgress` could only maintain `next_up_*` while it owned
  `series_episode_cache` directly, and it no longer does (that table is `Cache`-classified now,
  ADR-0046). The views are structurally incapable of drifting from their source tables (no second write
  path), so this is a strict improvement over the materialized columns, not just a workaround.
  `recalculateProgress` (`SeriesProjection.fs`) now only maintains `watched_episode_count` — a pure
  `COUNT(DISTINCT ...)` over `series_episode_progress`, itself a `Projected` table, fully re-derivable
  from the log. `series_list.next_up_*` becomes permanently unwritten dead weight from this task forward
  (it is never read either), ahead of `series-d5tpn`'s planned column drop.
- **`getBySlug`'s per-season episode read composes `IsWatched`/`WatchedDate` via a direct
  `LEFT JOIN series_episode_progress ... AND rewatch_id = @rewatch_id`** scoped to the active rewatch
  session, replacing a precomputed whole-series `Map` built from a separate up-front query. Functionally
  identical (same scoping, same null-handling), just expressed as a join instead of an app-level lookup —
  this was the one place the task text asked for the shape to change, not just the field source
  ("the per-season episode read to `series_episode_cache LEFT JOIN series_episode_progress`"). The
  `overallWatched` set (spans every rewatch session, used for `OverallWatchedCount`) is unrelated to the
  cache cutover and was left as its own separate whole-series query.

### `getRecentSeries` does not join `series_metadata_cache`

`RecentSeriesItem` has no `TmdbRating` field — only `EpisodeCount`/`NextUp`/`WatchedEpisodeCount`. The
task text's blanket "`getAll`/`getRecent`/`getFinished`: add `LEFT JOIN series_metadata_cache` plus the
two views" was read as describing the *shape* of the change (join the cache and the views where the DTO
actually needs the fields), not a literal instruction to add a join nothing would select from. Adding an
unused join would cost a real join operation for zero benefit.

### `getDashboardSeriesNextUp` retargets its existing joins rather than adding new ones

`ep` (episode still/overview) and `jej` (Jellyfin episode id) already joined against
`sl.next_up_season`/`next_up_episode`. Their `ON` clause retargets to `nu.season_number`/
`nu.episode_number` (the `series_next_up` view) instead — same shape the task text called out
explicitly ("retarget the existing `LEFT JOIN series_episodes ep` and `LEFT JOIN jellyfin_episode
jej`"), even though the view itself already carries `still_ref`/`overview`/`tmdb_rating` for the
next-up row and could in principle have replaced the `ep` join outright. Kept as a retarget, not a
removal, to match the task's explicit instruction rather than second-guess it with an unasked-for extra
simplification.

### Query-count proof, not just a code-review claim

"Query count unchanged at 1" is proven with a `CountingConnection` (a `SqliteConnection` subclass
overriding the protected `CreateDbCommand()` to count invocations — `Db.newCommand` calls
`conn.CreateCommand()` exactly once per logical query, so this is a direct, mechanical proxy for round
trips) in `SeriesProjectionReadsTests.fs`. `getAll`'s pre-existing `NextAirDate` computation is itself an
unrelated per-row seam (`getNextEpisodeAirDate` + `getNextSeasonAirDate`, up to two queries per row,
present before this task and explicitly out of this task's scope per its own text: "This is not the task
to fix it" applies to the *analogous* per-season N+1 in `getBySlug`, and the same restraint was applied
here). The test therefore asserts two things separately: zero rows produces exactly 1 command (the
composed identity+cache+views `SELECT` alone), and N rows produces `1 + N * 2` (the fixed per-row cost
of the pre-existing, untouched `NextAirDate` seam) — proving the cache/view joins themselves add zero
per-row cost, without either hiding or silently fixing the pre-existing seam.

### `MetadataPending`'s "no third-party metadata yet" generalization needed no code change

The task's Notes named `MetadataPending` (`EpisodeDto`, ADR-0012) as the vocabulary that "generalizes"
to cover a cold, never-fetched entry, specifically to explain why no new Shared DTO field was needed at
the series level. No code changed: `source = 'jellyfin'` is still the only computation, and it already
means "no third-party metadata yet" as much as "materialized from Jellyfin" — there is no third
provenance value. The empty-cache acceptance criterion's "`MetadataPending = true`" is satisfied
vacuously by an empty `Seasons` list (`List.forall` over zero elements), which is exactly the point: the
series-level "cold entry" signal is the DTO's own `TmdbRating = None`/`Overview = ""`, not a new field.

## Consequences

### Positive
- Closes the stale-but-read gap for every field the task named, without any Shared DTO or client change.
- `series_list.next_up_*`/`season_count`/`episode_count` become fully dead columns ahead of
  `series-d5tpn`'s planned drop — no code path writes or reads them anymore.
- The views' structural drift-immunity (ADR-0046) is now actually exercised by real read traffic, not
  just proven by a standalone `MetadataCacheTests.fs` test.

### Negative / accepted tradeoff
- **`series_metadata_cache` has no write path going forward** (only the one-time
  `seedFromProjections` seed) — discovered mid-task, filed as `series-t3jkv` rather than fixed here,
  since wiring `SeriesRefresh`/`Api.addSeriesToLibraryImpl` to write it is a write-path change this
  task's explicit scope ("join in the query function, not the API layer" — a *read* composition task)
  did not cover. Until that task lands, every cache-sourced field is frozen at whatever the one-time seed
  captured, and a series added after that seed gets no cache row at all.
- **`getRecentlyAbandoned` was deliberately left unretargeted** — same staleness class as
  `getRecentlyFinished` had before this task, filed as `series-x9mfp`. Not touching it kept this task's
  diff scoped to its explicit function list; the task text named `getRecentlyFinished` but not its
  structural twin.
- `getCurrentlyWatchingCount`/`getCompletionRate` and a few dashboard-stats functions still read
  `series_list.episode_count` directly — left untouched as out of this task's named scope;
  `series-d5tpn`'s planned column drop will need to fix every remaining reader of the dropped columns
  anyway, so these are naturally that task's problem, not this one's.

## Alternatives considered

- **`COALESCE(cache_value, stale_projection_column)` for `TmdbRating`/`Overview`/`EpisodeRuntime`** —
  rejected: would silently mask a cache miss with an increasingly-wrong frozen value instead of the
  honest `None`/`""` degradation the task's acceptance criteria require.
- **Join `series_metadata_cache` into `getRecentSeries` for template consistency with `getAll`/
  `getRecentlyFinished`** — rejected: `RecentSeriesItem` has no field that would ever read from it: an
  unused join has a real cost and no benefit.
- **Fix `getRecentlyAbandoned`/wire the `series_metadata_cache` write path inside this task, since both
  are one-line-away and closely related** — rejected in favor of filing them as backlog: neither was in
  the task's explicit "What" list, and folding them in would have stretched this task's diff beyond what
  its acceptance criteria (and the verifier reading them) were scoped to check.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md`
- `.agentheim/knowledge/decisions/0046-series-episode-tree-renamed-into-cache-views-replace-materialized-columns.md`
- `.agentheim/knowledge/decisions/0047-series-refreshed-narrowed-to-real-airing-status-transitions.md`
- `src/Server/SeriesProjection.fs` (`getAll`, `getBySlug`, `getRecentSeries`, `getRecentlyFinished`,
  `getDashboardSeriesNextUp`, `recalculateProgress`) — the code this ADR describes.
- `tests/Server.Tests/SeriesProjectionReadsTests.fs` — cache-hit/cache-miss/multi-rewatch-session/
  query-count coverage.
- `.agentheim/contexts/series/backlog/series-t3jkv-wire-series-metadata-cache-write-path.md`,
  `.agentheim/contexts/series/backlog/series-x9mfp-getrecentlyabandoned-cache-composition.md` — the two
  gaps discovered mid-task and deliberately not fixed here.
- `.agentheim/contexts/series/todo/series-d5tpn-drop-columns-prove-drift-zero.md` — the task this read
  composition unblocks.
