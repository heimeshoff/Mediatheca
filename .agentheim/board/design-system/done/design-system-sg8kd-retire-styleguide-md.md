---
id: design-system-sg8kd
title: Retire styleguide.md — the in-app StyleGuide page is the authoritative artifact (supersede ADR-0009)
status: done
type: decision
context: design-system
created: 2026-07-03
completed: 2026-07-03
depends_on: [design-system-grtw7]
blocks: []
tags: [design-system, styleguide, gate, adr-supersede, cleanup]
related_adrs: [0009, 0015]
related_research: []
prior_art: [design-system-001, design-system-002]
---

## Why

The user (2026-07-03) stated that `.agentheim/contexts/design-system/styleguide.md` **should not exist and has no authoritative quality**. The authoritative design-system style guide for this project is the **live in-app StyleGuide page** (`src/Client/Pages/StyleGuide/`, rendered Feliz specimens backed by `DesignSystem.fs` + `index.css`) — "the style guide in the front end."

This directly reverses **ADR-0009** ("styleguide.md is the canonical design-system artifact; CLAUDE.md points at it"), accepted 2026-05-27 and produced by `design-system-001`. The reversal is a deliberate decision, so it needs a superseding ADR and a coordinated rewire of every place that currently treats styleguide.md as authoritative — not a silent file delete.

## Resolved decisions (2026-07-03 refinement)

1. **The frontend "styleguide gate" — redefine around the living system.** The gate keeps its force but re-anchors from a standalone prose doc to the shipped design system: **`DesignSystem.fs` (typed compositions) + `index.css` (tokens/values) + the in-app StyleGuide page (review surface)**. Frontend tasks in any BC **still carry a `depends_on` on a design-system task** — the anchor stays `design-system-001` (the foundational styleguide task, done and user-signed-off). What changes is the gate's *meaning*: "conform to the living design system, reviewed on the running StyleGuide page," not "gate on the self-contained styleguide.md doc." The new ADR records this redefinition explicitly. (Options rejected: point at the in-app page as a bare pointer with no living-system framing; drop the gate entirely.)

