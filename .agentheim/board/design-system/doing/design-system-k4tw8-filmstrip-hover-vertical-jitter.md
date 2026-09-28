---
id: design-system-k4tw8
title: Movies filmstrip poster hover jitters vertically — on hover-in the poster nudges down before it zooms, on hover-out it shrinks back then nudges up; the Games/Books/Series poster cards scale cleanly and are the reference
status: doing
type: bug
context: design-system
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [filmstrip, hover, motion, dashboard]
related_adrs: []
related_research: []
prior_art: [design-system-wd5zk, design-system-hs4vm]
---

## Why
On the Dashboard's All tab, the "Movies to Watch" row (the sprocketed filmstrip well)
jumps when you hover a poster: it moves slightly **down** and only then zooms in; on
mouse-leave it shrinks back to size and then moves slightly **up**. It reads as janky.
The Games in focus, Currently Reading (books) and TV Series Next Up rows on the same
page scale smoothly, with no vertical movement — so this is specific to the filmstrip.

## What
Two changes to the filmstrip poster box in `DesignSystem.filmstripRow`
(`src/Client/DesignSystem.fs` ~l.669) and `src/Client/index.css`:

1. **Unify the hover mechanism** with the other poster cards. Apply the salvaged
   iteration-1 patch at `.agentheim/salvage/design-system-k4tw8-bounced.patch`
   (`git apply` — checked clean against main on 2026-09-28). It replaces Tailwind's
   `transition-transform duration-300 group-hover:scale-105` (which compiles to the
   separate CSS `scale` property) with a `.filmstrip-poster` class that shares the
   `.poster-card:hover .poster-image-container` rule's `transform: scale(1.05)` and
   `transition: transform 0.3s ease`.
2. **Keep the poster on its own compositor layer permanently.** Add
   `will-change: transform` to `.filmstrip-poster`.

**Working hypothesis (from the refinement on 2026-09-28):** the jitter is sub-pixel
raster snapping. Chrome puts the element on its own compositor layer only while the
transform transition runs. At a fractional page offset, the promoted layer rasterises
to a different pixel row than the in-flow paint. That gives about a 1px shift when the
zoom starts ("nudges down, then zooms") and the reverse when the layer is dropped at
the end ("shrinks, then nudges up"). Layout geometry never changes, which is why the
bounced worker's `getBoundingClientRect` sampling showed zero drift on both the fixed
and unfixed code. A permanently promoted layer removes the promote/demote step, so
there is nothing to snap.

Keep the StyleGuide filmstrip specimen in step (it renders `filmstripRow`, so it
follows automatically).

## Acceptance criteria
- [ ] `filmstripRow`'s poster box no longer carries `group-hover:scale-*` or `transition-transform`, and carries the `.filmstrip-poster` class; `index.css` scales it through the same selector list as `.poster-card:hover .poster-image-container` (`transform: scale(1.05)`), not a second, separately declared scale rule.
- [ ] `.filmstrip-poster` declares `will-change: transform` and `transition: transform 0.3s ease`. The live DOM check: a fixture-served All tab (temp DATA_DIR, never the live DB) or the StyleGuide specimen, with `getComputedStyle(posterBox).willChange === "transform"` at rest.
- [ ] Hovering a filmstrip poster still scales it to 1.05× over about 300ms (computed `transform` is `matrix(1.05, 0, 0, 1.05, 0, 0)` once the transition settles, back to `none` after mouse-leave), and the tile wrapper and caption rects don't move.
- [ ] `npm run build` passes and `npm run test:client` stays green.
- [ ] On the builder's own browser, hovering in and out of a Movies to Watch filmstrip poster on the All tab looks as smooth as the Games/Books/Series posters, with no down-then-zoom or shrink-then-up nudge. [human-eye]

## Notes
- Reported by the builder 2026-09-27 from the All dashboard; Games, Books and TV Series
  explicitly do not show it.
- Only the collapsed filmstrip is affected. The Movies tab's rails and the expanded
  All-tab Movies to Watch surface use `movieToWatchPosterCard` (the `.poster-card` path).
- **History, iteration 1 (bounced 2026-09-28):** the old criterion 1 asked for
  `getBoundingClientRect` sampling to show no more than 0.5px of vertical-centre drift.
  The worker ran it on the real All tab (fixture DB, real PNG posters, headless and
  headed Chromium, DPR 1/1.25/1.5, scrolling and non-scrolling layouts). Drift was zero
  on both the unfixed and the fixed code, so the method can't tell broken from fixed.
  That criterion was dropped: raster snapping is a paint-level effect geometry can't
  see. The only confirmation is the builder's eye-check, which is why that criterion
  exists.
- **If the builder still sees the nudge after this ships:** the next suspects are the
  `.poster-shine` hover overlay's diagonal gradient creating an illusion of movement,
  and the `Motion.flipKey` FLIP wrapper on the tile (that fix would belong in
  `Motion.fs`). Capture a follow-up with a screen recording; don't reopen this task.
- Don't add `will-change` to `.poster-image-container` in the same change. The
  reference cards don't jitter, and extra layers across every poster grid cost GPU
  memory.
