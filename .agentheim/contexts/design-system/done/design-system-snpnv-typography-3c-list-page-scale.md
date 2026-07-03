---
id: design-system-snpnv
title: Typography — adopt dir 3c's list-page type scale (grid captions, page header, filter pills)
status: done
type: feature
context: design-system
created: 2026-07-03
completed: 2026-07-03
depends_on: [design-system-001]
blocks: []
tags: [typography, velvet-lobby, styleguide, 3c]
related_adrs: [0009]
related_research: []
prior_art: [design-system-r7k2m, design-system-h3q8n]
---

## Why

The styleguide's § 2 typography (design-system-r7k2m) covers the editorial scale — serif display, italic section voice, sans body, mono data — but has no tiers for the *dense list-page* typography that direction **3c (Movies Grid)** in the captured design doc (`Mediatheca Directions.html`, marker `3c MOVIES GRID`) establishes. The user reviewed the design session output and wants § 2 to adapt 3c closer.

## What

Add the 3c list-page type tiers to the semantic scale — as *additions* alongside the existing helpers, not renames (the shipped four-tier ink ladder and `cardTitle` stay; `cardTitle` = Instrument Serif `text-lg` remains correct for velvet cards — 3a's secondary cards use serif 17px titles. Dense poster-grid captions are a *different, sans* voice):

1. **Grid caption pair** (poster-grid cards, filmstrip captions):
   - Grid card title: Instrument Sans, **12px, weight 600, line-height 1.3**, ink.
   - Grid meta: Instrument Sans, **10.5px**, ink-muted (`oklch(0.6 0.03 40)` in the doc — map to the nearest ink token), e.g. "2024 · rec. by Sam".
2. **List-page header pattern**: Instrument Serif page title at **34px** with a **baseline-aligned** Spline Sans Mono count line at **11px** ink-muted ("148 titles · 12 in focus") — `display:flex; align-items:baseline; gap:14px`.
3. **Filter pill typography**: **11.5px**; active pill = weight 600, dark ink (`oklch(0.16 0.028 20)`) on gold fill (`oklch(0.8 0.12 82)`); inactive = ink-secondary (`oklch(0.7 0.02 45)`) with `line`-toned 1px border, pill radius, `padding: 7px 15px`.

Deliverables follow the BC's lockstep convention: styleguide § 2 scale table rows + typed `DesignSystem.fs` helpers + live StyleGuide specimen.

## Acceptance criteria

- [x] `styleguide.md` § 2's semantic type scale documents the three new tiers (grid card title / grid meta, page-header title+count pairing, filter-pill active/inactive) with the literal 3c values above.
- [x] `DesignSystem.fs` exposes typed helpers for each tier (names at worker's discretion, consistent with existing `cardTitle`/`mutedText` naming), backed by tokens/CSS where needed.
- [x] The live StyleGuide page renders a "3c list-page chrome" typography specimen showing header + count, filter pills, and a grid caption pair.
- [x] Existing helpers (`cardTitle`, ink ladder) are unchanged — no app-wide re-skin in this task.
- [x] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3c MOVIES GRID` (912KB single-file archive; the section sits at byte offset ~820798, extractable by searching the marker comment). All needed literal values are quoted above so the worker shouldn't need to parse the file.
- Building the actual Movies grid *page* (filters, search, sort, grid) is Movies-BC work, not this task — this task only mints the typographic vocabulary.

## Outcome

Added the 3c list-page type tiers as pure additions alongside the existing editorial scale — no rename, no re-skin. All three literal-value ink/gold targets from the design doc mapped onto already-minted tokens (no new tokens needed): grid meta → `--color-ink-muted`, inactive pill → `--color-ink-secondary`, active pill dark ink → `--color-base-200` (an exact match), active pill fill → `--color-gold`.

Key files:
- `.agentheim/contexts/design-system/styleguide.md` — new § 2 "3c list-page type tiers" subsection (table + ink-token mapping) and a "Shipped (design-system-snpnv)" § 7 checklist entry.
- `src/Client/index.css` — `.filter-pill` / `.filter-pill-active` / `.filter-pill-inactive` (active/inactive states need CSS; the other two tiers are plain Tailwind arbitrary-value utility strings, no new CSS).
- `src/Client/DesignSystem.fs` — `gridCaptionTitle`, `gridCaptionMeta`, `gridCaptionPair`, `listPageHeaderTitle`, `listPageHeaderCount`, `listPageHeaderPattern`, `filterPill`.
- `src/Client/Pages/StyleGuide/Views.fs` — new "3c List-Page Chrome" specimen inside the Typography section (header+count, filter pills, grid caption pair).
- `.agentheim/contexts/design-system/README.md` — ubiquitous-language entry for the new tiers.

`npm run build` compiles clean (Fable + Vite + Tailwind, 172 modules transformed, no errors).
