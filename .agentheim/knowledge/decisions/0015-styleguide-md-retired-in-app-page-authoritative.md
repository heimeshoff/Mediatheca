---
id: 0015
title: styleguide.md retired; in-app StyleGuide page (DesignSystem.fs + index.css) is authoritative
scope: design-system
status: accepted
date: 2026-07-03
supersedes: [0009]
superseded_by: []
related_tasks: [design-system-sg8kd]
related_research: []
---

# ADR 0015: styleguide.md retired; the in-app StyleGuide page is the authoritative design-system artifact

## Context

ADR-0009 (2026-05-27, `design-system-001`) made `.agentheim/contexts/design-system/styleguide.md` the canonical, reviewable design-system artifact, establishing the split: `index.css`/`DesignSystem.fs` authoritative for *values*, `styleguide.md` authoritative for *intent* and the frontend task gate. That document accumulated real intent and rationale across several design-system tasks (`r7k2m`, `h3q8n`, `bky6v`, `fq3vp`) and was kept in lockstep with shipped changes (most recently `design-system-grtw7`'s sidebar revert).

On 2026-07-03 the user stated directly that `styleguide.md` **should not exist and has no authoritative quality**. The authoritative design-system reference is "the style guide in the front end" — the live, in-app `StyleGuide` page (`src/Client/Pages/StyleGuide/`), which renders real Feliz specimens backed by `DesignSystem.fs` (typed compositions) and `index.css` (tokens/values). A standalone prose document risks drifting from what's actually shipped; a page that renders the running code cannot drift in the same way.

This is a deliberate reversal of ADR-0009's central choice, not a correction of a mistake — ADR-0009 stands as the accurate record of why the standalone document was chosen at the time; this ADR is the record of why it was later retired.

## Decision

1. **The in-app StyleGuide page is the authoritative design-system artifact**, backed by:
   - `src/Client/DesignSystem.fs` — typed Feliz/Tailwind class compositions (the reviewable, in-code source of pattern *intent*).
   - `src/Client/index.css` — token definitions, theme, glassmorphism classes (authoritative for concrete *values*, unchanged from ADR-0009).
   - `src/Client/Pages/StyleGuide` — the live review surface: every component pattern rendered in situ, always in sync with what ships because it *is* what ships.
2. **`styleguide.md` is retired.** Archived (not deleted) to `.workflow.archived/styleguide.md` as a read-only historical record of the intent/rationale it accumulated. No longer read, updated, or cited as authoritative anywhere in live docs/skills.
3. **The frontend "styleguide gate" is redefined, not dropped.** Every frontend/UI task in any BC still `depends_on` a design-system task — the anchor stays `design-system-001` (the foundational, done, user-signed-off styleguide task). What changes is the gate's *meaning*: conform to the living design system — `DesignSystem.fs` + `index.css` — reviewed on the running StyleGuide page, not gate on a self-contained prose document. The gate keeps its force; it re-anchors from a document review to a running-code review.
4. **CLAUDE.md, the `design-check` skill, and the design-system BC README are repointed** at the living system (in-app StyleGuide page + `DesignSystem.fs` + `index.css`) and cite this ADR in place of ADR-0009. The glassmorphism spec and backdrop-filter gotcha in `CLAUDE.md` are independent of `styleguide.md` (they were only ever *reproduced* there per ADR-0009's "reproduce verbatim AND point" choice) and are unaffected by this retirement.
5. **Historical references are left as-is.** Done-task Notes sections and `protocol.md` history that mention `styleguide.md` are not rewritten — they are an accurate record of what was true when they were written.

## Consequences

- One fewer artifact to keep in lockstep with shipped code; the review surface *is* the shipped code, eliminating the drift risk ADR-0009's § 6/§ 7 lockstep rules were mitigating.
- Design-system intent that used to live in `styleguide.md` prose (rationale, "why this token/pattern") now lives only in ADRs, the BC README's Ubiquitous language section, and code comments in `DesignSystem.fs`/`index.css` — there is no longer a single consolidated prose narrative. If that's later felt as a loss, a future task can reintroduce a narrative doc, but it would need its own lockstep discipline to avoid repeating the drift concern that motivated the original ADR-0009 choice.
- The frontend gate's anchor task (`design-system-001`) is now a foundational/historical anchor whose own artifact (the original styleguide.md draft) is retired; the anchor stays valid because the gate cares about the *dependency edge* (a frontend task can't skip design-system review), not about that specific task's original deliverable still existing.
- `.claude/skills/design-check/references/design-rules.md`, `CLAUDE.md`, and the design-system README all needed coordinated edits in this same task to avoid leaving dangling references to a file that no longer exists at its old path.

## Alternatives rejected

- **Hard-delete `styleguide.md`.** Rejected: the document captured real, non-trivial rationale across four prior tasks; deleting it destroys that history for no benefit over archiving it.
- **Keep `styleguide.md` as a secondary/non-authoritative doc alongside the in-app page.** Rejected: the user was explicit that it "should not exist" in the live tree; keeping a stale copy invites exactly the confusion (which one is real?) this ADR exists to resolve.
- **Drop the frontend gate entirely now that its original artifact is gone.** Rejected: the gate's value (frontend work reviewed against design-system before landing) is independent of *which* artifact backs the review; dropping it was not asked for and would regress an established, useful practice.
