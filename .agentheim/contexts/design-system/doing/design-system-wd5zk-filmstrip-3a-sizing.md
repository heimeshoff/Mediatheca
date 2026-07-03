---
id: design-system-wd5zk
title: Movies filmstrip — full-width 3a proportions (flex-1 posters, ~196px tall)
status: doing
type: bug
context: design-system
created: 2026-07-03
completed:
depends_on: [design-system-001]
blocks: []
tags: [filmstrip, velvet-lobby, component-pattern, 3a]
related_adrs: [0009]
related_research: []
prior_art: [design-system-h3q8n]
---

## Why

The shipped `DesignSystem.filmstripRow` renders poster tiles at `w-16` (64px) — a thumbnail row inside an oversized black well. In direction **3a (Desktop Dashboard)** the filmstrip is the *cinematic centerpiece* of the Movies section: five posters at `flex: 1` each, **196px tall**, filling the strip's full width edge to edge. The user flagged the shipped strip as too small; it should fit 3a's size.

## What

Rework `filmstripRow` (`DesignSystem.fs:392`) and `.filmstrip` (`index.css:296`) to 3a's proportions:

- **Posters:** `flex: 1` (equal columns filling the strip), height **196px** (3a is fixed-height; posters are landscape-cropped into it — keep `object-cover`), radius `--radius-poster` (2px), **gap 10px**, row padding `0 16px` inside the well.
- **Well:** black `#000`, radius 8px, `padding: 7px 0`, sprocket strips 8px tall top and bottom with 7px clearance to the posters, `--shadow-filmstrip` — the shipped sprocket gradient already matches the doc.
- **Captions:** a row *below* the strip mirroring the poster columns (`flex: 1` each, same 10px gap + 16px side padding, `padding-top: 10px`): title 12px weight 600 line-height 1.35, meta 10.5px ink-muted on a second line ("2h 46m · rec. by Sam"). Use the grid-caption type tier from design-system-snpnv if it has landed; otherwise inline the values and reconcile later.
- The strip shows ~5 posters at dashboard width; the count is the caller's choice — the pattern must simply divide available width equally.

## Acceptance criteria

- [ ] At ~1200px content width the rendered filmstrip visually matches 3a: five full-width columns, 196px-tall posters, captions aligned under their posters.
- [ ] `filmstripRow`'s public signature keeps taking `FilmstripItem list`; existing call sites just get bigger, correctly-proportioned output.
- [ ] `styleguide.md` § 4 "Movies filmstrip" documents the sizing (flex-1 columns, 196px height, 10px gap, 16px side padding).
- [ ] StyleGuide specimen updated and renders the new proportions.
- [ ] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3a DESKTOP DASHBOARD`, "movies filmstrip" block — all literal values quoted above.
- Sibling task design-system-snpnv mints the caption type tier; no hard dependency, whichever lands second reconciles.
