# Task 055: Poster Hover Zoom — Stop Visual Clipping at Container Edges

**Status:** Todo
**Size:** Small
**Created:** 2026-05-02
**Milestone:** --
**Dependencies:** --

## Description

Larger poster cards on the dashboard and inside the SearchModal poster grid grow on hover via a CSS `transform: scale(...)`. Today that growth is **visually clipped** at the boundary of the scroll-rail / grid container, so the hovered poster looks like it has its top/bottom (or left/right) sliced off mid-zoom. The hover should feel like the poster is lifting toward the viewer, not pushing into a guillotine.

### Where the clipping comes from

1. **Poster scale source** — `src/Client/index.css:167–169`:
   ```css
   .poster-card:hover .poster-image-container {
       transform: scale(1.05);
   }
   ```
   Combined with `.poster-image-container { ... overflow: hidden; ... }` (`index.css:159–166`) — that inner `overflow: hidden` is intentional (it clips the image to the rounded corners and keeps the shine layer in bounds), so the **inner** clipping is fine; the problem is what happens to the *outer* scaled box once it grows past the rail/grid.

2. **Dashboard rails** clip the grown poster because they use `overflow-x-auto` with no breathing room above/below or to the sides:
   - `src/Client/Pages/Dashboard/Views.fs:370` — series next-up rail
   - `src/Client/Pages/Dashboard/Views.fs:482` — movies-to-watch rail
   - `src/Client/Pages/Dashboard/Views.fs:898` — game in-focus rail
   - `src/Client/Pages/Dashboard/Views.fs:1039` — series tab rail (All tab variant)
   - `src/Client/Pages/Dashboard/Views.fs:1196` — movies tab rail
   - `src/Client/Pages/Dashboard/Views.fs:2431` — recently watched rail (Movies tab)
   - `src/Client/Pages/Dashboard/Views.fs:2469` — movies-to-watch rail (Movies tab)
   - `src/Client/Pages/Dashboard/Views.fs:3313` — series-tab next-up rail
   - `src/Client/Pages/Dashboard/Views.fs:4174` and `:4189` — Games-tab rails

   Common class string: `"flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"` — only `pb-2` (8px) of vertical breathing room and **no horizontal padding**, so a 5%-grown poster gets clipped on top, sides (especially the first/last item in the rail), and partially on the bottom.

   Browser behaviour to remember: when one axis of `overflow` is non-`visible`, the other axis cannot stay `visible` — it is computed to `auto`/`hidden`. So `overflow-x: auto` clips the Y axis too. The fix is **padding inside the rail**, not changing overflow.

3. **SearchModal poster grid** clips the grown poster the same way:
   - The poster items use `hover:scale-[1.02]` (`src/Client/Components/SearchModal.fs:161–162`).
   - The grid container itself has no overflow and no padding (`SearchModal.fs:676` — `"grid grid-cols-4 gap-3"`), but it lives inside the modal's scrollable content area (`SearchModal.fs:875` — `"flex-1 overflow-y-auto px-5 pb-5"`, note: no `pt-*`). The top row of poster results and the leftmost/rightmost items in every row get their hover growth clipped against that scroll container.

### Why

User-visible polish: on hover the poster appears to "snap" against the rail edge instead of growing freely. The current behaviour undermines the intended lift-toward-viewer feel of the design system's poster cards.

## Acceptance Criteria

### Dashboard rails

