---
id: design-system-wd5zk
title: Movies filmstrip — full-width 3a proportions (flex-1 posters, ~196px tall)
status: done
type: bug
context: design-system
created: 2026-07-03
completed: 2026-07-03
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

- [x] At ~1200px content width the rendered filmstrip visually matches 3a: five full-width columns, 196px-tall posters, captions aligned under their posters.
- [x] `filmstripRow`'s public signature keeps taking `FilmstripItem list`; existing call sites just get bigger, correctly-proportioned output.
- [x] `styleguide.md` § 4 "Movies filmstrip" documents the sizing (flex-1 columns, 196px height, 10px gap, 16px side padding).
- [x] StyleGuide specimen updated and renders the new proportions.
- [x] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3a DESKTOP DASHBOARD`, "movies filmstrip" block — all literal values quoted above.
- Sibling task design-system-snpnv mints the caption type tier; no hard dependency, whichever lands second reconciles.

## Outcome

Reworked `DesignSystem.filmstripRow` and its supporting CSS to match 3a's full-width cinematic proportions, replacing the shipped `w-16` thumbnail row.

Read the reference markup directly from `Mediatheca Directions.html` (the file is minified to a single line; extracted the "movies filmstrip" block via a small Node snippet rather than guessing at the well/sprocket/clearance box model) to confirm the exact structure: the well itself carries `padding: 7px 0` and contains two explicit **sibling sprocket bars** (not `::before`/`::after` pseudo-elements as previously shipped) with `margin-bottom`/`margin-top: 7px` providing clearance to the poster row, rather than baking sprocket height into the well's own padding.

- `src/Client/index.css` — `.filmstrip` well: padding `7px 0`, `border-radius: var(--radius-panel)`, `--shadow-filmstrip` (unchanged). Replaced the old `::before`/`::after` sprocket pseudo-elements (10px tall, baked into a 14px/0.5rem padding) with a standalone `.filmstrip-sprocket` class (8px tall, same `repeating-linear-gradient`), applied twice as real sibling elements with Tailwind `mb-[7px]`/`mt-[7px]` utilities for the 7px clearance.
- `src/Client/DesignSystem.fs` (`filmstripRow`, ~L382-442) — poster row is now `flex-1` columns (equal-width, filling the well edge to edge) at `h-[196px]`, `gap-2.5` (10px), `px-4` (16px side padding), `rounded-[var(--radius-poster)]`, `object-cover` images. Caption row mirrors the same `flex-1`/gap/padding grid with `pt-[10px]`; title uses inlined `text-[12px] font-semibold leading-[1.35]` and meta `text-[10.5px] text-ink-muted` (design-system-snpnv's grid-caption type tier had not landed in this workspace at authoring time, per the task's own fallback instruction — inlined literals, flagged in styleguide.md for later reconcile). Public signature (`FilmstripItem list -> ReactElement`) is unchanged; the only call site (StyleGuide specimen) needed no signature changes.
- `src/Client/Pages/StyleGuide/Views.fs` (~L1598-1607) — specimen container widened from `max-w-2xl` to `max-w-[1200px]` (matching the acceptance criterion's reference width) and grown from 3 to 5 sample items so the full-width proportions are visible.
- `.agentheim/contexts/design-system/styleguide.md` § 4 "Movies filmstrip" — rewritten to document the new sizing (flex-1 columns, 196px height, 10px gap/gutter, 16px side padding, 7px sprocket clearance) and note the pending grid-caption type-tier reconcile.

Verified with `npm run build` (clean Fable + Tailwind compile, no test harness exists for this BC per CLAUDE.md). No BC README changes — no ubiquitous language, aggregates, or invariants changed, only an existing component pattern's visual proportions.
