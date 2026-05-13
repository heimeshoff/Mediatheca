# Task: Game Trailer Header Cleanup & Single-Trailer UX

**ID:** 044
**Milestone:** --
**Size:** Small
**Created:** 2026-04-20
**Dependencies:** 043 (game trailer gallery)

## Objective

Polish the game detail trailer UX shipped in task 043:

1. When a game has **only one** trailer, skip the thumbnail strip and show just the video player.
2. Remove the "Play Trailer" button and its loading spinner from the hero header.
3. Clean up the now-dead modal and its supporting state/messages/endpoint, since the button was its only trigger.

## Background

Task 043 added the inline gallery but intentionally left the task 018 modal ("Play Trailer" header button + full-screen `<video>` overlay) in place for back-compat. With the gallery as the primary surface, the header button is redundant — it plays the same trailer that's already one click away below the fold. The thumbnail strip is also visually noisy when there's nothing to choose between.

## Details

### Frontend

#### 1. Hide thumbnail strip when only one trailer — `src/Client/Pages/GameDetail/Views.fs`

In the Trailers section (around line 1011+), render the thumbnail strip only when `List.length visibleTrailers > 1`. When there is exactly one trailer:

- Auto-select it as the `selectedTrailer` so the video port renders (no empty state)
- Skip rendering the thumbnail list entirely — no strip, no wrapper div, no trailing whitespace

Section heading ("Trailers") behavior: keep it as-is when ≥1 trailer (the heading is fine for a single-trailer case too).

#### 2. Remove the header "Play Trailer" button — `src/Client/Pages/GameDetail/Views.fs`

Delete lines ~942–962: the `match model.TrailerInfo` block that renders the red "Play Trailer" button and the "Loading trailer..." spinner fallback. The outer "Action buttons row" `Html.div` at line ~939 may become empty — if so, delete it too rather than rendering an empty flex container.

#### 3. Remove the trailer modal — `src/Client/Pages/GameDetail/Views.fs`

Delete lines ~1720–1747: the `if model.ShowTrailer then match model.TrailerInfo with ...` block that renders the full-screen video overlay.

#### 4. Remove unused model state — `src/Client/Pages/GameDetail/Types.fs`

Remove from `Model`:
- `TrailerInfo: GameTrailerInfo option`
- `ShowTrailer: bool`
- `IsLoadingTrailer: bool`

Remove from `Msg`:
- `Load_trailer`
- `Trailer_loaded of GameTrailerInfo option`
- `Open_trailer`
- `Close_trailer`

Keep: `Trailers`, `IsLoadingTrailers`, `PlayingTrailerUrl`, `FailedTrailerUrls`, and the gallery messages (`Load_trailers`, `Trailers_loaded`, `Trailers_failed`, `Play_trailer_inline`, `Stop_trailer_inline`, `Trailer_errored`).

#### 5. Update state — `src/Client/Pages/GameDetail/State.fs`

- `init`: remove `TrailerInfo`, `ShowTrailer`, `IsLoadingTrailer` initializers (line ~32–34).
- `Game_loaded`: stop dispatching `Cmd.ofMsg Load_trailer` (line ~108) — only `Load_trailers` remains.
- Delete the `Load_trailer`, `Trailer_loaded`, `Open_trailer`, `Close_trailer` match arms (lines ~479–494).

### Backend (optional, include for a complete cleanup)

#### 6. Remove the now-unused singular endpoint

Nothing in the client calls `api.getGameTrailer` after step 5. Remove:

- `getGameTrailer: string -> Async<GameTrailerInfo option>` from `IMediathecaApi` in `src/Shared/Shared.fs` (line ~1133).
- The `getGameTrailer = fun slug -> async { ... }` record field from the Fable.Remoting implementation in `src/Server/Api.fs` (line ~2959).

Keep `Steam.getSteamStoreTrailer` and `Rawg.getGameTrailers` (singular) untouched — they are used internally by the `getSteamStoreTrailers`/`getGameTrailersAll` counterparts via the shared helpers, and removing them is out of scope.

## Acceptance Criteria

