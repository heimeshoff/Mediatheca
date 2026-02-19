# Task: Movie In Focus — domain + backend

**ID:** 001
**Milestone:** M1 - In Focus
**Size:** Medium
**Created:** 2026-02-19
**Dependencies:** None

## Objective
Movies can be toggled "In Focus" via a flag that auto-clears when a watch session is recorded.

## Details

### Domain (src/Server/Movies.fs)
- Add `InFocus: bool` field to `ActiveMovie` state (default `false`)
- New events:
  - `Movie_in_focus_set` (no payload)
  - `Movie_in_focus_cleared` (no payload)
- New commands:
  - `Set_movie_in_focus` → emits `Movie_in_focus_set` if not already in focus
  - `Clear_movie_in_focus` → emits `Movie_in_focus_cleared` if currently in focus
- Evolve: `Movie_in_focus_set` sets `InFocus = true`, `Movie_in_focus_cleared` sets `InFocus = false`
- **Auto-clear**: In the `decide` function for `Record_watch_session`, if `state.InFocus = true`, emit `Movie_in_focus_cleared` alongside `Watch_session_recorded`

### Serialization (src/Server/MovieSerialization.fs)
- Add serialization/deserialization for both new events
- Follow existing pattern (Thoth.Json.Net)

### Projection (src/Server/MovieProjection.fs)
- Add `in_focus INTEGER DEFAULT 0` column to both `movie_list` and `movie_detail` tables
- Handle both events in projection handler

### Shared Types (src/Shared/Shared.fs)
- Add `InFocus: bool` to `MovieListItem` record
- Add `InFocus: bool` to `MovieDetail` record

### API (src/Server/Api.fs)
- New endpoint: `setMovieInFocus: string -> bool -> Async<Result<unit, string>>` (movieSlug, inFocus)
- Implementation: load movie stream, dispatch Set_movie_in_focus or Clear_movie_in_focus based on bool

### Tests (tests/Server.Tests/)
- Test: setting in focus on a movie
- Test: clearing in focus on a movie
- Test: auto-clear on watch session recorded
- Test: idempotent (set in focus when already in focus → no event)
- Serialization round-trip tests for both events

## Acceptance Criteria
- [x] `Movie_in_focus_set` and `Movie_in_focus_cleared` events defined and serializable
- [x] `InFocus` field on ActiveMovie state, defaults to false
- [x] Auto-clear: recording a watch session clears In Focus
- [x] Projection columns updated on both tables
- [x] `InFocus` field on both shared DTOs
- [x] API endpoint functional
- [x] Tests passing

## Notes
- Follow the exact same event/command/evolve/decide patterns as existing movie events
- The auto-clear is the key behavior — ensure it's tested

## Work Log
<!-- Appended by /work during execution -->

### 2026-02-19 — Implementation complete

**Files modified:**
- `src/Server/Movies.fs` — Added `Movie_in_focus_set` and `Movie_in_focus_cleared` events, `Set_movie_in_focus` and `Clear_movie_in_focus` commands, `InFocus: bool` field on `ActiveMovie` (defaults false), evolve handlers, decide handlers with idempotency, auto-clear on `Record_watch_session`, and serialization/deserialization for both new events.
- `src/Server/MovieProjection.fs` — Added `in_focus INTEGER NOT NULL DEFAULT 0` column to both `movie_list` and `movie_detail` tables (with migration for existing DBs), projection handlers for both events, updated `getAll` and `getBySlug` queries to include `in_focus`.
- `src/Shared/Shared.fs` — Added `InFocus: bool` to both `MovieListItem` and `MovieDetail` records. Added `setMovieInFocus: string -> bool -> Async<Result<unit, string>>` to `IMediathecaApi`.
- `src/Server/Api.fs` — Implemented `setMovieInFocus` endpoint dispatching `Set_movie_in_focus` or `Clear_movie_in_focus` based on the bool parameter.
- `tests/Server.Tests/MoviesTests.fs` — Added 7 new domain tests (set in focus, idempotent set, clear in focus, idempotent clear, auto-clear on watch session, no auto-clear when not in focus, default is false) + 2 serialization round-trip tests + updated removed-movie test to include new commands.

**Test results:** All 218 tests pass.
**Build:** Client Fable build passes.
**Note:** Concurrent task 012 has introduced `HowLongToBeat.fs` with compilation errors in `Server.fsproj`. Tests and build were verified with that reference temporarily removed; the reference was restored afterward so task 012's changes are preserved.
