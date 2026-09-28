---
id: design-system-v3qh6
title: Games, Books and Series poster hovers get the same smooth mechanism the Movies filmstrip got in design-system-k4tw8 — one shared transform-scale rule on a permanent compositor layer
status: done
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

## Outcome

Brought Games, Books, Series poster hovers, and the Next-episode hero card onto the exact same shared CSS transform-scale mechanism the Movies filmstrip got in design-system-k4tw8, as the builder requested.

**1. One shared `will-change`/`transition` declaration** (`src/Client/index.css`, ~l.551-596): `.poster-image-container` (the Games/Books/Series poster-card mechanism, `DesignSystem.posterImageContainer`) and the renamed `.poster-hover-scale` class (was `.filmstrip-poster`) now declare `transition: transform 0.3s ease` and `will-change: transform` from one shared selector list — `.poster-image-container, .poster-hover-scale { ... }` — instead of `.poster-image-container` carrying its own copy of `transition` and `.filmstrip-poster` carrying a separate copy of both. The block comment above it was rewritten: it no longer says `.poster-image-container` is deliberately excluded from `will-change`; it now explains design-system-v3qh6 overrides k4tw8's GPU-memory caution in favour of hover-parity across every poster surface, and names both consumers (the filmstrip tile and the Next-episode hero card) that can't reuse `.poster-image-container` wholesale because it also carries `aspect-ratio: 2/3` and its own background/border-radius, which would fight their own sizing. The hover-trigger selector list also picked up the rename: `.poster-card:hover .poster-image-container, .group:hover .poster-hover-scale { transform: scale(1.05); }`.

**2. Renamed `.filmstrip-poster` → `.poster-hover-scale`** (`src/Client/index.css`, `src/Client/DesignSystem.fs`'s `filmstripRow`): the class is no longer filmstrip-specific now that the Next-episode hero card shares it too, per the task's "neutral shared class name" option. `filmstripRow`'s `posterBoxClass` and its explanatory comment were updated to the new name; the k4tw8 regression test was updated in step (see below).

**3. Moved `nextEpisodeHeroCard` onto the shared rule** (`src/Client/DesignSystem.fs` ~l.889-901): dropped Tailwind's `transition-transform duration-300 group-hover:scale-105` (which Tailwind 4 compiles to the standalone `scale` CSS property, a separate code path from `transform`) from the card's root `prop.className`, replaced with `poster-hover-scale`. The card's caller (`Dashboard/Views.fs`'s `seriesNextEpisodeCard`) already wraps it in an anchor carrying Tailwind's `group` marker, matching the filmstrip's `.group:hover .poster-hover-scale` trigger — no caller-side change was needed.

**`heroCard`** (the styleguide-only, non-repeated hero specimen) was checked and does not use any Tailwind scale utilities, so it needed no change.

**Testing**: `src/Client/DesignSystem.test.fs` was extended (registered already in `Client.fsproj`) with a second `testCase` that renders `nextEpisodeHeroCard` via `react-dom/server`'s `renderToStaticMarkup` and asserts its class list carries `poster-hover-scale` and neither `group-hover:scale` nor `transition-transform` — directly exercising acceptance criterion 2. Confirmed it fails for the right reason (missing `poster-hover-scale` in the emitted HTML) when `nextEpisodeHeroCard`'s className is reverted to the pre-fix string, then passes again after the fix. The pre-existing k4tw8 filmstrip test case was updated in place to assert `poster-hover-scale` instead of the retired `filmstrip-poster` name.

`grep -rn "group-hover:scale" src/Client --include=*.fs` now only matches explanatory comments and the test's own string literals (`"group-hover:scale"` as the assertion target) — no live `prop.className` usage remains on any poster or hero-card surface.

**Live DOM check** (temp fixture, never the live DB/dev stack): built the client (`npm run build`), then loaded a standalone static HTML page (loopback `file://`, no server) that links the real compiled `deploy/public/assets/index-*.css` and reproduces the *exact* real class strings/DOM shape three surfaces render in production — `.poster-card > .poster-image-container.poster-shadow` (the identical mechanism `Dashboard/Views.fs` uses for both the "Games in focus" and "Currently Reading" rails — same component, same classes, differing only in content, so one mechanism-level check covers both named surfaces) for a Games poster and a Books poster, and `.group > .poster-hover-scale` (`nextEpisodeHeroCard`'s real class list) for the Next-episode hero card. Drove it with a throwaway Playwright (`chromium.launch()`, launched directly, not through `playwright.config.ts`'s webServer) script that hovers each element and reads `getComputedStyle`. Deleted the throwaway script and scratchpad fixture files after. Measured, for all three surfaces:
- At rest: `willChange === "transform"`, `transform === "none"`.
- Hovered, after the 300ms transition settles: `transform === "matrix(1.05, 0, 0, 1.05, 0, 0)"`.
- After mouse-leave: `transform === "none"` again.

All three surfaces matched the acceptance criterion's expected values exactly.

`npm run build` passes (typecheck + Vite production build, confirmed both before and after the DOM-check round-trip). `npm run test:client` (`vitest run`) is green: 22 files, 151 tests (150 prior + 1 new).

Acceptance criterion "hovering Games in focus, Currently Reading and TV Series Next Up posters on the All tab feels the same as hovering a Movies to Watch filmstrip poster" is marked `[human-eye]` in the task and is left pending for the builder's own browser check, per this task's environment rules.

Key files:
- `C:\src\heimeshoff\containers\mediatheca\.worktrees\design-system-v3qh6\src\Client\index.css` (~l.551-596, shared `.poster-image-container, .poster-hover-scale` declaration and hover-trigger selector list)
- `C:\src\heimeshoff\containers\mediatheca\.worktrees\design-system-v3qh6\src\Client\DesignSystem.fs` (`filmstripRow`'s `posterBoxClass` ~l.671-680, `nextEpisodeHeroCard`'s root `prop.className` ~l.889-901)
- `C:\src\heimeshoff\containers\mediatheca\.worktrees\design-system-v3qh6\src\Client\DesignSystem.test.fs` (updated + extended)