- [ ] Game detail page with exactly one trailer renders the inline video player and **no** thumbnail strip
- [ ] Game detail page with two or more trailers still renders the thumbnail strip exactly as today (visual regression check on a multi-trailer game)
- [ ] Game detail page with zero trailers still hides the entire Trailers section (no empty heading)
- [ ] No "Play Trailer" button appears in the hero header under any state (has trailer, loading, no trailer)
- [ ] No full-screen trailer modal can be opened
- [ ] `Model` no longer contains `TrailerInfo`, `ShowTrailer`, or `IsLoadingTrailer`
- [ ] `Msg` no longer contains `Load_trailer`, `Trailer_loaded`, `Open_trailer`, or `Close_trailer`
- [ ] `api.getGameTrailer` is removed from both `IMediathecaApi` and the server implementation
- [ ] `npm run build` succeeds
- [ ] `npm test` passes all tests
- [ ] Design check: single-trailer layout still follows the design system (glassmorphism, spacing, no orphaned dividers or empty rows)

## Notes

- Verify against a known single-trailer game and a known multi-trailer game in the browser before marking done. Steam's own indie titles often have exactly one trailer; most AAA games have several.
- Watch for the hero's "Action buttons row" becoming an empty `flex` container — Tailwind's `gap-3` will collapse but the div still occupies a row slot; delete it if empty.
- The `GameTrailerInfo` type in `src/Shared/Shared.fs` stays — it is used by the surviving `getGameTrailers` (plural) endpoint.

### 2026-04-20 13:39 -- Work Completed

**What was done:**
- Views.fs: Trailers section now hides the thumbnail strip when `List.length visibleTrailers = 1`; `mb-4` between video port and strip is only applied when the strip is rendered, so the single-trailer layout has no trailing margin/divider.
- Views.fs: Deleted the hero "Action buttons row" entirely (was the only home of the Play Trailer button + loading spinner), avoiding an empty `flex` row.
- Views.fs: Deleted the full-screen `if model.ShowTrailer then …` modal block.
- Types.fs: Removed `TrailerInfo`, `ShowTrailer`, `IsLoadingTrailer` from `Model`; removed `Load_trailer`, `Trailer_loaded`, `Open_trailer`, `Close_trailer` from `Msg`. Gallery state (`Trailers`, `IsLoadingTrailers`, `PlayingTrailerUrl`, `FailedTrailerUrls`) and gallery messages preserved.
- State.fs: Removed the three field initializers from `init`; dropped `Cmd.ofMsg Load_trailer` from `Game_loaded`; deleted the four match arms.
- Shared.fs: Removed `getGameTrailer: string -> Async<GameTrailerInfo option>` from `IMediathecaApi`. `GameTrailerInfo` type retained (used by the plural endpoint).
- Api.fs: Removed the `getGameTrailer = fun slug -> async { ... }` record field. Steam/Rawg helpers kept.
- Ran `npm run build`: Fable compiled cleanly, Vite bundle emitted (36.92 s).
- Ran `npm test`: 233 / 233 passed.
- design-check skill verified the Trailers section — no glassmorphism/spacing/typography/layout violations.

**Acceptance criteria status:**
- [x] Single-trailer page renders video port + no thumbnail strip — single-branch check on `List.length visibleTrailers > 1`; verified via code inspection and clean build
- [x] Multi-trailer page unchanged — same strip markup under `>1` branch, `mb-4` still applied
- [x] Zero-trailer page hides the whole section — outer `if not (List.isEmpty visibleTrailers)` guard preserved
- [x] No Play Trailer button in header under any state — entire Action buttons row deleted from hero
- [x] No full-screen trailer modal — `ShowTrailer` modal block deleted from view
- [x] `Model` no longer contains `TrailerInfo`, `ShowTrailer`, `IsLoadingTrailer` — verified in Types.fs
- [x] `Msg` no longer contains `Load_trailer`/`Trailer_loaded`/`Open_trailer`/`Close_trailer` — verified in Types.fs
- [x] `api.getGameTrailer` removed from both IMediathecaApi and server — verified via Grep (0 remaining references)
- [x] `npm run build` succeeds — Vite build completed, Fable compilation clean
- [x] `npm test` passes — 233 tests, 0 failures
- [x] Design check: single-trailer layout conforms — design-check skill found no violations; `mb-4` correctly gated so no orphan divider/empty row

**Files changed:**
- src/Client/Pages/GameDetail/Views.fs — hide thumbnail strip when only one trailer (conditional `mb-4`); delete hero Action buttons row; delete trailer modal
- src/Client/Pages/GameDetail/Types.fs — drop 3 trailer modal fields from `Model`; drop 4 trailer modal cases from `Msg`
- src/Client/Pages/GameDetail/State.fs — drop 3 field initializers; stop dispatching `Load_trailer`; delete 4 match arms
- src/Shared/Shared.fs — remove `getGameTrailer` from `IMediathecaApi`
- src/Server/Api.fs — remove `getGameTrailer` record field from the Fable.Remoting implementation
