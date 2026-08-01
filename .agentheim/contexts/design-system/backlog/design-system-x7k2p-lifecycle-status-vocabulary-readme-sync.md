---
id: design-system-x7k2p
title: Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)
status: backlog
type: chore
context: design-system
created: 2026-08-01
depends_on: []
blocks: []
tags: [design-system, readme, lifecycle-status, cleanup, cross-bc]
related_adrs: [0042]
related_research: []
prior_art: [games-status-vocabulary-reconcile]
---

## Why

Task `games-status-vocabulary-reconcile` (games BC) remodeled the game lifecycle to five
states — `Backlog | InFocus | Retired | Abandoned | Dismissed` — removing `Playing`,
`Completed` (renamed `Retired`), and `OnHold` from both `Shared.GameStatus`
(`src/Shared/Shared.fs`) and `DesignSystem.LifecycleStatus` (`src/Client/DesignSystem.fs`),
and unified the two types 1:1. This decision is recorded in ADR-0042.

That task's iteration-1 verifier caught that two READMEs still describe the pre-remodel,
six-state vocabulary and an open reconciliation item that ADR-0042 has since closed. Per
worker scope rules, a task cannot edit another BC's README from within its own task, so this
follow-up task exists to close the gap. Leaving these READMEs stale would poison future
sessions that read them first for ubiquitous language.

## What

- Rewrite `.agentheim/contexts/design-system/README.md:23` (the "Lifecycle status
  vocabulary" entry). It currently describes `DesignSystem.LifecycleStatus` as a **six-state**
  vocabulary (`Backlog/InFocus/Playing/Completed/Abandoned/OnHold`) distinct from
  `Shared.GameStatus`'s `(Backlog/InFocus/Completed/Abandoned/OnHold/Dismissed — no
  Playing, has Dismissed)`, and calls mapping the two onto each other "a Games BC decision"
  tracked as an open backlog item. Replace this with: both types are now the same five states
  — `Backlog | InFocus | Retired | Abandoned | Dismissed` — unified 1:1, per ADR-0042 and
  task `games-status-vocabulary-reconcile`. Remove the "open Games BC backlog item" framing;
  the reconciliation is done.
- Fix `.agentheim/contexts/journal/README.md:32`, which still describes the
  `Game_status_changed` event as "(Playing/Completed transitions)". Update it to reflect the
  current vocabulary (no `Playing`; `Completed` is now `Retired`) — phrase it however best
  fits the surrounding sentence, e.g. "(status transitions, including the Retired terminal
  state)".
- Do not touch any other section of either README. Do not touch the games BC README (already
  updated by the source task) or any task files.

## Acceptance criteria

- [ ] `.agentheim/contexts/design-system/README.md`'s "Lifecycle status vocabulary" entry no
      longer mentions `Playing`, `OnHold`, or a six-state vocabulary; it states the five-state
      `Backlog | InFocus | Retired | Abandoned | Dismissed` vocabulary is shared 1:1 between
      `Shared.GameStatus` and `DesignSystem.LifecycleStatus`, citing ADR-0042.
- [ ] The same entry no longer frames the design-system/games vocabulary mapping as an open
      Games BC backlog item — it reflects that the reconciliation is closed.
- [ ] `.agentheim/contexts/journal/README.md`'s Games line (currently line 32) no longer says
      "(Playing/Completed transitions)" — it accurately describes the current five-state
      vocabulary with no stale status names.
- [ ] No other lines in either README are changed.