2. **Glassmorphism spec + backdrop-filter gotcha — confirmed no loss.** Verified by inspection during refine: **CLAUDE.md already holds both independently** — the full glassmorphism overlay spec at § Conventions (line 49) and the backdrop-filter nesting gotcha at § Gotchas (line 64). Neither depends on styleguide.md. CLAUDE.md remains the prose home; retiring styleguide.md loses nothing. (styleguide.md had only *reproduced* them verbatim per ADR-0009's "reproduce AND point" choice.)

3. **styleguide.md — archive, don't delete.** Move it to `.workflow.archived/styleguide.md` as a read-only historical record (it captured real intent/rationale accumulated across r7k2m/h3q8n/bky6v/fq3vp). Not a hard delete. See the Notes caveat on that folder's normal "do not write here" guardrail — this placement is a user-sanctioned exception.

## What

- Write a **new ADR superseding ADR-0009**: the **in-app StyleGuide page (backed by `DesignSystem.fs` + `index.css`) is the authoritative design-system artifact**; styleguide.md is retired; the frontend gate is redefined around the living system per decision 1. List `0009` in the new ADR's `supersedes`; set ADR-0009's frontmatter `superseded_by` to the new ADR (bidirectional). Next free ADR number is **0014** at time of refine (worker confirms it's still free — grtw7, in flight, also writes a superseding ADR for 0013 and may claim 0014 first).
- **Archive** `.agentheim/contexts/design-system/styleguide.md` → `.workflow.archived/styleguide.md` (git mv; keep as read-only historical record).
- Repoint **CLAUDE.md line 50** (§ Conventions "Design system canonical artifact"): drop the styleguide.md pointer and the ADR-0009 citation; point at the in-app StyleGuide page + `DesignSystem.fs`/`index.css` as the living system, citing the new ADR.
- Repoint the **`design-check` skill** `.claude/skills/design-check/references/design-rules.md` "Source of Truth" (line 5, set by `design-system-002`): drop the "Canonical styleguide" styleguide.md line; make the in-app StyleGuide page + `DesignSystem.fs` + `index.css` the source of truth.
- Update the **design-system README**: (a) the "Existing assets" section (currently calls styleguide.md "the canonical, reviewable artifact … Read it first") — repoint at the living system; (b) the "The styleguide gate (load-bearing)" section — redefine the gate per decision 1 and fix its stale task-path reference (it points at `todo/design-system-001-formalize-styleguide.md`; the task is in `done/`).
- Leave **done-task Notes and protocol history** that mention styleguide.md as-is (historical record). Do not rewrite closed tasks.
- **grtw7 is NOT a rewire target for this task.** It is in flight and its own criteria still write styleguide.md in lockstep; sg8kd runs *after* grtw7 (see `depends_on`). grtw7's own styleguide.md references become historical once it lands — leave them.

## Acceptance criteria

- [ ] A superseding ADR exists recording: in-app StyleGuide page authoritative, styleguide.md retired, gate redefined around the living system (decision 1). It lists `0009` in `supersedes`; ADR-0009's `superseded_by` points back at it (bidirectional).
- [ ] `styleguide.md` moved to `.workflow.archived/styleguide.md` (archived, not deleted); no copy remains under `contexts/design-system/`.
- [ ] CLAUDE.md line 50 no longer names styleguide.md or cites ADR-0009 as the canonical-artifact source; it points at the in-app StyleGuide page + `DesignSystem.fs`/`index.css` and cites the new ADR.
- [ ] design-check `design-rules.md` "Source of Truth" no longer names styleguide.md as canonical; it names the in-app StyleGuide page + `DesignSystem.fs` + `index.css`.
- [ ] design-system README: "Existing assets" repoints at the living system; the gate section is redefined per decision 1 and its `design-system-001` path is corrected to `done/`.
- [ ] The frontend gate remains in force (frontend tasks still `depends_on` a design-system task, anchor `design-system-001`) but is documented as "conform to the living system, reviewed on the running StyleGuide page" — captured in the new ADR and the README gate section.
- [ ] `grep -rn "styleguide.md"` over live docs/skills (excluding `.workflow.archived/`, done-task Notes, and protocol history) returns no *authoritative/source-of-truth* reference.
- [ ] Glassmorphism spec + backdrop-filter gotcha confirmed still present in CLAUDE.md (already verified at refine — re-confirm nothing was removed in the course of editing line 50).

## Notes

- **Sequencing (new at refine):** `depends_on: [design-system-grtw7]`. grtw7 is currently in `doing/` and one of its acceptance criteria updates `styleguide.md` § 4 "Sidebar nav" in lockstep. If sg8kd archived styleguide.md first, grtw7's worker would find the file gone mid-task. sg8kd must run after grtw7 reaches `done/`. The reciprocal `blocks: [design-system-sg8kd]` edge on grtw7 was **deliberately not written** during this refine — grtw7's file is owned by an in-flight worker and editing it risks a write collision; add the backlink when grtw7 lands (or let `work` reconcile it).
- **`.workflow.archived/` caveat:** CLAUDE.md describes that folder as "Historical record of pre-agentheim tasks (read-only; do not write here)." styleguide.md is an agentheim artifact, not a pre-agentheim task, so this archive placement is an off-label but **user-sanctioned exception** (decision 3). The worker should treat the user's choice as authoritative over the generic guardrail.
- Blast radius mapped 2026-07-03: `styleguide.md` is referenced in ~23 files — the authoritative/live ones to rewire are **CLAUDE.md, `.claude/skills/design-check/references/design-rules.md`, and the design-system README**; ADR-0009 to supersede. The rest are done-task Notes + protocol history (historical, leave as-is). grtw7 dropped from the rewire list per the sequencing note above.
- Decision 2 pre-verified at refine: CLAUDE.md § Conventions (line 49) holds the glassmorphism spec, § Gotchas (line 64) holds the backdrop-filter gotcha — both independent of styleguide.md.
- This task is `type: decision`; its worked output is the superseding ADR + the coordinated rewire, not application code. No `npm run build` gate needed (no source touched) — but a `grep` sweep confirms the rewire is complete.

## Outcome

Wrote **ADR-0015** (`.agentheim/knowledge/decisions/0015-styleguide-md-retired-in-app-page-authoritative.md`), superseding ADR-0009: the in-app StyleGuide page (backed by `DesignSystem.fs` + `index.css`) is now the authoritative design-system artifact; `styleguide.md` is retired; the frontend gate is redefined around the living system, keeping its force and its `design-system-001` anchor. Set `supersedes: [0009]` on 0015 and `superseded_by: [0015]` on ADR-0009 (bidirectional link); kept ADR-0009's `status: accepted` to match the repo's observed convention on superseded ADRs (ADR-0013 stays `accepted` with `superseded_by: [0014]`).

Archived `styleguide.md` from `.agentheim/contexts/design-system/styleguide.md` to `.workflow.archived/styleguide.md` via a plain filesystem move (no `git mv` — conductor stages it) — no copy remains under `contexts/design-system/`.

Rewired the three live authoritative pointers enumerated by the task:
- `CLAUDE.md` § Conventions "Design system canonical artifact" line now points at the in-app StyleGuide page + `DesignSystem.fs`/`index.css`, citing ADR-0015. Re-confirmed the glassmorphism spec (§ Conventions, line 49) and the backdrop-filter gotcha (§ Gotchas, line 64) are both still present and untouched.
- `.claude/skills/design-check/references/design-rules.md` "Source of Truth" now names the in-app StyleGuide page + `DesignSystem.fs` + `index.css`, citing ADR-0015.
- `.agentheim/contexts/design-system/README.md`: "Existing assets" repoints at the living system and records the retirement/archive; "The styleguide gate (load-bearing)" is redefined per decision 1 (still `depends_on: design-system-001`, meaning shifted to "conform to the live system, reviewed on the running StyleGuide page") and the stale `todo/design-system-001-...` path is corrected to `done/`.

Left done-task Notes, protocol history, in-code comments (`src/Client/*.fs`, `src/Client/index.css`), the StyleGuide page's own specimen copy, and other-BC task files (e.g. `contexts/games/backlog/games-status-vocabulary-reconcile.md`) untouched — all out of this task's scope per its Notes and the worker's file-touch rules. grtw7 was not a rewire target, as specified.

Final `grep -rn "styleguide.md"` sweep confirms no remaining authoritative/source-of-truth reference in live docs/skills (CLAUDE.md, the design-check skill, the design-system README) — only historical/in-code-comment references remain, all correctly out of scope.
