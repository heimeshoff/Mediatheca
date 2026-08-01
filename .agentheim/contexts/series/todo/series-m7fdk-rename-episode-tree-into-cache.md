---
id: series-m7fdk
title: Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views
status: todo
type: refactor
context: series
created: 2026-08-01
completed:
depends_on: [administration-c3nvp]
blocks: []
tags: [series, metadata, cache, drift, tmdb, jellyfin]
related_adrs: [0012, 0025, 0031, 0039, 0040]
related_research: [tv-series-metadata-fallback-sources]
prior_art: [integration-m4k7p, integration-q7wv3, integration-007]
---

## Why

`series_episodes` holds 161 rows that exist **only** in the live tables — they came from refreshes
and Jellyfin materialization, not from the event log. `series_seasons` holds 21 such rows.

The log's episode data (106 `Series_added_to_library` snapshots, 4514 episodes) and the live tables'
episode data (4624 episodes after 780 refreshes plus Jellyfin materialization) are **two sources for
the same rows**, which guarantees permanent drift. Exactly one must own the tree, and it must be the
one that can hold rows the other cannot produce.

## What

- In `MetadataCache.initialize`, **statement order is load-bearing**: run

  ```sql
  ALTER TABLE series_episodes RENAME TO series_episode_cache;
  ALTER TABLE series_seasons  RENAME TO series_season_cache;
  ```

  **first**, before any `CREATE TABLE IF NOT EXISTS` could claim the target name and strand the old
  rows. Idempotent via `try/with` — a second run throws "no such table" and is swallowed, the
  existing ALTER-migration idiom in `SeriesProjection.fs:120-127`.

  The rename preserves all 161 structural rows, every `source` provenance value and every `still_ref`
  with zero data movement.

- Create `series_metadata_cache` (flat per-series fields: `overview`, `backdrop_ref`, `tmdb_rating`,
  `episode_runtime`, `fetched_at`) and seed it from `series_detail`.
- Add `fetched_at` to both renamed tables via `try` / `ALTER TABLE ... ADD COLUMN`.
- Create two views, replacing materialized columns that can no longer be maintained (a projection may
  never read the cache — see `administration-c3nvp`):
  - `series_next_up` — `ROW_NUMBER() OVER (PARTITION BY series_slug ORDER BY season_number, episode_number)`
    over `series_episode_cache LEFT JOIN series_episode_progress ... WHERE p.series_slug IS NULL`.
  - `series_episode_counts` — per-series season and episode counts.

  Views are computed on read, structurally incapable of drifting, and invisible to `PRAGMA table_info`.
- Add `CREATE INDEX IF NOT EXISTS idx_series_progress_slug_episode ON series_episode_progress (series_slug, season_number, episode_number)`
  in `SeriesProjection.createTables`. `series_episode_progress`'s PK is
  `(series_slug, rewatch_id, season_number, episode_number)`, so the view's join cannot use it.
  Indexes do not appear in `PRAGMA table_info`, so this is invisible to the diff and exists identically
  in the shadow connection.
- **In the same commit**, retarget `Administration.imageRefColumns`:
  `series_seasons/poster_ref` → `series_season_cache/poster_ref`,
  `series_episodes/still_ref` → `series_episode_cache/still_ref`.
- Register both renamed tables as `Cache` in `tableRegistry`.
- Retarget ADR-0040's `SeriesProjection.getJellyfinEpisodesMissingStill` and `backfillEpisodeStill`
  at the cache tables — WHERE-clause logic unchanged.

## Acceptance criteria

- [ ] Expecto: after `initialize` on a fixture pre-populated as `series_episodes` / `series_seasons`, the renamed tables hold **exactly** the pre-migration row counts, with `source` and `still_ref` values byte-identical.
- [ ] Expecto: `initialize` run twice is a no-op the second time (rename already applied).
- [ ] **Expecto (data-loss regression): `getReferencedImageRefs` on a fixture containing episode stills and season posters returns a non-empty set after the rename.**
- [ ] `Administration.imageRefColumns` contains no entry naming `series_episodes` or `series_seasons`; `grep -c '"series_episodes"' src/Server/Administration.fs` returns 0.
- [ ] Expecto: `SELECT * FROM series_next_up WHERE series_slug = ?` returns exactly one row per series with at least one unwatched episode, and zero rows for a fully-watched series — proving the `LEFT JOIN ... IS NULL` cannot fan out across rewatch sessions.
- [ ] Expecto: `series_episode_counts` matches a direct `COUNT(*)` over `series_episode_cache` for a multi-season fixture.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

The `getReferencedImageRefs` regression test is the one standing between this task and hard-deleting
the entire stills cache on the next ADR-0025 orphan purge. It is not optional and must not be folded
into a broader assertion.

`series_seasons` and `series_episodes` cease to exist as projection tables. `SeriesProjection` stops
writing them in `series-r2xhv`; the `Series_added_to_library` snapshot's tree becomes a **cache seed
written at the command path**, not by replay.
