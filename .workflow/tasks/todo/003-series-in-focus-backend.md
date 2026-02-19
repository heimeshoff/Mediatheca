# Task: Series In Focus — domain + backend

**ID:** 003
**Milestone:** M1 - In Focus
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** None

## Objective
TV Series can be toggled "In Focus" via a flag that auto-clears when an episode is watched.

## Details

### Domain (src/Server/Series.fs)
- Add `InFocus: bool` field to `ActiveSeries` state (default `false`)
- New events:
  - `Series_in_focus_set` (no payload)
  - `Series_in_focus_cleared` (no payload)
- New commands:
  - `Set_series_in_focus` → emits `Series_in_focus_set` if not already in focus
  - `Clear_series_in_focus` → emits `Series_in_focus_cleared` if currently in focus
- Evolve: `Series_in_focus_set` sets `InFocus = true`, `Series_in_focus_cleared` sets `InFocus = false`
- **Auto-clear**: In the `decide` function for `Mark_episode_watched`, if `state.InFocus = true`, emit `Series_in_focus_cleared` alongside `Episode_watched`. Same for `Mark_season_watched` and `Mark_episodes_watched_up_to`.

### Serialization (src/Server/SeriesSerialization.fs)
- Add serialization/deserialization for both new events

### Projection (src/Server/SeriesProjection.fs)
- Add `in_focus INTEGER DEFAULT 0` column to both `series_list` and `series_detail` tables
- Handle both events in projection handler

### Shared Types (src/Shared/Shared.fs)
- Add `InFocus: bool` to `SeriesListItem` record
- Add `InFocus: bool` to `SeriesDetail` record

### API (src/Server/Api.fs)
- New endpoint: `setSeriesInFocus: string -> bool -> Async<Result<unit, string>>` (seriesSlug, inFocus)

### Tests
- Test: setting in focus on a series
- Test: clearing in focus
- Test: auto-clear on episode watched
- Test: auto-clear on season marked watched
- Test: auto-clear on episodes watched up to
- Test: idempotent behavior
- Serialization round-trip tests

## Acceptance Criteria
- [ ] `Series_in_focus_set` and `Series_in_focus_cleared` events defined and serializable
- [ ] `InFocus` field on ActiveSeries state, defaults to false
- [ ] Auto-clear: watching any episode(s) clears In Focus
- [ ] Projection columns updated on both tables
- [ ] `InFocus` field on both shared DTOs
- [ ] API endpoint functional
- [ ] Tests passing

## Notes
- Auto-clear triggers on all three episode watch commands (single episode, season, up-to)
- This is the same pattern as Movie In Focus but with different auto-clear triggers

## Work Log
<!-- Appended by /work during execution -->
