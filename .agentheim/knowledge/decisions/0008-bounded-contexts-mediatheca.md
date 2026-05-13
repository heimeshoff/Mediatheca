---
id: 0008
title: Ten bounded contexts for Mediatheca
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0008: Ten bounded contexts for Mediatheca

> Decision made during brainstorm extension on 2026-05-12 while migrating the project onto agenthoff.

## Context

Pre-existing `CLAUDE.md` named seven bounded contexts (Movies, Journal, Friends, Curation, Intelligence, Integration, Administration), but the codebase treats **Series** and **Games** as first-class siblings of Movies with their own event families, projections, and dashboard tabs. Folding all media types into "Movies" would create an umbrella BC whose name misrepresents its contents.

Additionally, agenthoff's `model` skill requires a `design-system` BC to hold the **styleguide gate** that every frontend task in any BC depends on. The project already has an in-app StyleGuide page, glassmorphism rules in `CLAUDE.md`, and a `design-check` skill — these need a home in the context map.

## Decision

Adopt **ten bounded contexts**:

| BC | Classification |
|---|---|
| Movies | core |
| Series | core |
| Games | core |
| Journal | core |
| Intelligence | core |
| Friends | supporting |
| Curation | supporting |
| Design system | supporting |
| Integration | generic |
| Administration | generic |

Movies / Series / Games are sibling write-side BCs; Journal and Intelligence are read-side BCs that subscribe to their events. Friends is the lightweight upstream registry. Curation, Integration, Administration and Design system play the roles documented in their READMEs.

## Consequences

### Positive
- Context map matches the code organization. No translation layer between "where it lives in source" and "where it lives in the model".
- Each media type owns its language without an umbrella that smudges Movie / Series / Game distinctions.
- Journal cleanly separates the cross-media diary from the per-type write authority.
- The styleguide gate has a real home; every frontend task can depend on it.

### Negative
- Three near-parallel BCs (Movies / Series / Games) carry some duplication in event names (`*_added_to_library`, `*_recommended_by`, etc.). Tempting to "abstract", but the convergence is incidental — the lifecycles diverge enough that a generic `Media` aggregate would lose more than it saves.
- Books (vision v2) and any future media types will follow the same triplicate pattern unless the convergence proves real.

### Neutral
- The list grew from seven to ten relative to `CLAUDE.md`'s aspirational sketch. `CLAUDE.md` will be kept aligned via its project-structure pointer at `.agenthoff/`.

## Alternatives considered

- **Seven BCs with Movies as umbrella** — rejected (name misleads, code says otherwise).
- **Seven BCs, rename Movies → Library** — honest but loses the per-type language clarity; also doesn't solve the design-system gate question.
- **Combine Journal + Intelligence** — both are read-side and downstream of the media BCs, so a merge looks tempting. Kept separate because Journal is a chronological timeline (what / when / with whom) and Intelligence is a synthesis layer (stats, comparisons, breakdowns). The reads they serve are shaped differently enough that one model would be a hybrid that suits neither.

## References

- `.agenthoff/context-map.md` — full map with relationships.
- `.agenthoff/contexts/<bc>/README.md` — per-BC details.
- Existing code: `src/Server/{Movies,Series,Games,Friends,Catalogs,EventStore}.fs` etc.
- Vision: `.agenthoff/vision.md`.