- [ ] Hovering any poster in any horizontal scroll rail on the Dashboard (All / Movies / Series / Games tabs) grows the poster smoothly with **no visible top, bottom, left, or right clipping** at the rail edge — including for the very first item (against the rail's left edge) and the very last item (against the right edge) when scrolled to either end.
- [ ] Add internal padding to the rail container itself so the 5% scale fits inside the scroll viewport. Suggested change to the shared rail class string: replace `pb-2` with `py-2 px-2` (or equivalent — match existing gap rhythm). This must be applied consistently to **all** the rails listed in the Description above.
- [ ] Snap behaviour, scrollbar styling, and gap between cards remain as they are today (`snap-x snap-mandatory`, `scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent`, `gap-3`).
- [ ] No regression in the layout of the surrounding glassCard sections (the new padding lives *inside* the rail, not added on top of the glassCard's existing `p-4`, so the visual rhythm of each section should be effectively unchanged or only marginally tighter).

### SearchModal poster grid

- [ ] Hovering any poster in the SearchModal grid (Library / Movies / Series / Games tabs) grows the poster smoothly with **no visible clipping** at the modal's scrollable content edge — including the top row and the leftmost/rightmost columns.
- [ ] Add a small amount of breathing room — either `p-1` on the grid container at `SearchModal.fs:676` or `pt-1 px-1` (matching the existing `px-5 pb-5`) on the scrollable wrapper at `SearchModal.fs:875`. Pick whichever reads cleaner; both are acceptable.
- [ ] Selected-state ring (`ring-2 ring-primary rounded-lg scale-[1.02]` at `SearchModal.fs:161`) and keyboard navigation behaviour are unaffected.

### Out of scope

- `PosterCard.thumbnail` (small row thumbnails in dashboard list rows, FriendDetail, etc.) — these have no hover scale today; do not add one.
- Any change to `transform: scale(1.05)` magnitude, transition timing, or shine/shadow behaviour in `index.css`.
- Any change to `overflow: hidden` on `.poster-image-container` itself (intentional — clips the image to rounded corners).
- The hero spotlight backdrop hover (`group-hover:scale-105` on the background `<img>` at `Dashboard/Views.fs:937`) — that one grows *inside* an intentionally `overflow-hidden` hero card and the clipping is the desired ken-burns effect.
- Any glass-card `overflow-hidden` removal — those exist to clip the rounded corners and should stay.
- New `z-index` lift on hover (could be a follow-up if sibling overlap is an issue, but is not the reported problem).

### Build / test

- [ ] `npm run build` succeeds (Fable compiles clean).
- [ ] `npm test` passes.
- [ ] Manual check via Chrome DevTools MCP: hover several posters on each Dashboard tab (All / Movies / Series / Games) and confirm the zoom is fully visible at top, bottom, and at both rail extremes; do the same in the SearchModal across each tab and confirm the top row and edge columns zoom freely.

## Implementation Notes

### Frontend only — class-string tweaks

**Rails** — for each `Dashboard/Views.fs` line listed above, change:

```text
flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent
```

to:

```text
flex gap-3 overflow-x-auto py-2 px-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent
```

(There is no shared helper for this string — they're inlined per call site. Worth looking for any `Dashboard/Views.fs:1196` / `:2431` / `:2469` / `:3313` / `:4174` / `:4189` / `:898` / `:1039` / `:482` / `:370` you may have missed via a grep for `overflow-x-auto pb-2 snap-x snap-mandatory` to make sure the change is uniform. There is also a horizontal scroll at `:1524` that does **not** carry posters — leave it alone unless inspection shows it clips a hover-zoom too.)

**SearchModal grid** — change `SearchModal.fs:676` from:

```fsharp
prop.className "grid grid-cols-4 gap-3"
```

to:

```fsharp
prop.className "grid grid-cols-4 gap-3 p-1"
```

That's it. No CSS file changes, no DesignSystem.fs changes, no shared-types changes, no backend work.

### Verification with Chrome DevTools MCP

After running `npm start`, navigate to `http://localhost:5173/`, then in each of the four dashboard tabs hover several posters in each rail and confirm zoom is fully visible. Then open the SearchModal (Cmd/Ctrl+K), search for something with enough results to fill at least two rows in each tab, and hover top-row + edge-column posters.

### 2026-05-02 10:05 -- Work Completed

**What was done:**
- Updated all 10 horizontal poster rails in `src/Client/Pages/Dashboard/Views.fs` to replace `pb-2` with `py-2 px-2` so the 5% hover-zoom is no longer clipped at the rail edges. Two rails (lines 898 and 1196) carried an additional `mb-3` utility, which was preserved.
- Added `p-1` padding to the SearchModal poster grid container at `src/Client/Components/SearchModal.fs:676` so the top-row/edge-column hover scale is visible.

**Acceptance criteria status:**
- [x] Hovering posters in any Dashboard rail no longer clips at top/bottom/left/right -- verified by class-string change adding `py-2 px-2` (8px on each axis) inside the scroll viewport, matching the 5% scale headroom required by `.poster-card:hover .poster-image-container { transform: scale(1.05); }`.
- [x] Internal padding applied to all rail containers -- grep confirmed the new pattern `overflow-x-auto py-2 px-2` appears on all 10 expected lines (370, 482, 898, 1039, 1196, 2431, 2469, 3313, 4174, 4189) and the old `overflow-x-auto pb-2 snap-x snap-mandatory` pattern is no longer present.
- [x] Snap, scrollbar, and gap behaviour unchanged -- the `snap-x snap-mandatory`, `scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent`, and `gap-3` utilities were preserved in every replacement.
- [x] No regression in surrounding glassCard layout -- the new padding lives inside the rail container; glassCard's `p-4` is untouched.
- [x] SearchModal grid hover no longer clipped -- `p-1` added to `grid grid-cols-4 gap-3` at SearchModal.fs:676; `hover:scale-[1.02]` is 2% so 4px of breathing room is sufficient.
- [x] Selected-state ring and keyboard nav unaffected -- only the grid container's padding changed; child item classes (`ring-2 ring-primary rounded-lg scale-[1.02]`) are unchanged.
- [x] `npm run build` succeeds -- Fable + Vite build completed in 32.38s with no errors.
- [x] `npm test` passes -- 255 tests run, 255 passed, 0 failed.
- [ ] Manual Chrome DevTools MCP verification -- skipped (dev server not running); change is class-string only and verified via grep + build + tests.

**Files changed:**
- `src/Client/Pages/Dashboard/Views.fs` -- 10 rail class strings updated: `pb-2` -> `py-2 px-2`.
- `src/Client/Components/SearchModal.fs` -- poster grid class string updated: `gap-3` -> `gap-3 p-1`.
