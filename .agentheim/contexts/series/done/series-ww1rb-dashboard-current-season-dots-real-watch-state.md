---
id: series-ww1rb
title: Dashboard series cards show only the *current season's* episode dots with a season rail above, and mark the episodes actually watched — extend `DashboardSeriesNextUp` with per-season touched flags and current-season per-episode watched flags, joined at query time per ADR-0048
status: done
type: feature
context: series
created: 2026-08-07
completed: 2026-08-08
depends_on: [design-system-001, design-system-mz9v7]
blocks: []
tags: [dashboard, ui, read-model, series]
related_adrs: [0048, 0046]
related_research: []
prior_art: [series-q8jwc, series-m7fdk]
---

## Why

Two defects in the dashboard series card, both traceable to the same missing data.

**One: the dot row is unreadable for long shows.** The card renders one segment per episode
across the *entire series*. A 120-episode show becomes a hairline that carries no information.
The user's mental model is "where am I in this show" (seasons) plus "where am I in this
season" (episodes) — one flat row of every episode ever answers neither.

**Two: the watched episodes are the wrong ones.** `DashboardSeriesNextUp` carries only
`WatchedEpisodeCount` and `EpisodeCount` — a count, not a set. `DesignSystem.progressSegmented`
therefore paints the *first N* segments. Watch episodes 1-3 and 6-7 and the card shows five gold
segments at the front instead of gold-gold-gold-brown-brown-gold-gold. The card asserts
something about the user's history that is simply untrue.

The per-episode truth already exists on the server: `series_episode_progress` holds
`(series_slug, rewatch_id, season_number, episode_number, watched_date)`, and
`SeriesProjection.recalculateProgress` already reduces exactly that table to the single
count it hands the client. The data is thrown away one step before it reaches the DTO.

## What

**Read model.** Extend `DashboardSeriesNextUp` (`src/Shared/Shared.fs:290`) with three fields:

- `CurrentSeasonNumber: int` — the season the episode row represents.
- `CurrentSeasonWatched: bool list` — one entry per episode of that season, in episode order; `true` when the episode is watched in **any** rewatch session (same "distinct across all rewatches" rule `recalculateProgress` already uses at `SeriesProjection.fs:204-209`).
- `SeasonsTouched: bool list` — one entry per season of the series, in season order; `true` when that season has ≥1 watched episode in any rewatch session.

**Current season** is the season of the Next Up episode (`NextUpSeason`). When there is no
Next Up — the series is finished, fully watched, or abandoned — fall back to the highest-numbered
season. A series with no season/episode cache data at all yields `CurrentSeasonNumber = 0` and
empty lists, which the primitives render as an empty row.

Per **ADR-0048** these compose **inside `SeriesProjection.getDashboardSeriesNextUp`**, joining
`series_season_cache` / `series_episode_cache` (structure) against `series_episode_progress`
(watch state) — *not* in `Api.fs`. A cache miss degrades to empty lists, never a synchronous
TMDB fetch on the read path. Watch out for the N+1 shape: the dashboard fetches ~5-6 series,
so a single grouped query over the whole slug set is preferable to a per-series round trip.

**Client wiring.** Three surfaces move onto [[design-system-mz9v7]]'s
`seriesSeasonEpisodeProgress`:

1. `seriesNextEpisodeCard` → `DesignSystem.nextEpisodeHeroCard` (`Pages/Dashboard/Views.fs:1106`, `:1146`) — the live "Next episode" section on the All tab. This is the card that prompted the report.
2. The StyleGuide's `heroCard` / `secondaryMediaCard` specimens — prop shapes already changed by [[design-system-mz9v7]]; feed them fixture flag lists including a mid-season hole.
3. `seriesNextUpItemEnhanced` (`Pages/Dashboard/Views.fs:2630`) — the Series-tab "Next Up" list, today a continuous percentage bar overlaid on the poster bottom. Convert it to the same season-rail + episode-dots pattern. If the 4px poster-bottom overlay cannot carry two legible rows, move the progress out from under the poster into the text column beside it rather than shrinking it to illegibility — and say so in the BC README.

## Acceptance criteria

