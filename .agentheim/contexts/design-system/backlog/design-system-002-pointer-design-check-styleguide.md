---
id: design-system-002
title: Point design-check skill's "Source of Truth" at styleguide.md
status: backlog
type: chore
context: design-system
created: 2026-05-27
completed:
commit:
depends_on: [design-system-001]
blocks: []
tags: [design-system, docs]
---

## Why

`design-system-001` produced `.agentheim/contexts/design-system/styleguide.md` as the canonical, reviewable design-system artifact. The `design-check` skill's "Source of Truth" section (`.claude/skills/design-check/references/design-rules.md:3-7`) predates it and lists only `index.css` and `DesignSystem.fs`. Finding F-1 from the styleguide cross-check.

## What

Add `styleguide.md` to the skill's "Source of Truth" section as the canonical doc (the code remains authoritative for values; the styleguide for intent and the gate). Keep the rule categories themselves unchanged.

## Acceptance criteria

- [ ] `design-rules.md` "Source of Truth" references `.agentheim/contexts/design-system/styleguide.md`.
- [ ] No rule category semantics changed.

## Notes

Pure docs alignment; no UI code.
