# games -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 1
- **Todo:** 0
- **Doing:** 1
- **Done:** 3
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
- **games-h4mrd** — Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration (chore) — `doing/games-h4mrd-reconstruct-play-session-history.md`
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **games-p6vkz** — Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor (feature) — `done/games-p6vkz-play-sessions-as-first-class-events.md`
- **games-w4tzc** — Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards (bug) — `done/games-w4tzc-idempotent-external-identity-events.md`
- **games-status-vocabulary-reconcile** — Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages (refactor) — `done/games-status-vocabulary-reconcile.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **games-a7dqx** — Move Game attribute metadata into the cache and stop emitting it — 7668 Game_play_mode_added events are literally Steam Store category tags and make up 43% of the entire event log (refactor) — `backlog/games-a7dqx-game-attribute-metadata-into-cache.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0050** -- Play sessions are first-class Games events keyed on (game, gaming day); pre-tracking playtime is its own dateless event; the Steam sync cursor is derived from the log via the two-fold aggregate (`TotalPlayTimeMinutes` / `SteamObservedMinutes`). -- 2026-08-01 -- `../../knowledge/decisions/0050-play-sessions-first-class-events-two-fold-cursor.md`
- **0042** -- Games lifecycle remodeled to five states (Backlog/InFocus/Retired/Abandoned/Dismissed) — OnHold removed via parse-time upcast, Completed renamed Retired, Playing never added (InFocus covers it); DesignSystem.LifecycleStatus unifies 1:1 -- 2026-08-01 -- `knowledge/decisions/0042-games-lifecycle-remodeled-to-five-states.md`
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
<!-- no research touching this BC -->
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- no concept pages yet -->
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
