# games -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 1
- **Doing:** 0
- **Done:** 7
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **games-b8xnw** — Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge (feature) — `todo/games-b8xnw-steam-deck-compat-readiness.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **games-j6wkr** — Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened) (refactor) — `done/games-j6wkr-play-facets-ui-rewrite.md`
- **games-v4nqe** — Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible) (refactor) — `done/games-v4nqe-emission-cutover-column-drop.md`
- **games-a7dqx** — Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI) (refactor) — `done/games-a7dqx-game-attribute-metadata-into-cache.md`
- **games-h4mrd** — Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration (chore) — `done/games-h4mrd-reconstruct-play-session-history.md`
- **games-p6vkz** — Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor (feature) — `done/games-p6vkz-play-sessions-as-first-class-events.md`
- **games-w4tzc** — Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards (bug) — `done/games-w4tzc-idempotent-external-identity-events.md`
- **games-status-vocabulary-reconcile** — Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages (refactor) — `done/games-status-vocabulary-reconcile.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0057** -- Play facets UI: the 4-badge row is Solo/Co-op/Versus + a Couch summary badge with online/couch sub-labels; the ADR-0053 override trap is guarded by pure `Shared` record-update functions (`withSolo` et al.), Expecto-tested in place of absent client-test infra -- 2026-08-04 -- `../../knowledge/decisions/0057-play-facets-ui-badge-mapping-and-override-trap-guard.md`
- **0055** -- Game genres stays an event-carried identity-card projection column (amends ADR-0043's Game row back into compliance); games-v4nqe's genres cache-cutover is reverted — `game_metadata_cache.genres` is kept but permanently unused -- 2026-08-04 -- `../../knowledge/decisions/0055-game-genres-stays-event-carried-identity-card.md`
- **0054** -- The Steam category-id → PlayFacets derivation table is fixed from 13 live-verified appId fixtures (ids decoded with `&l=english`); bare umbrella ids resolve to the online facet -- 2026-08-04 -- `../../knowledge/decisions/0054-steam-category-id-facet-table-live-verified.md`
- **0053** -- Game play facets are cache-derived from Steam; per-field manual overrides stay event-sourced (`Game_play_facets_overridden` carrying an all-`Option` record) and merge at query time via a pure `PlayFacets.merge` -- 2026-08-04 -- `../../knowledge/decisions/0053-game-play-facets-cache-derived-event-sourced-override.md`
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
