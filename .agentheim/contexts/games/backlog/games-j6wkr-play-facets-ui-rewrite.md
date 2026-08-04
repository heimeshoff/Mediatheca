---
id: games-j6wkr
title: Rewrite the Games UI for typed play facets — delete the 302-value play-mode picker, add Auto/On/Off facet override controls and badges/list filters, consuming the PlayFacets contract games-v4nqe lands (split 3 of 3)
status: backlog
type: refactor
context: games
created: 2026-08-04
completed:
depends_on: [games-v4nqe, design-system-001]
blocks: []
tags: [games, ui, play-modes, facets]
related_adrs: [0053]
related_research: []
prior_art: [games-a7dqx]
---

## Why

`games-a7dqx` (split 1 of 3) and `games-v4nqe` (split 2 of 3) built and cut over the server-side
play-facets pipeline: `game_metadata_cache`'s facet columns, the ADR-0053
`Game_play_facets_overridden`/`Override_play_facets` event/command, `PlayFacets.merge`, and the
finalized `Shared.fs` contract (`GameListItem`/`GameDetail.PlayFacets: PlayFacets` +
`GameDetail.PlayFacetsOverride: PlayFacetsOverride`, plus the `overrideGamePlayFacets` API method).
games-v4nqe also performed the *mechanical* deletion of the old 302-value picker as a forced
compile-fix (command deletion left it uncompilable) — so by the time this task starts, the app
compiles and boots with **no play-mode UI at all**: no picker, no badges, no filters. That gap is
this task's to close, and is the last of the three named transitional windows this split accepts
(games-a7dqx→games-v4nqe: a frozen-value read/write gap for a handful of already-seeded fields;
games-a7dqx→games-v4nqe: facet badges absent until the backfill catches up, decision 3, explicit
regardless of split; games-v4nqe→this task: no play-mode UI at all). This task has no dependency on
exactly when games-v4nqe's *server*-side cutover completed beyond the DTO shape existing — it needs
`PlayFacets`/`PlayFacetsOverride` on the wire and `overrideGamePlayFacets` callable, nothing more.

## What (decision 4/5 from games-a7dqx's ideation session, 2026-08-04)

- The 302-value play-mode picker is confirmed already deleted by games-v4nqe — verify, don't
  re-delete; this task only adds the replacement.
- Game cards/detail page render up to 4 badges — **Solo · Co-op · Versus · Couch** — with the
  online/couch distinction as a sub-label, from the merged `PlayFacets` on the DTO.
- Games list gains facet filters (at least couch co-op), backed by the merge-rule
  `COALESCE(d.facet_override_x, c.facet_x, 0)` — a code comment distinguishes this merge-rule
  `COALESCE` from the staleness-masking `COALESCE` ADR-0048 rejected for Series (the two look
  similar but mean opposite things: this one composes two *live, current* tiers; that one would
  have masked a stale value).
- Detail page exposes per-facet **Auto/On/Off segmented controls** (VR: Auto / No VR / Supported /
  VR only; Auto displays the Steam-derived cached value). Controls render merged values but
  **POST the override record, never the merged record** — this is ADR-0053's flagged correctness
  trap: a single toggle flip must not silently freeze all seven facets as explicit overrides, so
  the client must construct and send a `PlayFacetsOverride` (mostly `None`, one field `Some`), not
  a full `PlayFacets`.
- `Shared.fs`'s `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` are already gone
  (games-v4nqe); this task's client code calls `overrideGamePlayFacets` exclusively.

## Acceptance criteria

- [ ] `GameDetail/Views.fs`'s `PlayModePicker`, `GameDetail/Types.fs`'s
      `ShowPlayModePicker`/`AllPlayModes`, and `GameDetail/State.fs`'s `getAllPlayModes` dispatch
      are confirmed absent (deleted by games-v4nqe) — no leftover dead references.
- [ ] Game cards and the detail page render up to 4 badges — Solo · Co-op · Versus · Couch — with
      online/couch sub-labels, from the merged `PlayFacets`. [human-eye]
- [ ] The games list gains facet filters (at least couch co-op) backed by the merge-rule
      `COALESCE(d.facet_override_x, c.facet_x, 0)` (a code comment distinguishes this merge-rule
      `COALESCE` from the staleness-masking `COALESCE` ADR-0048 rejected). [human-eye]
- [ ] The detail page exposes per-facet Auto/On/Off segmented controls (VR: Auto / No VR /
      Supported / VR only; Auto displays the Steam-derived cached value). Controls render merged
      values but POST the override record, never the merged record (the ADR-0053 correctness
      trap). [human-eye]
- [ ] Client code calls `overrideGamePlayFacets` exclusively; no reference to the deleted
      `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` remains anywhere in `src/Client`.
- [ ] `npm run build` passes (Fable compile gate) with the new UI in place.

## Notes

**Fold-forward from the original file's worker survey (2026-08-04), scoped to this task:**
`Shared.fs`, `GameDetail/Views.fs` (2073 lines), `GameDetail/State.fs`, `GameDetail/Types.fs` —
sizes confirmed by the original survey, not read in detail at that time; this task carries the
full UI construction (4 Auto/On/Off segmented controls plus the VR 4-option variant, badges,
list-page facet filters). The picker deletion itself is **not** this task's job — verify it's
already gone (games-v4nqe), don't redo it.

**Design-system gate:** per the Games BC README, frontend tasks in this BC must `depends_on` the
design-system styleguide task — this task does (games-a7dqx and games-v4nqe do not, since neither
touches new visual/styleguide-consuming client code; games-v4nqe's client change was a pure
deletion, not new design-system consumption).

**No dependency on games-a7dqx directly** — only on games-v4nqe, since the DTO shape and API method
this task consumes are finalized there, not in games-a7dqx (which only added the *types* and an
*unused* API method, not the `GameListItem`/`GameDetail` field wiring this task's components bind
to).
