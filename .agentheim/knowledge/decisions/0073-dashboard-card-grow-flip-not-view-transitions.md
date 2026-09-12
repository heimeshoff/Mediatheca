---
id: 0073
title: Dashboard card grow animates with key-based FLIP in a view-layer shell, not the View Transitions API
scope: global
status: accepted
date: 2026-09-12
supersedes: []
superseded_by: []
related_tasks: [design-system-m2v88, intelligence-m09d4]
related_research: []
---

# ADR 0073: Dashboard card grow animates with key-based FLIP in a view-layer shell, not the View Transitions API

## Context

`intelligence` shipped expandable dashboard cards (commit 74e1ab8): an expand button
grows a query-backed card over the whole tab area and re-runs its query without the row
limit. `Views.fs`'s `expandable` builds two entirely separate React subtrees
(`CollapsedView` / `ExpandedView`) stacked in one CSS grid cell (`col-start-1 row-start-1`);
`tabArea` keeps the collapsed subtree mounted under `invisible` on purpose, so the page
keeps its height and scroll position instead of the other cards unmounting underneath.
Expanding therefore *swaps* which subtree is visible. Nothing moves.

The ask is a genuine grow: the card reaches full size in ~0.5s and the items already on
screen visibly travel from their collapsed positions to their expanded ones. The limited
(tab) list is a `LIMIT n` prefix of the same ordered query as the unlimited one, so the
two sets overlap by construction and every overlapping item has a stable domain key.

Two constraints shape the answer.

**The runtime is three engines, not one.** ADR-0018 ships this codebase as a Photino
desktop shell (WebView2/Chromium on Windows, WKWebView/WebKit on macOS) *and* as a Docker
deployment opened in whatever browser the user has. Same-document View Transitions are in
Chromium and in WebKit 18+, and only recently in Firefox — any VT-based solution needs a
capability check plus a fallback, i.e. the fallback gets written anyway.

**`Program.withReactSynchronous` does not commit in the dispatch call stack.**
`Fable.Elmish.React` 4.0.0's `react.fs` computes
`useRootApi = int ReactBindings.React.version.[..1] >= 18`, which is true here, so
`withReactSynchronousUsing` takes the `ReactDomClient.createRoot` branch and calls
`root.render`. On a concurrent root that only *schedules*; the commit lands in a later
microtask. "Synchronous" in the name means only "no `requestAnimationFrame` batching",
in contrast to `withReactBatchedUsing`. The `Fable.Core.JS.setTimeout ... 50` inside
`State.scrollToExpandedCardCmd` is that fact showing through: a 50ms guess standing in
for "after React commits". Any post-commit measurement or transition trigger must be
anchored to React's own commit phase, not to dispatch.

## Decision

1. **Technique: key-based FLIP, translate-only, played through WAAPI `Element.animate`.**
   Measure a `key -> box` map before the state change, re-measure after commit, animate
   each surviving key from `translate(dx, dy)` to `none` over `--duration-grow`.
   - **Translate-only, no `scale`.** Poster tiles are the same fixed width in both
     layouts, so translate is exact for them; list rows re-flowing into grid tiles do
     change width, and a FLIP scale there would visibly stretch text. Items whose box
     size changed instead get a short opacity fade on top of the travel, reusing the
     existing 200ms cross-fade vocabulary.
   - **WAAPI, not CSS class toggling.** Default `fill: "none"` means the element returns
     to its untransformed state with no cleanup step, no inline-style residue, and no
     forced reflow between "invert" and "play". In-flight `Animation` handles are held in
     a ref and `cancel()`ed on re-entry, so spam-clicking expand/collapse cannot leave a
     stray transform behind.
   - **Boxes are measured in document coordinates** (`rect.left + window.scrollX`,
     `rect.top + window.scrollY`), so a concurrent smooth scroll cannot skew the delta.
   - **The card box itself** animates its measured `height` (old px -> new px) with
     `overflow: hidden`, a single layout-animated element whose children's transforms stay
     independent of it. If that ever costs frames, the escape hatch is a compositor-only
     `clip-path: inset()` reveal.

2. **Rejected: the View Transitions API.** See Alternatives.

3. **No Elmish model change.** `Expanded: ExpandedCard option` already encodes everything
   the animation needs. `ExpandedItems` gains no `Expanding`/`Collapsing` phase, and
   `Dashboard.State.update` stays pure and fully covered by the existing
   `ExpandCard.test.fs` cases. The transient animation state (the before-snapshot, the
   live `Animation` handles) is DOM-derived and lives in a `React.useRef` inside a new
   `[<ReactComponent>]` wrapper around `tabArea` — the same precedent as `ActionMenu`'s
   and `Sidebar`'s view-only state staying out of the model (ADR-0043's test: a viewport
   concern is not an observation).

4. **One `useLayoutEffect` covers both directions.** Expand and collapse are the same
   operation — "`Expanded` changed and a before-snapshot exists, so play the plan". This
   works *because* FLIP is keyed on domain identity rather than element identity: the
   collapsed and expanded subtrees are different DOM trees with no shared element to
   persist, and `data-flip-key` is what connects an old rect to a new element across the
   swap.

5. **`ExpandedItemsLoaded` does not animate.** The unlimited fetch appends items in order
   below what is already on screen; they appear without motion. Only `Expanded`
   transitioning `None <-> Some` animates.

6. **`scrollToExpandedCardCmd` is retired from `State.fs`.** The scroll moves into the
   same layout effect, ahead of `play`, where React guarantees it runs post-commit. This
   deletes the 50ms guess rather than racing it.

