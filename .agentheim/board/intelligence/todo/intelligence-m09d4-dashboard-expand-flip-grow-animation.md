---
id: intelligence-m09d4
title: Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)
status: todo
type: feature
context: intelligence
created: 2026-09-12
completed:
depends_on: [design-system-m2v88, design-system-001]
blocks: []
tags: [dashboard, expand-card, motion, animation, flip]
related_adrs: [0073, 0015, 0064]
related_research: []
prior_art: []
---

## Why

Commit 74e1ab8 shipped expandable dashboard cards: an expand button grows a query-backed
card over the whole tab area and re-runs its query without the row limit. Today the grow
is a subtree swap — the collapsed card becomes `invisible` and the expanded surface appears
at full size in the same frame, then the whole list re-renders when the unlimited fetch
lands. The builder's ask, verbatim: clicking expand should *not* jump to full size; the card
should get there within roughly half a second, and the elements that were already rendered
in the small version should move to where they sit in the expanded version, so it reads as
one dynamic process. Refined after the builder checked the running app (2026-09-12): today
the expanded list *fades in* whole (the `animate-fade-in-up` entrance on the grown
surface); what they want instead is the already-rendered movies moving *upwards* so they
sit at the top of the screen, with the rest of the screen then filling up with the new
entries.

## What

Wire the design-system FLIP primitive (`design-system-m2v88`, ADR-0073) into
`src/Client/Pages/Dashboard/Views.fs`'s expand/collapse:

- Wrap `tabArea`'s grown-surface branch in a `[<ReactComponent>]` (e.g. `GrowingTabArea`)
  that holds a `React.useRef` before-snapshot and the live `Animation` handles, driven by
  one `useLayoutEffect` keyed on `Expanded` — the same effect serves expand and collapse
  ("`Expanded` changed and a snapshot exists → play the plan").
- Take the snapshot (`Motion.Flip.snapshot` over the tab area) in the expand and collapse
  buttons' `onClick`, *before* `dispatch`. Cancel in-flight animations before re-measuring
  on re-entry, so rapid toggling never leaves a stray transform.
- Give every expandable item renderer a `Motion.flipKey` — movie/series/game slug, person
  name, achievement composite id — scoped per card so the All tab and a media tab reading
  the same query never clash. The card surface itself gets a key too, for the height grow
  (`growSurface`), independent of the item transforms.
- `ExpandedItemsLoaded` does not animate: the unlimited list extends the limited one in
  order, so the fetched items append below the ones that travelled, and the travelled ones
  do not move again.
- Move the scroll-into-view into the same layout effect, **instant** (`behavior:
  "instant"`) and *before* the after-snapshot, so the posters' measured travel includes
  the scroll shift: on screen they slide upward to the top of the viewport and the new
  entries fill the rest of the screen below them. Delete `State.scrollToExpandedCardCmd`,
  its 50ms `setTimeout`, and the `Cmd.batch` in `ExpandCard`. No shape change to `Model`,
  `ExpandedItems`, or `Msg`.
- Drop `animate-fade-in-up` from the expanded surface. Today `chromeClass` gives it the
  same fade-in-up entrance as a freshly rendered card, which is the "fully expanded list
  fades in" the builder saw on the running app (2026-09-12) and rejected. The grown surface
  enters through the FLIP travel and the height grow only.
- Reduced motion is handled by the primitive (no-op `play`), nothing extra here.

## Acceptance criteria

- [ ] The existing `ExpandCard.test.fs` cases pass unmodified, except any assertion on `ExpandCard`'s scroll command, which now asserts the fetch is the only command (confirms no model-shape change).
- [ ] `State.fs` no longer contains `scrollToExpandedCardCmd` or a `setTimeout`; `ExpandCard` returns only `fetchExpandedItems`.
- [ ] Every item renderer used by an `expandable` spec applies `Motion.flipKey`; `npm run build` and a dev-server smoke run show no duplicate-key React warnings in the console.
- [ ] The expand-time scroll still brings the expanded card's top into view, now triggered from the layout effect — verified by code read (DOM scroll assertions are outside Vitest/jsdom's reach, same boundary as today's tests).
- [ ] `npm run build`, `npm test`, `npm run test:client` pass.
- [ ] Expanding a poster-rail card (e.g. Movies to Watch): the already-visible posters slide upward on screen to the top of the viewport, into their expanded grid positions, in roughly half a second, with no fade-in of a whole new list and no flash or jump; the posters that arrive from the unlimited fetch then fill the rest of the screen below them without disturbing the ones that moved. [human-eye]
- [ ] The expanded surface no longer carries `animate-fade-in-up` (code read; the class stays on collapsed cards).
- [ ] Expanding a list card that re-flows into tiles (e.g. Series Next Up): no text stretching or distortion during the transition. [human-eye]
- [ ] Collapse mirrors expand — items travel back and the card shrinks within the same ~0.5s budget. [human-eye]
- [ ] With `prefers-reduced-motion: reduce` emulated, expand and collapse show no travel; card and items are at their final positions immediately. [human-eye]
- [ ] Rapidly toggling expand/collapse before the animation settles leaves no item visibly stuck off its resting position. [human-eye]

## Notes

- ADR-0073 carries the technique decision and the finding that `Program.withReactSynchronous`
  commits on a concurrent `createRoot` (so nothing post-commit may hang off `dispatch`).
- Prior art is commit 74e1ab8 itself (no task file — it was built outside the board) and
  `intelligence-dq8rk`'s 3a layout that `tabArea` sits in. `LocalCopyRemovalDialog` is the
  precedent for keeping transient DOM/animation state out of the Elmish model.
- Recent Achievements cannot widen on expand (Steam keeps ten unlocks); its expand still
  animates the surface, with nothing new to append.
- `clip-path: inset()` is the documented escape hatch if animating the surface's `height`
  costs frames.
