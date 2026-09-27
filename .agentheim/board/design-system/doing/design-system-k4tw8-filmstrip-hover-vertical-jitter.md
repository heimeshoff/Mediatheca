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
The filmstrip tile's hover zoom should look the same as the other dashboard poster
cards: a scale around the poster's own centre with no vertical translate, going in or
out.

Where the two differ in code (found during capture; the root cause is still a
hypothesis — confirm it before fixing):

- **Filmstrip** (`DesignSystem.filmstripRow`, `src/Client/DesignSystem.fs` ~l.661):
  the poster box uses Tailwind 4 utilities
  `overflow-hidden transition-transform duration-300 group-hover:scale-105`. Tailwind 4
  compiles `scale-105` to the separate CSS `scale` property, driven by `--tw-scale-*`
  vars, not to `transform`. The group is the tile wrapper (`flex-[1_0_130px] group`).
  The whole strip sits inside an `overflow-x-auto` scroller, which also makes
  `overflow-y` non-visible, and inside `.filmstrip { overflow: hidden }`.
- **Poster cards** (games/books/series/the expanded movie tiles): the plain CSS rule
  `.poster-card:hover .poster-image-container { transform: scale(1.05) }` with
  `transition: transform 0.3s ease` (`src/Client/index.css` ~l.551).

Likely suspects: (a) the `scale`-property vs `transform` path, e.g. compositing-layer
promotion or sub-pixel snapping at transition start/end, visible as a ~1px shift;
(b) the poster growing inside the nested overflow containers and nudging layout or
scroll position; (c) interaction with the `Motion.flipKey` FLIP wrapper on the tile.
The preferred fix is to make the filmstrip tile use the **same** hover mechanism as
`.poster-card` rather than adding a second one. Keep the StyleGuide filmstrip specimen
in step.

## Acceptance criteria
- [ ] Hovering a Movies to Watch filmstrip poster on the All tab changes only its scale. Sampled with Chrome DevTools (`getBoundingClientRect` on the poster box across the 300ms hover-in and hover-out transitions), the box's vertical centre stays within 0.5px of its resting value at every sample, and the tile wrapper's and caption's rects don't move at all.
- [ ] The filmstrip poster's hover zoom uses the same mechanism as the other dashboard poster cards: the `.poster-card` / `.poster-image-container` transform rule or a shared composition, not a separately declared Tailwind `group-hover:scale-*` path.
- [ ] The StyleGuide page's filmstrip specimen renders with the fixed hover behaviour.
- [ ] `npm run build` passes and `npm run test:client` stays green.
- [ ] Hovering in and out of a filmstrip poster looks as smooth as the Games/Books/Series posters, with no down-then-zoom or shrink-then-up movement. [human-eye]

## Notes
- Reported by the builder 2026-09-27 from the All dashboard; Games, Books and TV Series
  explicitly do not show it.
- The Movies tab's own rails use `movieToWatchPosterCard` (the `.poster-card` path), and
  so does the expanded All-tab Movies to Watch surface. Only the collapsed filmstrip is
  affected.
- If the cause turns out to be the FLIP wrapper (suspect c), the fix belongs in
  `Motion.fs` rather than the filmstrip. Note that in the Outcome.
