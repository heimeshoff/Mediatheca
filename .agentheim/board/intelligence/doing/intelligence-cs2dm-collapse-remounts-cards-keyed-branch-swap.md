---
id: intelligence-cs2dm
title: Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4)
status: doing
type: bug
context: intelligence
created: 2026-09-12
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, expand-card, motion, animation, flip, react-reconciliation]
related_adrs: [0073, 0015, 0005]
related_research: []
prior_art: [intelligence-m09d4]
---

## Why

Builder's report on the running app, 2026-09-12, after `design-system-btmdx` shipped
(surviving FLIP items no longer fade):

1. *"When expanding, it works beautifully. When collapsing again, I can still see a fade
   out / fade in of the initial items."*
2. *"On the movies dashboard, when I expand the Recently Watched card and I collapse it
   again, then the content of the Recently Added seems to be [the] card from the Recently
   Watched in large. This is completely messed up, and the content of the original Recently
   Added is at the bottom of that card."*

Both come from one structural fault in `growingTabArea` (`src/Client/Pages/Dashboard/Views.fs`).
Its render has two shapes:

```
Expanded = None  →  div.flex-col        [ card0, card1, card2, … ]                 (content, flat)
Expanded = Some  →  div.grid-cols-1     [ div.invisible [ card0, card1, … ], surface ]
```

Neither branch gives its children a `prop.key`. React therefore reconciles the two shapes
**by index**: on expand, `card0`'s DOM node is repurposed as the `invisible` wrapper, `card1`'s
node becomes the grown surface, and `card2…` are unmounted; every collapsed card is then
*re-created* from scratch inside the wrapper. On collapse the reverse happens — the wrapper
node becomes `card0`, the surface node becomes `card1`, and `card2…` mount fresh.

So the premise ADR-0073 §3/§4 and the intelligence README's `Card grow` bullet state — "the
collapsed subtree stays mounted `invisible` underneath" — is **not what the code does**. The
collapsed cards are torn down and rebuilt on every expand *and* every collapse. Consequences:

- **Symptom 1 — collapse fades.** Each card's chrome is `chromeClass`, which carries
  `animate-fade-in-up` (`opacity 0→1, translateY 12px→0`, 0.4s, ADR-0073 §6a deliberately keeps
  it on *collapsed* cards as a mount entrance). Because collapse re-mounts the cards, that
  entrance replays on every one of them — the old content vanishes at commit and the rebuilt
  cards fade in. That is the "fade out / fade in" the builder sees. It is not the FLIP fade
  `design-system-btmdx` removed (that was per-item and is gone); it is a *mount* entrance
  firing on what should have been a still-mounted element. Expand looks right only because the
  same replay happens inside the `invisible` wrapper, where nobody can see it.
- **Symptom 2 — Recently Added shows Recently Watched's content.** On the Movies tab the
  content list is `[statsRow, row1Grid[RecentlyWatched, RecentlyAdded], row2Grid, …]`. When
  Recently Watched is collapsed, React maps the surface node (`div#dashboard-expanded-card`,
  children `[sectionHeader, itemsWrap[keyed poster tiles…]]`) onto `row1Grid` by index, so the
  surface's `itemsWrap` node — still holding the expanded poster grid — is repurposed as the
  **Recently Added** card, and Recently Added's own header + list are appended into it as new
  children. The `growSurface` height animation for the collapse then runs against
  `#dashboard-collapsed-card-MoviesRecentlyWatched`, which after the remap is the former
  `sectionHeader` node. The result the builder describes — Recently Watched's large tiles
  sitting in Recently Added's slot with Recently Added's real content below them — is exactly
  the shape this index-remap produces. Whether React's own child cleanup leaves the stale tiles
  in place or a live WAAPI handle / the pending height animation pins them is for the worker
  to confirm on the DOM (see Notes); the remap is the confirmed root and the fix below removes
  it regardless.

The fix is not to hunt each symptom; it is to make the ADR's premise true.

## What

Make `growingTabArea` render **one structure in both states**, with stable keys, so React
preserves the collapsed cards' DOM nodes across every expand and collapse and never
repurposes the surface node as a sibling card.

- **Always render the collapsed content inside one wrapper `div` carrying a stable
  `prop.key` (e.g. `"content"`).** In the `None` state it is visible; in the `Some` state it
  gets `invisible` + `aria-hidden` and the `col-start-1 row-start-1` placement, as today. The
  outer container may switch between `flex flex-col gap-4` and `grid grid-cols-1` (a className
  change on the same element is fine), or simply stay `grid grid-cols-1` throughout — worker's
  call; what matters is that the wrapper element and its children are the *same React
  elements* in both states.
- **Render the grown surface as a keyed sibling** (e.g. `prop.key "surface"`) only when
  `Expanded = Some`. With keys on both siblings React mounts/unmounts the surface on its own
  and leaves the content wrapper alone — no node ever crosses from one role to the other.
