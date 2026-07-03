---
id: design-system-fq3vp
title: Compact on-poster "✦ Focus" pill (3c grid badge variant)
status: doing
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

Add a new typed `DesignSystem.fs` composition **`inFocusPill`** + a supporting **`.in-focus-pill`** `index.css` class for the compact on-poster pill per the 3c spec above.

**Resolved at refinement (2026-07-03) — solid, not swept.** The pill is a **static solid gold fill** (`oklch(0.84 0.11 85)`), it does **not** carry `DesignSystem.goldLeafSweep` / `.gold-sweep`. This resolves the sweep-vs-solid conflict the capture flagged (the doc's "alive family" framing vs. the 3c literal solid-fill spec), in favor of the literal 3c spec, for three reasons:

1. **Motion economy / no competing sweeps.** The pill is always composed *on top of* a poster wrapped in the animated `inFocusFrame` (its sweeping gold border is directly behind the pill). A second sweep on an ~8.5px pill competes with the frame's sweep and reads as jitter rather than life. Solid pill + animated frame = exactly one motion focal point per poster.
2. **Perceptibility.** A moving gradient across a pill only a few characters wide is barely legible as motion.
3. **System coherence.** The reduced-motion fallback for the existing sweep carriers already *is* a static solid gold fill (`.gold-sweep` / `.in-focus-frame` freeze at a fixed gradient position under `prefers-reduced-motion`, design-system-bky6v) — so a deliberately-solid gold pill is already an accepted "In focus" signal in the system, not a new visual language.

The animated members of the In-focus family (badge `.gold-sweep`, poster `.in-focus-frame`) are unchanged; this pill is a third, deliberately-static member.

## Acceptance criteria

- [ ] `DesignSystem.inFocusPill` (+ `.in-focus-pill` in `index.css`) renders the compact on-poster pill per the 3c literal spec: 8.5px, weight 700, 0.18em tracking, uppercase, "✦ Focus" label, dark ink (`oklch(0.16 0.024 82)`) on **solid** gold `oklch(0.84 0.11 85)`, positioned top-left over a poster/card.
- [ ] The pill is a **solid fill** — it does **not** apply `.gold-sweep` / `DesignSystem.goldLeafSweep`. No new keyframe animation is introduced for it.
- [ ] `styleguide.md` § 4 "Motion discipline" documents the solid-not-swept decision and its rationale (motion economy against the co-occurring animated `inFocusFrame`; reduced-motion coherence).
- [ ] StyleGuide specimen added showing `inFocusPill` composed with `DesignSystem.inFocusFrame` on a poster (the intended poster-grid pairing), so the "one moving element per poster" intent is visible in situ.
- [ ] `styleguide.md` § 4 "Poster grid" updated to point at the shipped `inFocusPill` instead of "not yet built" — and its guidance that poster chrome should reuse `statusBadge InFocus` is updated to name `inFocusPill` as the poster-grid In-focus badge.
- [ ] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3c` poster grid, badge block. (The literal token values above are extracted from it — a worker need not re-parse the 912KB archive.)
- Naming: `inFocusPill` / `.in-focus-pill` chosen at refinement to read as the poster-grid sibling of `inFocusFrame` — the two compose as the poster-grid In-focus pair.
- This is a genuinely new component, not a variant flag on `statusBadge` — keep it a separate composition so the full-size `statusBadge InFocus` (list rows, hero, detail) and the compact grid pill can diverge freely.
- Poster-grid *page* chrome itself is still unbuilt (a separate, pre-existing backlog gap noted in `styleguide.md`'s "Not yet implemented" table). This task ships the pill component + specimen; wiring it into a real list page is downstream BC work.
