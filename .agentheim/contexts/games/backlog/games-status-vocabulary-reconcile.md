---
id: games-status-vocabulary-reconcile
title: Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages
status: backlog
type: refactor
context: games
created: 2026-07-02
completed:
depends_on: [design-system-001]
blocks: []
tags: [game-status, design-system, lifecycle, status-badge]
related_adrs: []
related_research: []
prior_art: [design-system-h3q8n]
---

## Why

Originally captured as a `type: decision` task: the design-system status-badge pattern
(`DesignSystem.LifecycleStatus`, design-system-h3q8n) used a six-state vocabulary
(Backlog/InFocus/Playing/Completed/Abandoned/OnHold) that didn't match `Shared.GameStatus`
(no `Playing`, extra `Dismissed`), and no real page consumed the pattern yet.

The decision was made by the builder in the 2026-08-01 modeling session — and it goes
further than reconciling the two vocabularies: it **remodels the game lifecycle itself**.
`Playing` will never exist as a status (InFocus covers it), `OnHold` is removed as a
distinction that never mattered, and `Completed` is renamed to **Retired** because
"completed" never captured "played enough for now". What remains is implementing the
remodel and wiring the badge pattern into the real Games pages.

## What

`Shared.GameStatus` becomes exactly five states, with these builder-stated semantics:

- **Backlog** — not played yet, generally interested (that's why it's tracked); or a Steam
  family member added it and it awaits evaluation.
- **InFocus** — I want to play it in the near future, I want to recommend it to someone,
  or I'm actively playing it right now. Set manually, or automatically: any recognized
  play session (Steam sync or manual) promotes a game with any non-InFocus status to
  InFocus — **including Retired, Abandoned, and Dismissed** (task-048 behavior, deliberately
  reaffirmed 2026-08-01: since InFocus explicitly means "actively playing", playing anything
  makes it InFocus with zero manual upkeep).
- **Retired** *(renamed from Completed)* — played enough for now; a contented stop, not
  necessarily beaten, return possible. Distinct from Abandoned.
- **Abandoned** — decided not to play further because it's actively boring / not
  entertaining me.
- **Dismissed** — a Backlog game I never intend to play, kept in the system for the record
  (soft-hide, filtered from default lists). Now renders as a **muted badge variant** instead
  of being a vocabulary outsider.
- **OnHold** — removed. Anything currently OnHold becomes InFocus.

**Event-store migration is upcast-only, no event rewriting:** the DU↔string mappers
(`src/Server/Games.fs:354-365`, `src/Server/GameProjection.fs:91-102`) parse legacy
`"OnHold"` → `InFocus` and `"Completed"` → `Retired`; new events serialize `"Retired"`.
Read models are rebuilt (ADR-0024 machinery) or migrated so no legacy status strings
remain in `game_list`.

**Design system unifies 1:1:** `DesignSystem.LifecycleStatus` reshapes to the same five
states — `Playing`/`OnHold` variants and their `status-badge-playing`/`status-badge-on-hold`
CSS go away, `Retired` and a muted `Dismissed` variant arrive. `LifecycleStatus` stays a
pattern-owned type (series/movie compositions consume `statusBadge InFocus`); the
GameStatus→LifecycleStatus mapping becomes trivial 1:1.

**Wiring:** Games list rows and GameDetail drop their ad hoc status rendering
(plain colored text at `src/Client/Pages/Games/Views.fs:11-36`, DaisyUI `badge-*` at
`src/Client/Pages/GameDetail/Views.fs:65-81`) in favor of `DesignSystem.statusBadge`.

## Acceptance criteria

- [ ] `Shared.GameStatus` is exactly `Backlog | InFocus | Retired | Abandoned | Dismissed`;
      no `OnHold` or game-status `Completed` case remains anywhere; server tests and
      `npm run build` are green.
- [ ] Both DU↔string mappers upcast legacy payloads — `"OnHold"` → `InFocus`,
      `"Completed"` → `Retired` — and serialize the new vocabulary; an Expecto test proves
      event replay / projection rebuild succeeds over a store containing legacy strings.
- [ ] Games read models carry no `'OnHold'`/`'Completed'` status strings after the change;
      every SQL literal previously matching `'Completed'` matches `'Retired'`
      (`GameProjection.fs:721/793/861/886`, `Api.fs:2079`).
- [ ] Task-048 auto-promotion is preserved verbatim: a recognized play session (Steam sync
      or manual) promotes a game from **any** non-InFocus status — including Retired,
      Abandoned, Dismissed — to InFocus; `PlaytimeTracker` tests pass with the comment
      updated to the new vocabulary.
- [ ] `DesignSystem.LifecycleStatus` is the same five states with a muted Dismissed variant;
      `status-badge-retired`/`status-badge-dismissed` exist in `index.css`,
      `status-badge-playing`/`status-badge-on-hold` are gone; the StyleGuide specimen renders
      all five, and both the discrepancy note (`StyleGuide/Views.fs:1511`) and the
      `DesignSystem.fs` doc comment are rewritten to record the 1:1 unification.
- [ ] Games list rows and GameDetail render status via `DesignSystem.statusBadge` (the ad hoc
      `statusLabel`/`statusTextClass`/`badge-*` helpers are deleted); the status filter pills
      and GameDetail's status picker enumerate exactly the five states.
- [ ] No user-facing surface still says "Completed" or "On Hold" for games — dashboard stat
      badges and the per-year chart heading (`Dashboard/Views.fs:3487/3941`), the dashboard
      status color map (`Dashboard/Views.fs:3555-3561`), and event formatting for new events
      (`EventFormatting.fs` may keep legacy strings for displaying historical raw payloads).
- [ ] The Games BC README's ubiquitous language records the five states with the builder's
      semantics, drops the stale `Playing` claim, and resolves the open-question line about
      `Dismissed` pattern-match coverage.
- [ ] An ADR records the remodel: the five-state vocabulary, the Retired rename, OnHold
      removal with parse-time upcast, the no-Playing decision reaffirming task-048 InFocus
      semantics, and the 1:1 design-system unification.
- [ ] Retired and Dismissed badges read as quiet, distinct states beside the colored ones,
      on both the StyleGuide page and GameDetail. [human-eye]

## Notes

- Decided by the builder 2026-08-01 (modeling session): no `Playing` status ever; rename
  chosen from Retired/Played/Finished/Satisfied; auto-promotion scope deliberately kept at
  any-status (task 048), not narrowed to Backlog-only; Dismissed gets a muted badge rather
  than staying badge-less.
- `vision.md`'s game-lifecycle wording was updated to the five-state vocabulary during the
  same refinement — no drift to reconcile there.
- DTO field names in the stats layer (`GamesCompleted`, `CompletedPerYear`,
  `CompletionRate` for games) may be renamed to the Retired vocabulary or kept as internal
  names — worker's judgment; only user-facing labels are a criterion.
- The design-system side's authoritative artifact is the **in-app StyleGuide page**
  (styleguide.md is retired, design-system-sg8kd) — update the live specimen, not a prose doc.
- Prior art: design-system-h3q8n shipped the badge pattern + specimen only, deliberately
  leaving `Shared.GameStatus` untouched and this reconciliation to the Games BC.
