---
id: series-d5tpn
title: Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard
status: todo
type: refactor
context: series
created: 2026-08-01
completed:
depends_on: [series-q8jwc, administration-kv7dp]
blocks: []
tags: [series, drift, projection, determinism, schema]
related_adrs: [0031, 0032, 0033]
related_research: []
prior_art: [administration-btvqa, administration-xjmda]
---

## Why

This is the proof step. Everything before it is preparation; drift only reaches 0 when the columns
physically stop existing. Do not skip it and do not merge it into `series-q8jwc` — its first
acceptance criterion is the whole point of the exercise.

Drift goes to zero by **removing columns, not by ignoring them**. An ignore-list on
`Administration.diffTable` would be a second hand-maintained schema registry — the exact species
ADR-0031 explicitly rejected when it chose `PRAGMA table_info` over a hand-maintained PK map — and it
would be a mechanism for declaring this bug's recurrence acceptable. Column removal makes the same
statement in a form SQLite enforces. `diffTable` therefore stays byte-for-byte as written and still
diffs every non-PK column; it reads zero because there is nothing left to find.

## What

- `ALTER TABLE ... DROP COLUMN` (`try/with`, idempotent — a second run throws "no such column"),
  **after** the seed, in the same release:
  - `series_list` drops `tmdb_rating`, `season_count`, `episode_count`, `next_up_season`,
    `next_up_episode`, `next_up_title`.
  - `series_detail` drops `overview`, `backdrop_ref`, `tmdb_rating`, `episode_runtime`, plus the
    vestigial `jellyfin_id` (JellyfinStore has owned it since the ADR-0033 era — same class of leftover).
  - **`status` stays** in both tables. Under `series-r2xhv` it is written exclusively by
    `Series_added_to_library` and the narrowed `Series_refreshed`, both of which carry it — the
    identity-card clause of `infrastructure-e4kwm`.
- Update each `CREATE TABLE IF NOT EXISTS` in `SeriesProjection.fs` to match, so fresh installs and
  migrated installs converge on the same schema.
- Remove `series_seasons` / `series_episodes` from `projectionTables` (now derived from `tableRegistry`).
- Resolve the two known residual status discrepancies carried over from `series-r2xhv`:
  - `love-death-robots-2019` — replay yields `Ended`, live holds `Returning`. Append a compensating
    `Series_refreshed` via the ADR-0032 composer.
  - `silo-2023-2` — replays to `Returning` with no live row (the `series_list` `onlyInShadow` row).
    Decide remove-vs-restore and record the call in the ADR.
- Rebuild SeriesProjection once.
- **Remove `"SeriesProjection"` from `lossyRebuildProjections`.** If the list is then empty, delete the
  whole `administration-kv7dp` mechanism — `lossyRebuildProjections`, `lossyRebuildRejectionMessage`,
  its test, the `MEDIATHECA_ALLOW_LOSSY_REBUILD` env var, the `CinemarcoImport.fs:866` branch, the
  rejection arm — and mark that task's ADR superseded.

## Acceptance criteria

- [ ] **Expecto: `Administration.checkProjectionDrift` returns an empty `Discrepancies` list for `SeriesProjection` against a fixture exercising add + refresh + Jellyfin materialization + episode-watched.** This is the gate.
- [ ] Live verification: the Settings > Projections drift check reports **0** discrepancies overall.
- [ ] Expecto: `Projection.rebuildProjection` over SeriesProjection leaves `series_metadata_cache`, `series_season_cache` and `series_episode_cache` row counts unchanged.
- [ ] `PRAGMA table_info(series_list)` and `PRAGMA table_info(series_detail)` contain none of the dropped column names, and both still contain `status`.
- [ ] `Administration.diffTable` is unchanged in the diff — no ignore-list, no per-column exclusion.
- [ ] `grep -c "SeriesProjection" src/Server/Administration.fs` returns 0 in the `lossyRebuildProjections` neighbourhood.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"Drift goes to zero by removing columns, not by ignoring them; the shadow replay never reads
the cache"*, `scope: administration`, explicitly **amending ADR-0031** — which it preserves and
strengthens, since the throwaway-connection design and its by-construction read-only guarantee both
stand.

Record the accepted price: `series_list.next_up_*`, `season_count` and `episode_count` can no longer be
materialized, because a projection may never read the cache. They become the SQL views built in
`series-m7fdk` — computed on read, structurally incapable of drifting, invisible to `PRAGMA table_info`.

Reasonable fold: merge this ADR into `administration-c3nvp`'s.