- [ ] `DashboardSeriesNextUp` carries `CurrentSeasonNumber: int`, `CurrentSeasonWatched: bool list`, and `SeasonsTouched: bool list`.
- [ ] A server test over a series with episodes 1-3 and 6-7 of season 1 watched returns `CurrentSeasonWatched = [true; true; true; false; false; true; true; …]` — the holes are preserved, not collapsed to a count.
- [ ] A server test over a series with seasons 1-3 where only season 2 has any watched episode returns `SeasonsTouched = [false; true; false]`.
- [ ] A server test confirms a season watched *completely* and a season watched *partially* both report `true` in `SeasonsTouched` — the rail has two states, not three.
- [ ] `CurrentSeasonNumber` equals `NextUpSeason` when a next-up episode exists, and the highest season number when it does not (finished / fully watched / abandoned series).
- [ ] An episode watched only in a non-default rewatch session still reports `true` — the flags are distinct across all rewatch sessions, matching `recalculateProgress`.
- [ ] A series with no rows in `series_episode_cache` returns `CurrentSeasonNumber = 0` and empty lists, and the dashboard renders without throwing.
- [ ] The composition lives in `SeriesProjection.getDashboardSeriesNextUp`, not `Api.fs` (ADR-0048). `rg` shows no new season/episode query in `Api.fs` for this DTO.
- [ ] `Administration.checkProjectionDrift` still reports zero discrepancies for `SeriesProjection` — no new materialized columns were added to `series_list` / `series_detail` to serve this (ADR-0051).
- [ ] The dashboard "Next episode" hero card renders one dot per episode of the current season only — a 120-episode series shows one season's worth of dots, not 120.
- [ ] The Series-tab "Next Up" list uses the same season-rail + episode-dots pattern rather than the continuous percentage bar.
- [ ] `npm test` passes (full Expecto suite) and `npm run build` succeeds.
- [ ] On a real series with a mid-season gap, the dashboard card visibly shows the gap in the correct position — the reported bug is gone as seen in the running app. [human-eye]
- [ ] The season rail reads as an at-a-glance answer to "how far into this show am I" on a long-running series. [human-eye]

## Notes

- **Depends on [[design-system-mz9v7]]** for `progressSeasons` / `progressEpisodes` / `seriesSeasonEpisodeProgress`. Do not hand-roll the dot markup here — the primitives and their StyleGuide specimens are that task's deliverable, and the signatures in its `What` section are the contract.
- `depends_on` also carries `design-system-001` per the Series BC README's frontend gate. It is already `done/`.
- Prior art: [[series-q8jwc]] established the query-time composition pattern this follows (`LEFT JOIN` the cache tier inside the query function, DTOs unchanged shape-wise); [[series-m7fdk]] is why the episode tree is `series_episode_cache` and why `series_next_up` is a view. ADR-0046 and ADR-0048 are the decisions of record.
- **Interacts with [[series-k4zpn]]** (todo, same BC): "Next Up must follow the furthest-watched episode, not the first unwatched one". That task changes what `NextUpSeason` *means* for a series with a mid-season gap — exactly the scenario this task renders. Not a hard dependency in either direction (this task derives `CurrentSeasonNumber` from whatever `NextUpSeason` reports, and the fallback-to-last-season rule is unaffected), but whichever ships second should re-check the other's tests. If both are in flight at once, expect a merge on `SeriesProjection.getDashboardSeriesNextUp`.
- The BC README's read-composition paragraph (`README.md`, the ADR-0048 paragraph) should gain a sentence naming the new per-episode/per-season fields, since it currently enumerates exactly what `getDashboardSeriesNextUp` composes.
- Deliberately **not** in scope: changing the meaning of `WatchedEpisodeCount` / `EpisodeCount`, which stay whole-series counts and still drive the "time remaining" estimate at `Views.fs:2635`.

## Outcome

Resumed from a prior interrupted session, which had already landed the server-side read model
correctly (verified against ADR-0048/0046 before building on it): `DashboardSeriesNextUp`
(`src/Shared/Shared.fs`) gained `CurrentSeasonNumber: int`, `CurrentSeasonWatched: bool list`,
`SeasonsTouched: bool list`, and `SeriesProjection.getDashboardSeriesNextUp` composes them via
two grouped queries (`series_episode_cache` for structure, `series_episode_progress` DISTINCT
across all rewatch sessions for watch state) over the whole dashboard slug set — no per-series
round trip, no new query in `Api.fs`. Added the 6 missing Expecto tests covering every
acceptance criterion (mid-season holes preserved, `SeasonsTouched` two-state semantics,
`CurrentSeasonNumber` fallback to the highest season with no Next Up, cross-rewatch-session
watch state, and the cache-miss empty-lists case) — all pass, full suite 686/686 green.