7. **The vocabulary/application split follows the design-system motion doctrine.**
   design-system owns: the `--duration-grow` / `--ease-grow` tokens (mirrored as F#
   constants, since WAAPI cannot read a CSS custom property without a `getComputedStyle`
   round trip — the mirroring is commented on both sides), the pure
   `Motion.Flip.plan`, the `snapshot`/`play`/`growSurface` DOM shell, the
   `Motion.flipKey` helper that emits `prop.key` and `data-flip-key` together so the two
   cannot drift, the reduced-motion gate, and a StyleGuide specimen (ADR-0015 gate).
   intelligence owns: which cards participate, the `flipKey` call on each item renderer,
   the component wrapper and its effect, the snapshot-on-click, and the scroll retiming.

8. **`prefers-reduced-motion: reduce` short-circuits `play` in JS**, so items are simply
   already at their final positions. This is the project's established freeze-at-the-final-
   state pattern (`.gold-sweep` / `.in-focus-frame`, design-system-bky6v) reached by
   construction rather than by a `@media` override — the feature still works, only the
   motion is gone.

9. **Testability line: the arithmetic is pure, the DOM is shell.**
   `Motion.Flip.plan : Map<string, Box> -> Map<string, Box> -> FlipMove list` takes plain
   records, is unit-tested under Vitest/jsdom (ADR-0064) with no layout engine involved,
   and owns every judgement in the feature: sub-pixel threshold, "size changed, so also
   fade", keys only in the before map (ignored), keys only in the after map (ignored).
   `snapshot`/`play`/`growSurface` touch `getBoundingClientRect`, `Element.animate` and
   `matchMedia` and are deliberately untested — the same division as
   `LocalCopyRemovalDialog`'s pure phase machine over an untested async shell.

## Consequences

### Positive
- Identical behaviour on all three engines ADR-0018 ships into, with no capability check
  and no second code path to keep alive.
- The animated set is the *intersection* of the two key maps, which is structurally
  bounded by the collapsed card's `LIMIT n` in both directions — at most a dozen or two
  animations, never one per row of an unlimited list.
- `update` stays pure; the existing card-expansion test suite is untouched. The only new
  tests are pure arithmetic.
- The `50ms` timing guess in `State.fs` is deleted rather than inherited.
- The primitive is generic: any other BC with a two-subtree swap (a list page re-flowing,
  a detail-page tab) can reuse `Motion.Flip` without new vocabulary.

### Negative
- More code than a working View Transitions call would be — roughly 120 lines of
  `Motion.fs`, of which the DOM half is untestable in this harness.
- `tabArea`'s consumer becomes a `[<ReactComponent>]` to hold hooks; `expandable`'s
  `ExpandedView` stays a plain function, but the wrapper is a new indirection in an
  already dense view file.
- A WAAPI transform animation outranks CSS for its duration, so `.card-hover`'s
  `transform: scale(1.02)` is suppressed on an item while it travels (~0.5s). Accepted.
- Animating the surface's `height` runs layout every frame for one element. Accepted, with
  `clip-path` documented as the escape hatch.
- Every new expandable item renderer must remember `Motion.flipKey`. Forgetting it fails
  silently (the item just doesn't travel) rather than loudly.

### Neutral
- Item renderers gain `prop.key`, which they should have had anyway.

## Alternatives considered

- **View Transitions API** (`document.startViewTransition` + per-element
  `view-transition-name`) — rejected on three counts, any one of which would have been
  enough. (a) **Duplicate names abort the transition.** `tabArea` deliberately keeps the
  collapsed subtree mounted under `invisible`; `visibility: hidden` elements are still
  rendered for VT purposes, so every item's name would appear twice and the browser skips
  the transition entirely. Stripping names conditionally is the same per-item plumbing
  FLIP needs, for less control. (b) **VT morphs bitmap snapshots.** Fine for fixed-width
  posters; for the list-rows-into-grid-tiles layout it scales the old snapshot to the new
  box and visibly stretches the text. (c) **It still needs `ReactDOM.flushSync`** inside
  the transition callback, because `withReactSynchronous` commits on a concurrent root
  (see Context) — coupling the animation to React's scheduler and flushing the whole app
  synchronously mid-click, with Elmish `Cmd`s firing inside that window. Plus: the
  document is inert for the transition's duration, which makes expand/collapse spam and
  the concurrent smooth scroll worse, and browser coverage needs a fallback anyway.
- **Size-only CSS transition on the card box, items just reflow** — a few lines, but it is
  the instant item swap the builder explicitly rejected. Kept as the shape the
  reduced-motion path degrades *past* (reduced motion animates nothing at all).
- **A layout-animation library (Motion / framer-motion)** — solves it off the shelf, at
  the cost of a JS-first component model that fights Feliz/Elmish composition, a
  dependency with no second consumer in this codebase, and bundle weight for one
  interaction. Revisit only if shared-element motion becomes pervasive across BCs.
- **Putting the before-rects in the Elmish model** — rejected outright: DOMRects are
  view-layer, frame-scoped, and would make `update` both impure and untestable, for zero
  gain over a ref.

## References

- `src/Client/Motion.fs` (new), `src/Client/Motion.test.fs` (new)
- `src/Client/index.css` (`--duration-grow`, `--ease-grow`), `src/Client/DesignSystem.fs`
- `src/Client/Pages/Dashboard/Views.fs` (`expandable`, `tabArea`), `.../State.fs`
- `src/Client/fable_modules/Fable.Elmish.React.4.0.0/react.fs` — the `useRootApi` /
  `createRoot` fact in Context
- ADR-0005 (Elmish MVU), ADR-0015 (StyleGuide gate), ADR-0018 (Photino desktop shell /
  engine set), ADR-0064 (Vitest+Fable client unit tests)
