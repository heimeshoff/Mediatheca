# games -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 1
- **Todo:** 0
- **Doing:** 0
- **Done:** 15
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **games-kfpqp** — Deck-compat job — re-check already recorded verdicts once they pass an age limit (Unknown 30 days, Playable/Unsupported 90, Verified 180), capped per night, so Valve's verdict changes reach existing games (feature) — `done/games-kfpqp-deck-compat-age-based-verdict-recheck.md`
- **games-wkyf0** — Deck-compat job — stop re-fetching permanently failing games every night by recording failed attempts and retrying them on a growing delay (bug) — `done/games-wkyf0-deck-compat-failed-fetch-retry-backoff.md`
- **games-fffvm** — Re-sanitize existing game descriptions — a resumable, throttled backfill that re-fetches every already-cached game's description (Steam-linked via the storefront, RAWG-only via RAWG details) through games-r1tx4's sanitizer, so games imported before it gain paragraphs/emphasis and RAWG-only games get the description games-v4nqe silently dropped (chore) — `done/games-fffvm-resanitize-existing-game-descriptions-backfill.md`
- **games-r1tx4** — Apply the Audible description sanitizer + RichText renderer pattern to game descriptions (Steam/RAWG) (chore) — `done/games-r1tx4-apply-the-audible-description-sanitizer-richtext-renderer-pa.md`
- **games-t69rb** — Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise (feature) — `done/games-t69rb-game-detail-persistent-side-cards-journal-first.md`
- **games-ev65k** — Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab (feature) — `done/games-ev65k-game-release-dates-from-steam.md`
- **games-k3vps** — Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results (feature) — `done/games-k3vps-search-source-toggles-rawg-steam.md`
- **games-b8xnw** — Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge (feature) — `done/games-b8xnw-steam-deck-compat-readiness.md`
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
- **games-b76z3** — Research IGDB as a supplement to, or successor of, RAWG for game metadata — game modes / multiplayer modes for non-Steam play facets, franchise and collection links, time-to-beat as a HowLongToBeat scraping replacement, external-id mapping to Steam appIds — and weigh the Twitch client-credential cost against the RAWG identity-card migration question (ADR-0055) (spike) — `backlog/games-b76z3-igdb-as-rawg-supplement-or-successor-research.md`
<!-- backlog-list:end -->


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/games/INDEX.md`
