---
id: games-status-vocabulary-reconcile
title: Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages
status: done
type: refactor
context: games
created: 2026-07-02
completed: 2026-08-01
depends_on: [design-system-001]
blocks: []
tags: [game-status, design-system, lifecycle, status-badge]
related_adrs: [0042]
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

- [x] `Shared.GameStatus` is exactly `Backlog | InFocus | Retired | Abandoned | Dismissed`;
      no `OnHold` or game-status `Completed` case remains anywhere; server tests and
      `npm run build` are green.
- [x] Both DU↔string mappers upcast legacy payloads — `"OnHold"` → `InFocus`,
      `"Completed"` → `Retired` — and serialize the new vocabulary; an Expecto test proves
      event replay / projection rebuild succeeds over a store containing legacy strings.
- [x] Games read models carry no `'OnHold'`/`'Completed'` status strings after the change;
      every SQL literal previously matching `'Completed'` matches `'Retired'`
      (`GameProjection.fs:721/793/861/886`, `Api.fs:2079`).
- [x] Task-048 auto-promotion is preserved verbatim: a recognized play session (Steam sync
      or manual) promotes a game from **any** non-InFocus status — including Retired,
      Abandoned, Dismissed — to InFocus; `PlaytimeTracker` tests pass with the comment
      updated to the new vocabulary.
- [x] `DesignSystem.LifecycleStatus` is the same five states with a muted Dismissed variant;
      `status-badge-retired`/`status-badge-dismissed` exist in `index.css`,
      `status-badge-playing`/`status-badge-on-hold` are gone; the StyleGuide specimen renders
      all five, and both the discrepancy note (`StyleGuide/Views.fs:1511`) and the
      `DesignSystem.fs` doc comment are rewritten to record the 1:1 unification.
- [x] Games list rows and GameDetail render status via `DesignSystem.statusBadge` (the ad hoc
      `statusLabel`/`statusTextClass`/`badge-*` helpers are deleted); the status filter pills
      and GameDetail's status picker enumerate exactly the five states.
- [x] No user-facing surface still says "Completed" or "On Hold" for games — dashboard stat
      badges and the per-year chart heading (`Dashboard/Views.fs:3487/3941`), the dashboard
      status color map (`Dashboard/Views.fs:3555-3561`), and event formatting for new events
      (`EventFormatting.fs` may keep legacy strings for displaying historical raw payloads).
- [x] The Games BC README's ubiquitous language records the five states with the builder's
      semantics, drops the stale `Playing` claim, and resolves the open-question line about
      `Dismissed` pattern-match coverage.
- [x] An ADR records the remodel: the five-state vocabulary, the Retired rename, OnHold
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

## Outcome

`Shared.GameStatus` is now exactly `Backlog | InFocus | Retired | Abandoned | Dismissed`.
`OnHold` was removed and `Completed` renamed `Retired`, with parse-time-only upcast in both
DU↔string mappers (`Games.fs`, `GameProjection.fs`) plus idempotent read-model migration
statements — no event rewriting. `DesignSystem.LifecycleStatus` reshapes to the same five
states (new `status-badge-retired`/`status-badge-dismissed` CSS, `-playing`/`-on-hold`
removed) and now maps 1:1 onto `Shared.GameStatus`; `Games/Views.fs` and
`GameDetail/Views.fs` drop their ad hoc status helpers in favor of `DesignSystem.statusBadge`.
Task 048's any-status auto-promotion to InFocus is preserved verbatim. ADR-0042 records the
decision. Full details and file list in the worker's final report.

Test run: `dotnet run --project tests/Server.Tests/Server.Tests.fsproj` — 445/445 passing
(4 new: 2 legacy-upcast round-trip tests in `GamesTests.fs`, net +2 in
`PlaytimeTrackerTests.fs`'s new `legacyStatusUpcastTests` list after removing the
no-longer-constructible OnHold-specific test case). `npm run build` — clean, no Fable
compile errors.

## Verifier note (iteration 1)

REASONS:
- Ubiquitous-language conflict across BCs (check 4): `.agentheim/contexts/design-system/README.md:23` ("Lifecycle status vocabulary") still defines `DesignSystem.LifecycleStatus` as "the status-badge pattern's own **six-state** vocabulary (Backlog/InFocus/Playing/Completed/Abandoned/OnHold)" and `Shared.GameStatus` as "(Backlog/InFocus/Completed/Abandoned/OnHold/Dismissed — no `Playing`, has `Dismissed`)". This diff deletes every one of `Playing`, `Completed`, and `OnHold` from both types (`src/Client/DesignSystem.fs:279-284`, `src/Shared/Shared.fs:799-805`), so the owning BC's README now asserts a shape the code no longer has.
- Same entry states the vocabulary mismatch "is tracked as a Games BC backlog item; mapping one onto the other ... is a Games BC decision" — this diff *is* that decision (ADR-0042, 1:1 unification), so the design-system README also advertises outstanding work that is now closed. The worker updated the two artifacts criterion 5 enumerated (`StyleGuide/Views.fs:1510` note, `DesignSystem.fs` doc comment) but left the BC README that owns the term untouched, and reported `NEW_BACKLOG_ITEMS: none` — no sanctioned handoff was filed either.
- Secondary occurrence of the same rot: `.agentheim/contexts/journal/README.md:32` still describes `Game_status_changed` as "(Playing/Completed transitions)".
- Note for the next worker — these are the only defects found. Checks 1, 2 and 3 pass on their own terms: the Expecto suite from the worktree is green (445 passed, 0 failed, exit 0) and `npm run build` is green (exit 0); the 4 new tests are real and would fail without the production change; scope is clean.

SUGGESTED_FIX: Do **not** edit the design-system BC README from this task (that would be an out-of-scope cross-BC edit). Instead file a backlog task file under `.agentheim/contexts/design-system/backlog/` — reported via `NEW_BACKLOG_ITEMS` — to rewrite that README's "Lifecycle status vocabulary" entry to the five-state 1:1 unification (citing ADR-0042 and this task), and to fix the stale `(Playing/Completed transitions)` phrase in the journal BC README's Games line at the same time.

ITERATION_HINT: likely-fixable
