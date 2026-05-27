---
id: design-system-001
title: Formalize the existing styleguide as a reviewable document
status: done
type: feature
context: design-system
created: 2026-05-12
completed: 2026-05-27
commit:
depends_on: []
blocks: []
tags: [foundation, gate]
---

## Why

The design system already exists in the running app — glassmorphism rules in `CLAUDE.md`, tokens in `index.css`, a live `src/Client/Pages/StyleGuide` page — but there is no single canonical reviewable artifact. The agentheim styleguide gate requires that every frontend task in any BC `depends_on` a concrete styleguide task; without a formalized document, the gate is fuzzy.

This task produces that artifact: a single `styleguide.md` that consolidates the existing rules and component patterns and serves as the gate's source of truth for future frontend captures.

## What

Produce `.agentheim/contexts/design-system/styleguide.md` covering:

1. **Tokens** — colors, spacing scale, radii, opacities (read from `index.css` and the `dim` theme block).
2. **Typography** — Oswald / Inter pairing, sizing scale, semantic mapping (display / heading / body / mono).
3. **Glassmorphism rules** — the full overlay spec from `CLAUDE.md` § "Conventions": opacity range, blur, saturation, border, highlight. Include the `backdrop-filter` nesting gotcha from § "Gotchas".
4. **Component patterns** — one entry per recurring pattern visible in `src/Client/Pages/StyleGuide` and across pages: glass card, rating dropdown, poster card + rail, modal, action button, friend chip, etc. Each entry: name, intended use, anatomy, code reference (file + line).
5. **Theme** — `dim` selection mechanism, how to add a token, when *not* to introduce a new one.
6. **Review process** — how this document gets updated and how frontend tasks depend on it.

The document references existing code; it does not duplicate it. Where a pattern lives in `src/Client/Pages/StyleGuide` or `src/Client/Components/`, link to file+line rather than copying.

## Acceptance criteria

- [ ] `.agentheim/contexts/design-system/styleguide.md` exists with the six sections above.
- [ ] Every recurring component pattern visible on the live StyleGuide page is documented with a file+line code reference.
- [ ] The glassmorphism spec and the backdrop-filter gotcha are reproduced verbatim from CLAUDE.md (or CLAUDE.md is updated to point at the styleguide as the source of truth — pick one).
- [ ] The design-system README's "existing assets" section is updated to point at the new `styleguide.md` as the canonical artifact.
- [ ] **Human review gate:** the user has read and signed off on `styleguide.md` before any frontend task in any BC is promoted to `todo/`. Sign-off recorded as a one-line note in the protocol entry that closes this task.

## Notes

- The `design-check` skill already encodes parts of these rules — cross-check that its checks align with the formalized doc. Any drift between them is a finding for this task.
- Do not attempt to *change* the design system in this task. Just formalize what exists. Refactors / additions get their own backlog items.
- After this task is done, the `model` skill captures any new frontend task with `depends_on: [design-system-001]` automatically (per the gate rule documented in each frontend-bearing BC's README).

## Outcome

Produced `.agentheim/contexts/design-system/styleguide.md` — the canonical, reviewable design-system artifact — with all six required sections: tokens (from `index.css`), typography, glassmorphism rules (spec + backdrop-filter gotcha reproduced verbatim from `CLAUDE.md`), component patterns (every recurring pattern on the live StyleGuide page, each with file+line references into `DesignSystem.fs` / `Components/` / `StyleGuide/Views.fs`), theme, and the review/gate process.

Criterion-3 source-of-truth question resolved by **reproducing verbatim AND pointing**: the glassmorphism rule and gotcha stay in `CLAUDE.md` (critical onboarding) with a new pointer line to the styleguide as canonical. Recorded in ADR 0009 (`0009-styleguide-canonical-artifact.md`).

design-check cross-check (criterion note) done: rules align across all 9 categories. Two drift findings raised as backlog items — F-1 `design-system-002` (point design-check's "Source of Truth" at styleguide.md) and F-2 `design-system-003` (ActionMenu has no live-page specimen).

Updated the design-system README "Existing assets" section to make `styleguide.md` the canonical artifact.

**Criterion 5 (human sign-off) is pending** — only the user can sign off. The document is ready for review; the gate does not open for promoting frontend tasks to `todo/` until sign-off is recorded in the closing protocol entry.

Key files:
- `.agentheim/contexts/design-system/styleguide.md` (new)
- `.agentheim/knowledge/decisions/0009-styleguide-canonical-artifact.md` (new)
- `CLAUDE.md` (pointer added)
- `.agentheim/contexts/design-system/README.md` (existing-assets section)
- `.agentheim/contexts/design-system/backlog/design-system-002-*.md`, `design-system-003-*.md` (new)
