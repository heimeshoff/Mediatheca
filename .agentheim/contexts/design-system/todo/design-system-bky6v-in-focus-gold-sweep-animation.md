---
id: design-system-bky6v
title: In-focus signifiers must animate — gold sweep on status badge and poster frame
status: todo
type: bug
context: design-system
created: 2026-07-03
completed:
depends_on: [design-system-001]
blocks: []
tags: [motion, gold-sweep, in-focus, status-badge, velvet-lobby]
related_adrs: [0009]
related_research: []
prior_art: [design-system-h3q8n]
---

## Why

In the design doc every "In focus" signifier is *alive*: the hero's "In focus" pill (3a), the grid's "✦ Focus" pill (3c), **and** the In-focus poster frame (3c) all carry the 3.2s gold sweep (`animation: mtq-sweep 3.2s linear infinite` on a `background-size: 200% 100%` gold gradient). In the shipped app the InFocus status badge does not visibly animate, and the frame was built static. The sweep is the design system's one reserved motion ornament — In-focus surfaces are exactly where it must fire.

## What

1. **Status badge (InFocus variant).** `DesignSystem.statusBadge InFocus` already composes `.status-badge status-badge-in-focus gold-sweep` (`DesignSystem.fs:252`) and `.gold-sweep` in `index.css` defines the gradient + `animation: gold-leaf-sweep var(--sweep)` — yet the badge renders static. Diagnose why (candidates: a later `background`/`background-color` shorthand on `.status-badge` or a DaisyUI badge rule overriding `background-image`; CSS order; `var(--sweep)` shorthand resolution) and fix so the sweep visibly runs.
2. **In-focus poster frame.** Replace the static `.in-focus-frame` ring (`index.css:325` — `box-shadow: 0 0 0 2px var(--color-gold)`) with the doc's animated gradient-border treatment (3c): a wrapper with `padding: 1.5px`, `border-radius: 8px`, the 3-stop gold gradient `linear-gradient(90deg, oklch(0.68 0.1 80), oklch(0.88 0.11 88), oklch(0.68 0.1 80))`, `background-size: 200% 100%`, `animation: … var(--sweep)`, and the gold glow `box-shadow: 0 14px 30px -12px oklch(0.6 0.11 82 / 0.45)`; inner child radius 7px. `DesignSystem.inFocusFrame` keeps its signature — callers unaffected.
3. **Grid badge variant.** 3c's on-poster pill reads "✦ Focus" (8.5px, weight 700, 0.18em tracking, uppercase, dark ink on solid gold `oklch(0.84 0.11 85)`, positioned top-left on the poster). Add this compact on-artwork variant (or document that `statusBadge InFocus` is also the on-poster badge — worker's call, but the styleguide must state which).

## Acceptance criteria

- [ ] The InFocus status badge visibly runs the gold-leaf sweep in the app and on the StyleGuide page (all other lifecycle badges stay static).
- [ ] `inFocusFrame` renders the animated sweeping gradient border + gold glow per 3c, not a static ring; existing call sites unchanged.
- [ ] The hero card's "In focus" pill sweeps (it reuses `statusBadge InFocus`).
- [ ] `styleguide.md` § 4 (status badges, poster grid) and Motion sections updated to match what shipped.
- [ ] `npm run build` clean.

## Notes

- Root-cause the badge first — if `.gold-sweep` is genuinely correct and the badge animates in isolation, the bug may be call-site-specific; record what was actually wrong.
- Consider honoring `prefers-reduced-motion` (freeze the sweep at a fixed gradient position). Not required by the doc — worker/user judgment; if added, document it in § 1.6 Animation.
