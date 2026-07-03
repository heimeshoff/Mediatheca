---
id: 0009
title: styleguide.md is the canonical design-system artifact; CLAUDE.md points at it
scope: design-system
status: accepted
date: 2026-05-27
supersedes: []
superseded_by: [0015]
related_tasks: [design-system-001]
related_research: []
---

# ADR 0009: styleguide.md is the canonical design-system artifact

## Context

The design system existed across three places: tokens/classes in `src/Client/index.css`, typed compositions in `src/Client/DesignSystem.fs`, the live `StyleGuide` page, and prose rules in `CLAUDE.md` (§ Conventions, § Gotchas) plus the `design-check` skill. There was no single reviewable artifact, so the agentheim "styleguide gate" (every frontend task `depends_on` a styleguide task) had nothing concrete to gate on.

`design-system-001` produced `.agentheim/contexts/design-system/styleguide.md`. Criterion 3 of that task required a binary choice: either reproduce the glassmorphism spec verbatim inside the styleguide, or strip it from `CLAUDE.md` and point `CLAUDE.md` at the styleguide.

## Decision

Make `styleguide.md` the canonical, reviewable design-system artifact. Specifically:

- Reproduce the glassmorphism overlay spec and the backdrop-filter nesting gotcha **verbatim** inside `styleguide.md`, so the document is self-contained for human review and the gate.
- Keep those same rules in `CLAUDE.md` (they are critical onboarding context every agent reads) and **add a pointer** from `CLAUDE.md` § Conventions to `styleguide.md` as the canonical artifact.
- Establish the split: `index.css` / `DesignSystem.fs` are authoritative for *values*; `styleguide.md` is authoritative for *intent* and the gate.

This is "reproduce verbatim AND point at it", not "strip from CLAUDE.md". The two short paragraphs in `CLAUDE.md` are cheap to keep and removing them would degrade fresh-agent onboarding (agents read `CLAUDE.md` before any BC doc).

## Consequences

- One reviewable document gates all frontend work; the gate is no longer fuzzy.
- The glassmorphism spec now lives in three prose places (`CLAUDE.md`, `styleguide.md`, `design-check`). Drift risk is mitigated by § 6/§ 7 of the styleguide, which mandate lockstep updates and treat divergence as a backlog finding. The `design-check` skill pointer alignment is queued as `design-system-002`.
- Future design-system changes route through the design-system backlog, never inline during feature work.

## Alternatives rejected

- **Strip glassmorphism rules from `CLAUDE.md`, point only.** Rejected: `CLAUDE.md` is the first file every agent reads; an indirection for the single most load-bearing visual rule hurts onboarding more than the small duplication costs.
- **Keep `CLAUDE.md` as canonical, make styleguide a pointer.** Rejected: the gate needs a self-contained reviewable artifact the user signs off on; a stub pointing back at `CLAUDE.md` is not reviewable as a unit.
