---
id: design-system-bky6v
title: In-focus signifiers must animate — gold sweep on status badge and poster frame
status: done
type: bug
context: design-system
created: 2026-07-03
completed: 2026-07-03
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

## Outcome

**Root cause of the static badge (as predicted, candidate 1):** `.status-badge` (`index.css`, applied to every lifecycle badge) declared `background: transparent;`. The `background` shorthand resets *all* background sub-properties not explicitly given — including `background-image` and `background-size` — to their initial values. `.gold-sweep` (which sets `background-image`, `background-size: 200% 100%`, and the `animation`) and `.status-badge` are both single-class selectors with identical specificity (0-1-0-0); `.status-badge` is declared later in `index.css`, so its `background: transparent` always won the cascade and silently erased the gradient `.gold-sweep` had just set, leaving only the (never-conflicting) `animation` property active on an invisible `background-image: none`. Fix: `.status-badge` now uses `background-color: transparent;` instead of the `background` shorthand — this only touches the color channel, leaving `.gold-sweep`'s `background-image`/`background-size` intact. No call-site or class-order change was needed; `statusBadgeClass InFocus`'s composition (`"status-badge status-badge-in-focus " + goldLeafSweep`) was already correct. Verified in the compiled production CSS (`deploy/public/assets/index-*.css`): `.status-badge` now compiles to `background-color:#0000` (not the shorthand), and `.gold-sweep`/`.status-badge-in-focus`/`.in-focus-frame` all carry their intended `background-image`/`animation` declarations untouched.

`DesignSystem.inFocusFrame` was restructured from a single div with a static `box-shadow: 0 0 0 2px var(--color-gold)` ring into a two-layer composition: an outer `.in-focus-frame` wrapper (1.5px padding, 8px radius, the 3-stop gold gradient at `background-size: 200% 100%` driving `animation: gold-leaf-sweep var(--sweep)`, plus the `box-shadow: 0 14px 30px -12px oklch(0.6 0.11 82 / 0.45)` glow) and an inner `.in-focus-frame-inner` (7px radius, `overflow: hidden`) that clips the wrapped child so the animated gradient only shows at the border. The function's signature (`child: ReactElement -> ReactElement`) is unchanged — the only existing call site (StyleGuide § In-Focus Poster Frame) required no edits.

Added `@media (prefers-reduced-motion: reduce)` handling (worker judgment call, not mandated by the reviewed doc) that freezes both sweep carriers (`.gold-sweep`, `.in-focus-frame`) at a fixed `background-position` instead of disabling the gold-fill signal outright.

The compact on-poster "✦ Focus" pill (item 3 in "What", not present in the acceptance-criteria checklist) was deliberately **not** built — `styleguide.md` § 4 "Poster grid" documents `statusBadge InFocus` as the interim on-poster badge and a new backlog item (`design-system-fq3vp`) tracks the distinct compact variant as a future task, flagging that the doc's "always sweeps" framing and the 3c pill's literal "solid gold" spec conflict and need resolving before that task starts.

Key files: `src/Client/index.css` (`.status-badge`, `.in-focus-frame`/`.in-focus-frame-inner`, new reduced-motion block), `src/Client/DesignSystem.fs` (`inFocusFrame`), `.agentheim/contexts/design-system/styleguide.md` (§ 4 Status badges, § 4 Poster grid, § 4 Motion, shipped/sign-off tables), `.agentheim/contexts/design-system/README.md` (Motion primitive / In-focus poster frame ubiquitous-language entries). `npm run build` compiles clean; verified in the compiled CSS output that the fix takes effect (see above).
