---
id: design-system-003
title: Add ActionMenu specimen to the live StyleGuide page
status: todo
type: feature
context: design-system
created: 2026-05-27
completed:
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
