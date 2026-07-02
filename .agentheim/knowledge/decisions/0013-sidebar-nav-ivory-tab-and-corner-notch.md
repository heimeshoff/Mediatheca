---
id: 0013
title: Sidebar active-nav tab — ivory placard + concave corner-notch (override of brief's burgundy fill)
scope: design-system
status: accepted
date: 2026-07-03
supersedes: []
superseded_by: []
related_tasks: [design-system-t4b9k]
related_research: []
---

# ADR 0013: Sidebar active tab — ivory surface (not the brief's burgundy) and the concave corner-notch technique

## Context
`design-system-t4b9k` reworked the desktop sidebar (`Components/Sidebar.fs`) per the
"Mediatheca Directions" design doc, direction 3a. Two choices in that task are
non-obvious enough to pin:

1. **Palette override.** The captured design doc's dir 3a active nav item is a
   *burgundy* raised layer (`background: oklch(0.22 0.035 25)` + a gold
   `inset 2px 0 0` left bar). The user separately asked for a "white tab" while
   viewing a pasted reference image in the same doc. These two sources
   disagree. Refined with the user present (2026-07-02): the active tab lands
   as a warm **ivory** surface (`oklch(0.94 0.02 75)`, in the gold family) with
   dark-burgundy ink and a gold icon — not literal `#fff`, and not the doc's
   burgundy fill either. A deliberate in-palette compromise between the two
   sources, not a bug or a partial implementation of either.
2. **Curved-corner boundary technique.** The task called for the active tab's
   right edge (rail ↔ content boundary) to curve concavely around the tab's
   top and bottom corners, so the tab reads as joining the content panel
   rather than sitting behind a straight border.

## Decision
1. Mint three new tokens (`--color-nav-active-bg/-ink/-icon` in `index.css`)
   rather than reusing `--color-gold`/`--ring-active` — the existing `gold`
   token (`oklch(0.80 0.12 82)`) is tuned for use *on* the dark rail/surface
   tones and has too little contrast against an ivory background; the active
   tab needs its own darker, more saturated gold for the icon and its own ink
   flip for the label.
2. Implement the curved boundary with the standard **radial-gradient
   corner-mask** technique (two small `--nav-notch-size` squares as
   `::before`/`::after` on `.nav-item-active`, each a hard-edged
   `radial-gradient(circle at <corner>, transparent N, var(--color-base-300) N)`),
   rather than `border-radius` alone (which cannot produce a concave curve) or
   an SVG mask (unnecessary complexity for a two-corner cutout). The active
   `<a>` bleeds right via a negative margin equal to the nav container's
   gutter (`--space-gap-standard`) so its edge sits flush against the rail's
   inner boundary, and the notches paint page-tone (`--color-base-300`)
   squares with a transparent bite nearest the tab — since the notch sits
   inside the aside itself, the "reveal" naturally shows the rail's own
   background underneath, requiring no cross-component color coordination
   with `Layout.fs` or the main content area.
3. Retire the old `.nav-glow` left-edge glow-bar mechanism entirely (it only
   ever fired on `.active`, which no longer exists as a class name) rather
   than leaving it as dead CSS.

## Consequences
- The active-tab palette is a **deliberate, user-confirmed divergence** from
  the design doc's dir-3a spec — future re-reads of the design doc should not
  "fix" the sidebar back to the doc's burgundy fill; this ADR is the record of
  why it differs.
- The notch technique is entirely self-contained in `Sidebar.fs` + `index.css`
  — no `Layout.fs` changes were needed. If a future redesign changes the main
  content area's background to something other than `--color-base-300`, the
  notch fill color must be updated to match, or the seam will show.
- `--nav-notch-size` and the nav container's gutter (`--space-gap-standard`)
  are coupled: the negative margin assumes they're equal. If the nav's `px-3`
  padding changes, the notch bleed offset must change with it.
