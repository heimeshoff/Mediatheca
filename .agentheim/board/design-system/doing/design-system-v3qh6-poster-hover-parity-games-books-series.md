---
id: design-system-v3qh6
title: Games, Books and Series poster hovers get the same smooth mechanism the Movies filmstrip got in design-system-k4tw8 — one shared transform-scale rule on a permanent compositor layer
status: doing
type: bug
context: design-system
created: 2026-09-28
completed:
depends_on: [design-system-001]
blocks: []
tags: [poster, hover, motion, dashboard]
related_adrs: []
related_research: []
prior_art: [design-system-k4tw8, design-system-wd5zk]
---

## Why
After design-system-k4tw8 the Movies to Watch filmstrip hover looks right to the
builder (confirmed 2026-09-28). The builder wants Games, Books and Series posters to
behave exactly the same. Right now they don't share the mechanism:

- **Games in focus / Currently Reading (All tab), the Books rails, the Series tab
  Next Up row, PosterCard grids**: scale via `.poster-card:hover
  .poster-image-container { transform: scale(1.05) }`, but they have **no**
  `will-change: transform`. The layer is promoted and demoted around every
  transition, which is the raster-snap step k4tw8 removed from the filmstrip.
- **TV Series Next Up on the All tab** (`seriesNextEpisodeCard` →
  `DesignSystem.nextEpisodeHeroCard`, `src/Client/DesignSystem.fs` ~l.892): still
  uses Tailwind `transition-transform duration-300 group-hover:scale-105`, which
  compiles to the separate CSS `scale` property. That is the exact path k4tw8 removed
  from the filmstrip.

The k4tw8 note "don't add `will-change` to `.poster-image-container`" is overridden
by the builder's decision here. Parity matters more than the GPU-memory worry,
especially now that fryq7 removed the big Movies/Series/Games list-page grids.

## What
Make every poster-hover surface use the one mechanism the filmstrip uses now:

1. **Permanent compositor layer on `.poster-image-container`.** In
   `src/Client/index.css`, declare `will-change: transform` for
   `.poster-image-container` and `.filmstrip-poster` together, in one shared
   declaration next to the existing `transition: transform 0.3s ease`. Don't give each
   selector its own copy. Update the k4tw8 CSS comment so it no longer says
   `.poster-image-container` is deliberately excluded.
2. **Move the Next-episode hero card onto the shared rule.** Drop
   `transition-transform duration-300 group-hover:scale-105` from
   `nextEpisodeHeroCard`'s root class. Give it a hover-scale target class, either
   reusing `filmstrip-poster` or a new, neutrally named shared class like
   `poster-hover-scale`, and add it to the shared `transform: scale(1.05)` selector
   list (`.poster-card:hover .poster-image-container, .group:hover
   .filmstrip-poster, …`). If a neutral class name reads better, the worker may rename
   `.filmstrip-poster` to it, as long as the k4tw8 test is updated in step.
3. Keep the StyleGuide specimens in step. They render the same DesignSystem pieces,
   so they should follow automatically. Check the `heroCard` specimen too, if it uses
   the same Tailwind scale utilities.

## Acceptance criteria
- [ ] `index.css` declares `will-change: transform` so that it applies to both `.poster-image-container` and the filmstrip poster, from one declaration block, not two copies.
- [ ] `nextEpisodeHeroCard`'s root class no longer contains `group-hover:scale-` or `transition-transform`. Its hover scale comes from the shared `transform: scale(1.05)` selector list in `index.css`, and a Vitest render test (like `src/Client/DesignSystem.test.fs`) asserts the class list.
- [ ] Nothing under `src/Client` still uses a `group-hover:scale-*` utility on a poster or hero-card surface: `grep -rn "group-hover:scale" src/Client --include=*.fs` returns no poster or hero hits.
- [ ] Live DOM check on a fixture-served dashboard or the StyleGuide page (temp DATA_DIR on 127.0.0.1:5100-ish, never the live DB or the 5000/5173 dev stack): a Games poster, a Books poster and a Next-episode hero card each have `getComputedStyle(el).willChange === "transform"` at rest, compute `transform` to `matrix(1.05, 0, 0, 1.05, 0, 0)` once the hover transition settles, and return to `none` after mouse-leave.
- [ ] `npm run build` passes and `npm run test:client` stays green (the k4tw8 filmstrip test still passes, updated if the class was renamed).
- [ ] On the builder's own browser, hovering Games in focus, Currently Reading and TV Series Next Up posters on the All tab feels the same as hovering a Movies to Watch filmstrip poster. [human-eye]

## Notes
- Reference implementation: design-system-k4tw8's Outcome, plus the
  `.filmstrip-poster` block in `src/Client/index.css` (~l.560-592).
- Hover-shine for hero cards already goes through `.group:hover .poster-shine`, so no
  change is needed there.
- Keep `.poster-image-container`'s own sizing (`aspect-ratio: 2/3`) off the hero card
  and the filmstrip. Share only the transform mechanism, as k4tw8 did.
- Workers never touch the live DB. DOM checks go against a fixture DATA_DIR only.
