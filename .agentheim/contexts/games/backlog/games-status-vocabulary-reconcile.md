---
id: games-status-vocabulary-reconcile
title: Reconcile GameStatus with the design system's LifecycleStatus vocabulary
status: backlog
type: decision
context: games
created: 2026-07-02
depends_on: []
blocks: []
tags: [game-status, design-system, lifecycle, status-badge]
related_adrs: []
related_research: []
prior_art: [design-system-h3q8n]
---

## Why

While building the Velvet Lobby status-badge pattern (`DesignSystem.statusBadge` /
`DesignSystem.LifecycleStatus`, design-system-h3q8n), a discrepancy surfaced between the
design brief's status vocabulary and the actual Games BC domain model:

- The design brief (and the design-system pattern that codifies it) uses a six-state lifecycle:
  **Backlog → InFocus → Playing → Completed / Abandoned / OnHold**.
- `Shared.GameStatus` (`src/Shared/Shared.fs`) is: `Backlog | InFocus | Completed | Abandoned |
  OnHold | Dismissed` — **no `Playing` state**, and an extra `Dismissed` state the design brief
  doesn't have.

design-system-h3q8n deliberately did **not** touch `Shared.GameStatus` — that's Games BC's
domain model, out of scope for a design-system task. It instead defined `LifecycleStatus` as
the status-badge *pattern's* own generic vocabulary (matching the brief), documented in
`styleguide.md` § 4 Status badges and the design-system BC README as intentionally distinct from
`Shared.GameStatus`, with a note that reconciling them is a Games BC decision.

## What

Decide (a `type: decision` task, likely needing the tactical-modeler) whether:
1. `GameStatus` should gain a `Playing` state (and if so, what `Dismissed` maps to — is it
   folded into `Abandoned`, kept separate, or dropped?), or
2. The design-system `LifecycleStatus` vocabulary and `Shared.GameStatus` are intentionally
   different (e.g. `Dismissed` is a Games-specific soft-delete/hide state that never needs a
   badge), in which case document the mapping — which `GameStatus` cases render which
   `LifecycleStatus` badge — explicitly in the Games BC README, and give `Dismissed` an
   explicit choice (e.g. render no badge, or render as `Backlog`).

Whichever direction, once decided, wire the real GameDetail / Games list pages to call
`DesignSystem.statusBadge` with the resolved mapping (currently no BC page consumes the new
pattern yet — design-system-h3q8n only shipped the pattern + StyleGuide specimen, not the wiring).

## Acceptance criteria

- [ ] A decision is recorded (ADR if the answer isn't obvious) on whether `GameStatus` changes
      or the two vocabularies stay intentionally distinct with a documented mapping.
- [ ] Games BC README's ubiquitous language reflects the resolved status vocabulary.
- [ ] At least one real Games page (list row and/or GameDetail) renders `DesignSystem.statusBadge`
      via the resolved mapping, replacing any ad hoc status-pill markup currently there.

## Notes

- Not urgent — `DesignSystem.statusBadge` / `LifecycleStatus` work today as a self-contained
  pattern with its own specimen; this task is about wiring it to the real Games domain data and
  resolving the vocabulary mismatch, not fixing a bug.
- See `.agentheim/contexts/design-system/styleguide.md` § 4 "Status badges" and the design-system
  BC README's "Lifecycle status vocabulary" ubiquitous-language entry for the full context.