- **`expandable`'s `expanded` view must accept a key.** `CardHandle.ExpandedView` returns the
  surface `Html.div`; add the key there (or wrap it) so the key lands on the surface element
  itself, not on a fragment.
- **Leave the FLIP choreography as it is** — snapshot-on-click, one `useLayoutEffect`, the
  play lookup scoped to the visible face on expand and to `root` on collapse
  (`intelligence-m09d4`). One consequence to re-check, not change: with the collapsed cards
  now genuinely persisting, the *before* snapshot taken at collapse time contains every
  surviving key twice (hidden clone first in document order, visible surface second);
  `Motion.Flip.snapshot` builds its map with `Map.ofArray`, which keeps the **last** entry per
  key — the surface's box — which is the right "from" for the collapse travel. Note this in a
  comment where the snapshot is taken so the next reader does not "fix" it.
- **Keep `animate-fade-in-up` on `chromeClass`.** Once the cards stop remounting, the entrance
  fires only on first mount of the tab (its intended job, ADR-0073 §6a). Do not strip it as a
  workaround for symptom 1 — that would hide the bug rather than fix it.
- **README delta (intelligence, `Card grow` bullet):** the clause "since the collapsed
  subtree stays mounted `invisible` underneath" becomes true only with this task; amend the
  bullet to say the collapsed content wrapper and the grown surface are keyed siblings so the
  collapsed cards persist across the swap (this task's id), and that this is what lets the
  mount entrance stay on `chromeClass` without replaying on collapse.

Out of scope: `src/Client/Motion.fs` (design-system) needs no change — `snapshot`/`play`/
`growSurface` behave correctly once the DOM they act on stops being rebuilt. The
`Dashboard.State` model (`Expanded: ExpandedCard option`) is unchanged (ADR-0073 §3).

## Acceptance criteria

- [ ] In `growingTabArea`, the collapsed content wrapper `div` and the grown surface are siblings each carrying an explicit, distinct `prop.key`, and the content wrapper element is rendered in **both** the `Expanded = None` and `Expanded = Some` branches (only its visibility/placement classes differ).
- [ ] The collapsed cards' DOM nodes survive an expand and a subsequent collapse: with a dashboard card expanded and then collapsed, a `[data-flip-key]` element captured by reference before the expand is still the same node (`===`) and still attached (`isConnected`) afterwards — checked via DevTools console or a Playwright probe; a remount would give a new node.
- [ ] `src/Client/Motion.fs` is not modified by this task.
- [ ] `npm run build`, `npm test`, and `npm run test:client` pass.
- [ ] Collapsing any dashboard card: the cards that were on screen before the expand reappear in place with no fade-in-up entrance — items present in both views only travel, nothing else animates. Checked on the All tab and the Movies tab. [human-eye]
- [ ] Movies tab: expand Recently Watched, wait for the grow to finish, collapse. Recently Added shows only its own header and its own list, Recently Watched shows only its own poster rail, and both cards are their normal collapsed height. Repeat expanding Recently Added and collapsing; same result. [human-eye]
- [ ] Rapid expand/collapse toggling (clicking collapse mid-grow, and expand mid-shrink) leaves no card with stale content, stale height, or a stuck transform. [human-eye]

## Notes

- ADR-0073 §3 ("`tabArea` keeps the collapsed subtree mounted under `invisible`") and §4 are
  the design intent this task restores in code; the ADR text is correct as written and needs
  no amendment. If the worker finds it necessary to change *how* the two faces coexist (rather
  than just keying them), that is a §3/§4 amendment and belongs in the `ADRS` block.
- Confirm symptom 2's final mechanism on the DOM before fixing, so the Outcome records it:
  with the current code, expand Recently Watched on the Movies tab, collapse, then inspect
  `#dashboard-collapsed-card-MoviesRecentlyAdded` in DevTools — the expectation from the
  analysis above is that it is the former `itemsWrap` node and either still contains the
  `MoviesRecentlyWatched-*` poster tiles or carries a stale `height` from a WAAPI handle. Ten
  minutes, not a spike; the fix is the same either way.
- Why symptom 1 only appeared *now*: before `design-system-btmdx`, the per-item FLIP fade on
  size-changed survivors was layered over the same card-level mount entrance, so the two fades
  read as one blur. Removing the item fade made the card-level replay legible on its own.
- Prior art: `intelligence-m09d4` shipped `growingTabArea` (its iteration-1 verifier caught the
  hidden-clone `querySelector` issue; this is the sibling hazard on the React side of the same
  two-faces design). `design-system-btmdx` is the immediately preceding change on this surface.
- Styleguide gate: `depends_on: [design-system-001]` per the intelligence README's frontend
  rule; no new design-system vocabulary is needed.
