---
id: 0042
title: Games lifecycle remodeled to five states — OnHold removed, Completed renamed Retired, Playing never added; DesignSystem.LifecycleStatus unifies 1:1
scope: games
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-status-vocabulary-reconcile, design-system-h3q8n]
related_research: []
---

# ADR 0042: Games lifecycle remodeled to five states — OnHold removed, Completed renamed Retired, Playing never added; DesignSystem.LifecycleStatus unifies 1:1

## Context

design-system-h3q8n shipped the status-badge pattern with a six-state
vocabulary (`Backlog/InFocus/Playing/Completed/Abandoned/OnHold`) that didn't
match `Shared.GameStatus` (which had no `Playing` and added `Dismissed`), and
deliberately left the reconciliation to the Games BC. This task was
originally captured as a `type: decision` either/or: add `Playing` to
`GameStatus`, or document a mapping between the two vocabularies.

Working the decision surfaced that the real problem wasn't the vocabulary
mismatch — it was that two of the six/six-ish states didn't earn their keep.
`OnHold` never carried a distinction anyone acted on differently from
`InFocus`. `Playing` was redundant with `InFocus`, which was already the
single "I care about this right now" state (task 048 already treated any
recognized play session as a promotion to `InFocus` regardless of prior
status). And `Completed` never captured what the status actually meant in
practice — "played enough for now, might return" — which reads as a
different word than "completed" (which implies "beaten" or "finished").

## Decision

### Five states, not six

`Shared.GameStatus` becomes exactly `Backlog | InFocus | Retired | Abandoned
| Dismissed`:

- **Backlog** — not played yet, tracked because there's interest (or a
  family member's Steam library awaits evaluation).
- **InFocus** — near-future intent, want-to-recommend, or actively playing
  right now. `Playing` never becomes a separate case — `InFocus` already
  covers "actively playing."
- **Retired** (renamed from `Completed`) — played enough for now; a
  contented stop, not necessarily beaten, return possible. Distinct from
  `Abandoned`.
- **Abandoned** — stopped because the game is actively boring, not
  entertaining.
- **Dismissed** — a Backlog game never intended to be played, kept for the
  record, soft-hidden from default lists. Gains a muted badge variant
  instead of staying a vocabulary outsider.
- **OnHold is removed.** It never marked a distinction the app acted on.
  Existing `OnHold` games become `InFocus`.

Task 048's any-status auto-promotion (a recognized play session promotes a
game with any non-`InFocus` status to `InFocus`) is deliberately reaffirmed,
not narrowed — Retired, Abandoned, and Dismissed games still get pulled into
`InFocus` by new play activity, since `InFocus` explicitly means "actively
playing."

### Migration is parse-time upcast only — no event rewriting