Client wiring: `seriesNextEpisodeCard` (Dashboard All-tab hero) now feeds
`nextEpisodeHeroCard`'s `SeasonsTouched`/`CurrentSeasonWatched` from the real DTO fields
instead of the placeholder prefix-derivation the interrupted session had left in place.
`seriesNextUpItemEnhanced` (Series-tab Next Up list) replaced its continuous
percentage-bar poster overlay with `DesignSystem.seriesSeasonEpisodeProgress` — the
40x56px thumbnail can't carry two legible rows, so the rail+dots pair moved into the text
column instead of shrinking (per the task's own escape hatch), documented in the BC README.
StyleGuide's `secondaryMediaCard` specimen fixture was fixed to use a genuine mid-season
hole (`heroCard`'s specimen already had one). `npm run build` succeeds.

Key files: `src/Shared/Shared.fs`, `src/Server/SeriesProjection.fs`,
`tests/Server.Tests/SeriesProjectionReadsTests.fs`, `src/Client/Pages/Dashboard/Views.fs`,
`src/Client/Pages/StyleGuide/Views.fs`, `.agentheim/contexts/series/README.md`.

No ADR needed — all decisions here were already governed by ADR-0048/0046; the poster-overlay
vs. text-column choice was explicitly anticipated by the task's own text, not a novel decision.

The two `[human-eye]` acceptance criteria (visible gap rendering on a real series, at-a-glance
readability) remain for the builder to confirm in the running app.

## Verifier note (iteration 1)

**REASONS:**
- Acceptance criterion "`CurrentSeasonNumber` equals `NextUpSeason` when a next-up episode exists, and the highest season number when it does not" (task file line 69) is only half-covered. The fallback clause is genuinely falsified by `tests/Server.Tests/SeriesProjectionReadsTests.fs:318-332` ("CurrentSeasonNumber falls back to the highest season when there is no Next Up"), but no test in the diff falsifies the primary clause: every test that asserts `CurrentSeasonNumber` does so on a series where `NextUpSeason` and `max seasonNumbers` coincide.
  - `SeriesProjectionReadsTests.fs:283-298` ("holey-show") seeds `[ mkSeason 1 8 ]` — a single season, so `NextUpSeason = 1` and `List.max seasonNumbers = 1` are indistinguishable.
  - `SeriesProjectionReadsTests.fs:300-306` ("touched-middle-show") is the one multi-season fixture where the branches diverge (seasons 1-3, watched S2E1 → next-up season 2, highest season 3), but it asserts only `SeasonsTouched`, never `CurrentSeasonNumber` or `CurrentSeasonWatched`.
  - `SeriesProjectionReadsTests.fs:308-316` ("two-state-show") next-up season 2 = highest season 2; `:334-342` ("alt-session-show") is single-season.
  - Consequence: an implementation of `src/Server/SeriesProjection.fs:1422-1423` that dropped the `NextUpSeason` branch entirely and always used `List.max seasonNumbers` would leave all 686 tests green. That is exactly the regression this task exists to prevent — a mid-series show (the motivating 120-episode case) would render the *final* season's dots instead of the current one's, silently.
- Related coverage gap on the same fixtures: `CurrentSeasonWatched` is never asserted on a multi-season series, so criterion line 74 ("the hero card renders one dot per episode of the current season only") is proven server-side only for single-season shows — the season-filtering at `SeriesProjection.fs:1426` is never exercised against a series that actually has other seasons' episodes to filter out.

**SUGGESTED_FIX:** Add one Expecto test in `dashboardSeasonEpisodeDotsTests` over a multi-season series whose next-up season is NOT the highest — e.g. seasons 1-3 with 4 episodes each, watch S2E1 and S2E3 only, then assert `CurrentSeasonNumber = 2` (not 3) and `CurrentSeasonWatched = [true; false; true; false]` (4 entries, season 2's, hole preserved). Extending the existing "touched-middle-show" case with those two assertions would also do it. No production change is expected — the implementation at `SeriesProjection.fs:1422-1429` already behaves correctly; only the falsifying test is missing.

**ITERATION_HINT:** likely-fixable

Supporting notes: `npm test` (686/686 passed, exit 0) and `npm run build` (exit 0) both ran clean from the worktree. Scope, BC README sync, ADR-0048/0046 consistency, and the no-protocol/no-INDEX rule all looked clean on inspection.

## Iteration 2 — gap closed

Extended the existing `"SeasonsTouched marks only the season with at least one watched
episode"` case (`tests/Server.Tests/SeriesProjectionReadsTests.fs`, "touched-middle-show"
fixture) with two more assertions on the same seeded data (seasons 1-3 of 2 episodes each,
only S2E1 watched) rather than adding a new fixture, per the verifier's suggested fix: the
frontier there is the sole watched tuple (2,1), so `NextUpSeason = 2` while the highest season
is 3 — exactly the divergence needed to falsify an implementation that dropped the
`NextUpSeason` branch and always used `List.max seasonNumbers`. Added assertions:
`CurrentSeasonNumber = 2` (not 3, the highest season) and `CurrentSeasonWatched = [true; false]`
(season 2's own two episodes, hole preserved, not season 3's). No production change — the
implementation at `SeriesProjection.fs:1422-1429` already behaved correctly. `npm test`:
686/686 passed, exit 0, run clean from the worktree root.
