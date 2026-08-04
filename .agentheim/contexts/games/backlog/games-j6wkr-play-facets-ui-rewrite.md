---
id: games-j6wkr
title: Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened)
status: backlog
type: refactor
context: games
created: 2026-08-04
completed:
depends_on: [games-v4nqe, design-system-001]
blocks: []
tags: [games, ui, play-modes, facets]
related_adrs: [0053, 0054]
related_research: []
prior_art: [games-a7dqx, games-v4nqe]
---

## Why

`games-a7dqx` (split 1 of 3) and `games-v4nqe` (split 2 of 3, **landed 2026-08-04**) built and cut
over the server-side play-facets pipeline: `game_metadata_cache`'s facet columns, the ADR-0053
`Game_play_facets_overridden`/`Override_play_facets` event/command, `FacetDerivation.merge`, and
the finalized `Shared.fs` contract (`GameListItem`/`GameDetail.PlayFacets: PlayFacets` +
`GameDetail.PlayFacetsOverride: PlayFacetsOverride`, plus the `overrideGamePlayFacets` API method —
all verified on disk during this task's 2026-08-04 refinement, `Shared.fs:830-905, 1405`).
games-v4nqe also performed the *mechanical* deletion of the old 302-value picker as a forced
compile-fix (command deletion left it uncompilable) — verified gone: only tombstone comments remain
(`GameDetail/Views.fs:330, 1767`). So the app today compiles and boots with **no play-mode UI at
all**: no picker, no badges, no filters. That gap is this task's to close — the last of the three
named transitional windows this split accepts (the first two closed with games-v4nqe; this one, no
play-mode UI at all, closes here).

A key simplification confirmed at refinement: the merge happens **server-side, in F#**
(`FacetDerivation.merge` inside `GameProjection`'s query functions — there is no SQL `COALESCE`
merge tier; the original spec's `COALESCE(d.facet_override_x, c.facet_x, 0)` phrasing described a
shape v4nqe did not build). `GameListItem.PlayFacets` and `GameDetail.PlayFacets` arrive on the
wire **already merged** — the client renders them directly and filters client-side; it never
re-implements the merge.

## What (decision 4/5 from games-a7dqx's ideation session, 2026-08-04)

- The 302-value play-mode picker is confirmed already deleted by games-v4nqe (verified at
  refinement — only tombstone comments remain); this task only adds the replacement.
- Game cards/detail page render up to 4 badges — **Solo · Co-op · Versus · Couch** — with the
  online/couch distinction as a sub-label, from the merged `PlayFacets` on the DTO.
- Games list gains facet filters (at least couch co-op) as **pure client-side filters** over the
  already-merged `GameListItem.PlayFacets`, following the existing `StatusFilter: GameStatus option`
  / `Status_filter_changed` pattern in `Pages/Games/Types.fs`. No server or SQL change — the merge
  tier already happened server-side in `FacetDerivation.merge` before the DTO left the wire.
- Detail page exposes per-facet **Auto/On/Off segmented controls** — seven facets; the six `bool`
  facets get tri-state Auto/On/Off, and `Vr` gets the 4-option variant matching its DU (Auto /
  No VR / Supported / VR only — `VrSupport = NoVr | VrSupported | VrOnly`, Auto = `None` on the
  override). Auto displays the Steam-derived cached value (ADR-0054's derivation table is what
  feeds it). Controls render merged values but **POST the override record, never the merged
  record** — this is ADR-0053's flagged correctness trap: a single toggle flip must not silently
  freeze all seven facets as explicit overrides, so the client must construct and send a
  `PlayFacetsOverride` (mostly `None`, one field `Some`) built from `GameDetail.PlayFacetsOverride`
  (carried on the DTO for exactly this purpose), never a full `PlayFacets`.
- `Shared.fs`'s `getAllPlayModes`/`addGamePlayMode`/`removeGamePlayMode` are already gone
  (games-v4nqe); this task's client code calls `overrideGamePlayFacets` exclusively.

## Acceptance criteria

- [ ] `GameDetail/Views.fs`'s `PlayModePicker`, `GameDetail/Types.fs`'s
      `ShowPlayModePicker`/`AllPlayModes`, and `GameDetail/State.fs`'s `getAllPlayModes` dispatch
      are confirmed absent (deleted by games-v4nqe; pre-verified at refinement 2026-08-04 — only
      tombstone comments remain) — no leftover dead references.
- [ ] Game cards and the detail page render up to 4 badges — Solo · Co-op · Versus · Couch — with
      online/couch sub-labels, from the merged `PlayFacets`. [human-eye]
- [ ] The games list gains facet filters (at least couch co-op) as pure client-side filters over
      the already-merged `GameListItem.PlayFacets`, following the existing
      `StatusFilter`/`Status_filter_changed` pattern in `Pages/Games/Types.fs` — no server or SQL
      change for filtering. [human-eye]
- [ ] The detail page exposes per-facet Auto/On/Off segmented controls — tri-state for the six
      `bool` facets, the 4-option variant for `Vr` (Auto / No VR / Supported / VR only; Auto
      displays the Steam-derived cached value). Controls render merged values but POST the
      override record, never the merged record (the ADR-0053 correctness trap). [human-eye]
- [ ] The override payload sent by the segmented controls is built from
      `GameDetail.PlayFacetsOverride` with only the flipped field changed — flipping one facet
      leaves the other six override fields byte-identical to what the DTO carried (this is the
      machine-checkable half of the ADR-0053 trap; an MVU update-function test can assert it).
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
design-system styleguide task — this task does, and `design-system-001` is **done**, so the gate
is satisfied. A segmented-control precedent already exists in the StyleGuide
(`Pages/StyleGuide/Views.fs` ~line 2279: icon-only segmented control in a contained pill group,
visually distinct from standalone filter pills) — start from that pattern. Note its specimen keeps
toggle state in local React state because it's a *view preference*; the facet override controls
are **application state** (they POST to the server), so they go through the Elmish model/update
loop, not local state.

**Both dependencies met as of 2026-08-04** (games-v4nqe done, design-system-001 done) — nothing
blocks pickup. No dependency on games-a7dqx directly — the DTO shape and API method this task
consumes were finalized in games-v4nqe, not games-a7dqx (which only added the *types* and an
*unused* API method, not the `GameListItem`/`GameDetail` field wiring this task's components bind
to).

**Refinement reconciliation (2026-08-04):** the original spec's SQL-`COALESCE` merge-rule phrasing
(and its ADR-0048 code-comment instruction) described a shape games-v4nqe did not build — the
landed merge is pure F# (`FacetDerivation.merge`, applied inside `GameProjection`'s query
functions), so DTO facets arrive pre-merged and the ADR-0048 COALESCE-distinction comment is moot:
this task writes no SQL. Also fixed the `PlayFacets.merge` name (the module is deliberately named
`FacetDerivation` to avoid ambiguity with the `Mediatheca.Shared.PlayFacets` record — see its doc
comment).
