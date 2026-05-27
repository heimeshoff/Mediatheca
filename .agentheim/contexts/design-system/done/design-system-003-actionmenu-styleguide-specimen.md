---
id: design-system-003
title: Add ActionMenu specimen to the live StyleGuide page
status: done
type: feature
context: design-system
created: 2026-05-27
completed: 2026-05-27
commit:
depends_on: [design-system-001]
blocks: []
tags: [design-system, ui]
---

## Why

`Components/ActionMenu.fs` is a recurring glassmorphic overlay pattern (kebab menus, hero action menus) but has no specimen on the live StyleGuide page (`src/Client/Pages/StyleGuide/Views.fs`). The live page is meant to render every recurring pattern. Finding F-2 from the styleguide cross-check (design-system-001).

## What

Add an ActionMenu specimen to the Components section of the StyleGuide page demonstrating `ActionMenu.view`, `heroView`, and `heroViewSections` (`Components/ActionMenu.fs:60,147,208`). Then add a "ActionMenu" entry to `styleguide.md` § 4 with the new `Views.fs` line reference.

## Acceptance criteria

- [ ] StyleGuide Components section renders an ActionMenu specimen with code references.
- [ ] `styleguide.md` § 4 ActionMenu entry updated with the live-page reference (removing the "not yet demoed" note).
- [ ] `npm run build` compiles cleanly.

## Notes

This is a frontend task; it conforms to the styleguide. Depends on the styleguide gate being signed off.

## Outcome

Added an ActionMenu specimen to the Components section of the live StyleGuide page (`src/Client/Pages/StyleGuide/Views.fs:1191-1283`). It demonstrates all three exported view functions — `ActionMenu.view` (kebab menu), `heroView` (glass-trigger hero menu), and `heroViewSections` (labelled, divider-separated sections) — each interactive, with code references, the `ActionMenuItem`/`ActionMenuSection` record signatures, and a note that the dropdown renders as a sibling of the trigger to avoid the nested `backdrop-filter` gotcha (per the page's Glassmorphism section).

Updated `styleguide.md` § 4 ActionMenu entry: replaced the "not yet demoed (finding F-2)" note with the live-page reference `Views.fs:1191-1283` and a one-line description of the sibling-render rule.

`npm run build` compiles cleanly (Fable transforms `StyleGuide/Views.fs` and `ActionMenu.fs` without error). No ADR, README, or context-map change warranted — this is coverage/documentation closing finding F-2 from design-system-001.

Key files:
- `src/Client/Pages/StyleGuide/Views.fs` (specimen)
- `.agentheim/contexts/design-system/styleguide.md` (§ 4 entry)
