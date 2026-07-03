---
id: design-system-fq3vp
title: Compact on-poster "✦ Focus" pill (3c grid badge variant)
status: backlog
type: feature
context: design-system
created: 2026-07-03
completed:
depends_on: [design-system-001]
blocks: []
tags: [motion, gold-sweep, in-focus, status-badge, poster-grid, velvet-lobby]
related_adrs: [0009]
related_research: []
prior_art: [design-system-h3q8n, design-system-bky6v]
---

## Why

The reviewed design doc's 3c poster-grid direction shows a distinct **compact on-artwork badge** for "In focus" items — a small "✦ Focus" pill positioned top-left directly on the poster (8.5px, weight 700, 0.18em tracking, uppercase, dark ink on solid gold `oklch(0.84 0.11 85)`) — separate from the full-size `.status-badge` used in list rows, hero cards, and detail pages.

design-system-bky6v (which fixed the In-focus badge's gold-sweep animation and animated the `inFocusFrame` poster border) deliberately did **not** build this distinct compact variant — it wasn't in that task's acceptance criteria, and adding a genuinely new component wasn't in scope for a bug-fix task. `styleguide.md` § 4 "Poster grid" now documents the decision: until this task exists, `DesignSystem.statusBadge InFocus` is the only "In focus" badge in the system, and any poster-grid page chrome should reuse it (composed with `inFocusFrame`) rather than inventing an ad hoc badge.

## What

Add a new typed `DesignSystem.fs` composition (name TBD at refinement, e.g. `inFocusPill`) + supporting `index.css` class for the compact on-poster pill per the 3c spec above. Decide (at refinement, with the user if ambiguous) whether it also carries the gold-leaf sweep — the doc's "Why" framing for bky6v treats all three In-focus signifiers (hero pill, grid pill, poster frame) as "alive", but the 3c literal spec for this pill describes a **solid** gold fill, not a gradient — these two readings conflict and should be resolved before implementation, not guessed.

## Acceptance criteria (draft — refine before starting)

- [ ] Compact on-poster pill renders per the 3c literal spec (size, weight, tracking, case, colors, top-left position on a poster/card).
- [ ] Sweep-vs-solid question resolved and documented (with rationale) in `styleguide.md` § 4 Motion discipline.
- [ ] StyleGuide specimen added showing the pill composed with `DesignSystem.inFocusFrame`.
- [ ] `styleguide.md` § 4 "Poster grid" updated to point at the shipped pill instead of "not yet built".
- [ ] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3c` poster grid, badge block.
- This is intentionally deferred, not blocking — poster-grid page chrome itself is also still unbuilt (separate, pre-existing backlog gap noted in `styleguide.md`'s "Not yet implemented" table).
