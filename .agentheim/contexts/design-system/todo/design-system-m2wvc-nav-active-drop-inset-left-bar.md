---
id: design-system-m2wvc
title: Sidebar active nav item — drop the gold inset-left bar; burgundy fill + gold icon carry the active state alone (retracts that half of ADR-0014's dir-3a treatment)
status: todo
type: refactor
context: design-system
created: 2026-08-07
completed:
depends_on: [design-system-001]
blocks: [design-system-n8zqr]
tags: [sidebar, nav, active-state, tokens]
related_adrs: [0013, 0014, 0015]
related_research: []
prior_art: [design-system-t4b9k, design-system-grtw7, design-system-vk7rd]
---

## Why

The active item in the desktop rail carries a 2px gold bar down its left edge
(`--ring-active`, `inset 2px 0 0`) on top of its burgundy fill and gold icon. The builder
wants the bar gone: the fill and the gold icon already say "you are here", and the bar adds
a hard vertical rule to every menu item's left edge that the nav doesn't need.

This retracts one half of the dir-3a active-tab treatment ADR-0014 restored — the burgundy
fill and gold icon (the other half) stay exactly as they are. It is not a return to
ADR-0013's ivory placard; it is the dir-3a tab minus its edge marker.

Doing this before `design-system-n8zqr` (the collapsible rail) is deliberate: an
inset-left bar behaves badly in a 64px icons-only rail, and settling the active treatment
first means the collapse task inherits one treatment instead of reconciling two.

## What

The bar has exactly one consumer, so this is a removal, not a redefinition:

- `src/Client/index.css:539` — `.nav-item-active`'s `box-shadow: var(--ring-active);`
  is the only place the bar is drawn. Delete the declaration; `.nav-item-active` keeps its
  `background`, `font-size: 13px`, and `font-weight: 600`.
- `src/Client/index.css:74` — `--ring-active` becomes dead once that line is gone. Delete
  the token too rather than leaving an unreferenced value in the theme block.
- `src/Client/index.css:76-79` — the sidebar-nav comment block describes the treatment as
  "Burgundy fill + the gold `--ring-active` inset-left bar". Amend to the surviving
  treatment.
- `src/Client/DesignSystem.fs:158-161` — `navItemActive`'s doc comment says the same thing.
  Amend. The class string itself (`"nav-item-active"`) does not change, so no call site
  moves.
- `src/Client/Pages/StyleGuide/Views.fs:1392` — the Sidebar Nav section's `decision` prose
  narrates the gold inset-left bar. Amend so the live StyleGuide page (the canonical
  artifact per ADR-0015) matches what it renders.

Nothing else about the rail changes: wordmark, tagline, group split, the bottom group's
smaller scale, hover states, and the `sticky top-0 h-screen` viewport pinning
(design-system-vk7rd) are all untouched.

## Acceptance criteria

- [ ] `.nav-item-active` in `src/Client/index.css` declares no `box-shadow`.
- [ ] The `--ring-active` token is deleted: `grep -rn "ring-active" src/Client` returns
      zero hits (build output under `src/Desktop/bin/` does not count).
- [ ] Playwright, desktop viewport (≥1024px): the active sidebar nav link's computed
      `box-shadow` is `none`, and its computed `background-color` still resolves to the
      burgundy `--color-nav-active-fill` (the fill was not removed along with the bar).
- [ ] Playwright, same run: the active item's icon still computes to the gold
      `--color-gold` (the `nav-item-active-icon` treatment survives).
- [ ] `src/Client/Pages/StyleGuide/Views.fs`'s Sidebar Nav `decision` prose no longer
      describes a gold inset-left bar.
- [ ] ADR-0014 is amended in place with a dated note recording that the inset-left bar was
      retracted while the burgundy fill and gold icon stand (the ADR-0043 "amends in place"
      precedent — do not write a new ADR for a half-retraction).
- [ ] The BC README's "Layered sidebar nav" entry describes the current active treatment.
- [ ] `npm run build` exits 0.
- [ ] The active item still reads unmistakably as active at a glance in the running rail,
      without the bar. [human-eye]

## Notes

- Prior art chain, worth reading in order before touching anything: design-system-t4b9k
  introduced the layered rail (ivory placard, ADR-0013) → design-system-grtw7 reverted it
  to dir 3a's burgundy fill + gold bar (ADR-0014) → design-system-vk7rd fixed the rail's
  viewport pinning. This task edits only the bar that grtw7 restored.
- The user's phrasing was "when expanded, the menu items shouldn't have a border on the
  left side". Confirmed during capture as *unconditional* removal, not expanded-only — one
  active treatment in both rail states, so `design-system-n8zqr` has nothing to branch on.
- `--ring-active`'s name suggests generality but it never had a second consumer. If a
  future pattern wants an inset edge marker it can mint its own token; resurrect nothing.
