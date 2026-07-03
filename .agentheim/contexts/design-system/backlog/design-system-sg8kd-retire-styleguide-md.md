---
id: design-system-sg8kd
title: Retire styleguide.md — the in-app StyleGuide page is the authoritative artifact (supersede ADR-0009)
status: backlog
type: decision
context: design-system
created: 2026-07-03
completed:
depends_on: []
blocks: []
tags: [design-system, styleguide, gate, adr-supersede, cleanup]
related_adrs: [0009]
related_research: []
prior_art: [design-system-001, design-system-002]
---

## Why

The user (2026-07-03) stated that `.agentheim/contexts/design-system/styleguide.md` **should not exist and has no authoritative quality**. The authoritative design-system style guide for this project is the **live in-app StyleGuide page** (`src/Client/Pages/StyleGuide/`, rendered Feliz specimens backed by `DesignSystem.fs` + `index.css`) — "the style guide in the front end."

This directly reverses **ADR-0009** ("styleguide.md is the canonical design-system artifact; CLAUDE.md points at it"), which was accepted 2026-05-27 and produced by `design-system-001`. The reversal is a deliberate decision, so it needs a superseding ADR and a coordinated rewire of every place that currently treats styleguide.md as authoritative — not a silent file delete.

## Open decisions (to resolve during REFINE, with the user)

1. **The frontend "styleguide gate."** ADR-0009 / the agentheim workflow define a gate where every frontend task `depends_on` a styleguide task, gating on styleguide.md as a self-contained reviewable artifact. If styleguide.md is retired, what does the gate reference instead? The live StyleGuide page is rendered code, not a standalone review doc — decide whether the gate (a) points at the in-app page as-is, (b) is redefined around `DesignSystem.fs` + `index.css` + the page, or (c) is dropped. This is the crux of the decision and must be settled before executing.
2. **Glassmorphism spec + backdrop-filter gotcha.** ADR-0009 deliberately reproduced these verbatim in styleguide.md AND kept them in CLAUDE.md. If styleguide.md goes, CLAUDE.md § Conventions / § Gotchas already hold them (they were never removed) — confirm CLAUDE.md remains the prose home and no content is lost.
3. **Scope of deletion vs. archive.** Delete styleguide.md outright, or move it to `.workflow.archived/` as a historical record? (It captures real intent/rationale accumulated across r7k2m/h3q8n/bky6v/fq3vp.)

## What (proposed — pending the decisions above)

- Write a new ADR superseding ADR-0009: the **in-app StyleGuide page is the authoritative design-system artifact**; styleguide.md is retired. Set ADR-0009 `superseded_by`.
- Remove (or archive) `.agentheim/contexts/design-system/styleguide.md`.
- Repoint **CLAUDE.md line 50** (§ Conventions "Design system canonical artifact") at the in-app StyleGuide page + `DesignSystem.fs`/`index.css`, dropping the styleguide.md pointer and the ADR-0009 citation.
- Repoint the **`design-check` skill** (`.claude/skills/design-check/references/design-rules.md` "Source of Truth", originally set by `design-system-002`) at the in-app StyleGuide page.
- Update the **design-system README** and the open **`grtw7`** backlog task to stop citing styleguide.md as the intent source.
- Redefine (or drop) the **frontend task gate** per decision 1.
- Leave done-task Notes that mention styleguide.md as-is (historical record); no need to rewrite closed tasks.

## Acceptance criteria (draft — finalize at REFINE)

- [ ] A superseding ADR exists; ADR-0009 marked `superseded_by`.
- [ ] styleguide.md removed or archived per decision 3.
- [ ] No live pointer (CLAUDE.md, design-check design-rules.md, design-system README, grtw7) still names styleguide.md as the authoritative/source-of-truth artifact.
- [ ] The frontend gate is redefined or explicitly dropped per decision 1, documented in the new ADR.
- [ ] `grep -rn "styleguide.md"` over live docs/skills (excluding done-task Notes and protocol history) returns no authoritative reference.
- [ ] Glassmorphism spec + backdrop-filter gotcha confirmed still present in CLAUDE.md (nothing lost).

## Notes

- Blast radius mapped 2026-07-03: `styleguide.md` is referenced in ~23 files — the authoritative/live ones to rewire are CLAUDE.md, `.claude/skills/design-check/references/design-rules.md`, the design-system README, and `grtw7`; ADR-0009 to supersede. The rest are done-task Notes + protocol history (historical, leave as-is).
- Filed after the user corrected the premise mid-session (right after design-system-fq3vp shipped, which had updated styleguide.md in lockstep per the then-current ADR-0009). This task is `type: decision` and under-specified until decision 1 (the gate) is answered — REFINE with the user before `work` picks it up.