The event store is append-only; historical `Game_status_changed` events that
say `"OnHold"` or `"Completed"` are never rewritten. Both DU↔string mappers
(`Games.fs`'s `encodeGameStatus`/`decodeGameStatus`,
`GameProjection.fs`'s `encodeGameStatus`/`parseGameStatus`) upcast on read:
`"OnHold"` → `InFocus`, `"Completed"` → `Retired` (alongside the
pre-existing `"Playing"` → `InFocus` upcast from task 048). New events only
ever serialize the five current-vocabulary strings. `GameProjection`'s
`createTables` also carries idempotent `UPDATE` migrations (mirroring the
existing `'Playing'` → `'InFocus'` one) so an on-disk read model populated
before this change is fixed at startup without requiring an explicit
rebuild, though a rebuild (ADR-0024 machinery) produces the same result.
`EventFormatting.fs`'s raw-event-history display is the one place that
*keeps* the legacy labels ("Completed", "On Hold") — it's showing what a
historical event payload literally said, not the current vocabulary.

### `DesignSystem.LifecycleStatus` reshapes to unify 1:1

The pattern's own type drops `Playing`/`OnHold` and adds `Retired` and a
muted `Dismissed` variant, landing on the same five cases as
`Shared.GameStatus`. It stays pattern-owned (series/movie compositions can
still consume `statusBadge InFocus` directly) — the mapping from a BC's real
status enum onto it is now trivial, one case to one case, written as a small
private function at each wiring site (`Games/Views.fs`,
`GameDetail/Views.fs`) rather than folded into the design system, which
still shouldn't know about `Shared.GameStatus`.

### Wiring: ad hoc status rendering is deleted in favor of the pattern

`Games/Views.fs`'s `statusLabel`/`statusTextClass` and
`GameDetail/Views.fs`'s `statusBadgeClass`/`statusLabel` are deleted. Both
pages now render status via `DesignSystem.statusBadge`. The status filter
pills (`Games/Views.fs`) and the status picker dropdown
(`GameDetail/Views.fs`'s `HeroStatus`) enumerate the five states and reuse
`DesignSystem.statusBadgeLabel` (made public) for filter-pill text, rather
than maintaining a second label vocabulary.

## Consequences

- `src/Shared/Shared.fs` — `GameStatus` is exactly five cases.
- `src/Server/Games.fs`, `src/Server/GameProjection.fs` — DU↔string mappers
  upcast legacy strings; `GameProjection.fs`'s SQL literals matching
  `'Completed'` (completion rate, HLTB comparison, completed-per-year,
  beaten-this-year) now match `'Retired'`; `Api.fs`'s dashboard stat query
  likewise.
- `src/Server/PlaytimeTracker.fs` — comment updated to the new vocabulary;
  behavior (`promoteToInFocusIfNeeded`) was already status-generic and
  needed no logic change.
- `src/Client/DesignSystem.fs`, `src/Client/index.css` — `LifecycleStatus`
  and its CSS classes reshape; `status-badge-playing`/`status-badge-on-hold`
  removed, `status-badge-retired`/`status-badge-dismissed` added.
- `src/Client/Pages/StyleGuide/Views.fs` — specimen renders all five states;
  the discrepancy note is rewritten to record the 1:1 unification instead of
  the mismatch.
- `src/Client/Pages/Games/Views.fs`, `src/Client/Pages/GameDetail/Views.fs`
  — wired to `DesignSystem.statusBadge`; ad hoc status helpers deleted.
- `src/Client/Pages/Dashboard/Views.fs` — "Completed" stat badge label,
  status color map, and the per-year chart heading now say "Retired"; the
  `GamesCompleted`/`CompletedPerYear`/`CompletionRate` DTO field names are
  kept as-is (internal names, not user-facing).
- `.agentheim/contexts/games/README.md` — ubiquitous language records the
  five states and their semantics; the stale `Playing` claim and the
  `Dismissed`-pattern-match open question are both resolved.
- Tests: `GamesTests.fs` and `PlaytimeTrackerTests.fs` updated off
  `OnHold`/`Completed`/`Playing` onto the new vocabulary; new tests cover
  the legacy-string upcast on both direct event replay and full projection
  rebuild (`PlaytimeTrackerTests.fs`'s `legacyStatusUpcastTests`).

## Alternatives considered

- **Add `Playing` to `Shared.GameStatus`, keep `OnHold`** (the original
  either/or's first branch) — rejected; would have grown the vocabulary
  instead of trimming it, and `Playing` would have been redundant with
  `InFocus`'s existing "actively playing" semantics from task 048.
- **Document a mapping without touching `Shared.GameStatus`** (the original
  either/or's second branch) — rejected; would have left `OnHold` and
  `Completed` in place as distinctions that don't inform any behavior, and
  left `DesignSystem.LifecycleStatus` permanently divergent from the real
  domain vocabulary it exists to render.
- **Narrow task-048 auto-promotion to Backlog-only** (considered alongside
  the rename) — rejected; `InFocus` explicitly means "actively playing," so
  any recognized play session earns the promotion regardless of prior
  status, with zero manual upkeep.
- **Leave `Dismissed` badge-less** (its pre-existing state) — rejected in
  favor of a muted badge variant, so every `GameStatus` case has a visual
  representation in the pattern.
