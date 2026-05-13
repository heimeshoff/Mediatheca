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
- [x] `Series_in_focus_set` and `Series_in_focus_cleared` events defined and serializable
- [x] `InFocus` field on ActiveSeries state, defaults to false
- [x] Auto-clear: watching any episode(s) clears In Focus
- [x] Projection columns updated on both tables
- [x] `InFocus` field on both shared DTOs
- [x] API endpoint functional
- [x] Tests passing

## Notes
- Auto-clear triggers on all three episode watch commands (single episode, season, up-to)
- This is the same pattern as Movie In Focus but with different auto-clear triggers

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Task completed
- Added `Series_in_focus_set` and `Series_in_focus_cleared` events to `SeriesEvent` DU in `src/Server/Series.fs`
- Added `Set_series_in_focus` and `Clear_series_in_focus` commands to `SeriesCommand` DU
- Added `InFocus: bool` field to `ActiveSeries` state (defaults to `false`)
- Updated `evolve` function to handle both new events
- Updated `decide` function:
  - `Set_series_in_focus` emits `Series_in_focus_set` (idempotent)
  - `Clear_series_in_focus` emits `Series_in_focus_cleared` (idempotent)
  - `Mark_episode_watched` auto-clears InFocus if set
  - `Mark_season_watched` auto-clears InFocus if set
  - `Mark_episodes_watched_up_to` auto-clears InFocus if set
- Added serialization/deserialization for both new events (inline in `Series.Serialization`)
- Updated `SeriesProjection.fs`:
  - Added `in_focus INTEGER NOT NULL DEFAULT 0` column to `series_list` and `series_detail` CREATE TABLE
  - Added migration ALTER TABLE for existing databases
  - Added event handlers for `Series_in_focus_set` and `Series_in_focus_cleared`
  - Updated `getAll` and `getBySlug` queries to read `in_focus` column
- Updated `Shared.fs`:
  - Added `InFocus: bool` to `SeriesListItem` record
  - Added `InFocus: bool` to `SeriesDetail` record
  - Added `setSeriesInFocus: string -> bool -> Async<Result<unit, string>>` to `IMediathecaApi`
- Updated `Api.fs`: implemented `setSeriesInFocus` endpoint
- Added tests in `SeriesTests.fs`:
  - Setting in focus on a series
  - Setting in focus when already in focus (idempotent)
  - Clearing in focus
  - Clearing when not in focus (idempotent)
  - InFocus defaults to false
  - Auto-clear on episode watched
  - Auto-clear on season marked watched
  - Auto-clear on episodes watched up to
  - Episode watched when not in focus does not emit clear
  - Serialization round-trip for Series_in_focus_set
  - Serialization round-trip for Series_in_focus_cleared
  - Added both events to "All event types" round-trip test
- All 229 tests pass (`npm test`)
- Fable client build passes (`npm run build`)
