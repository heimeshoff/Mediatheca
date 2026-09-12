---
id: design-system-btmdx
title: Surviving FLIP items must never fade — drop `FadeIn` from `Motion.fs` so an item present in both the collapsed and expanded view only ever translates (ADR-0073 §1 amended)
status: todo
type: bug
context: design-system
created: 2026-09-12
completed:
depends_on: []
blocks: []
tags: [motion, animation, flip, dashboard, expand-card]
related_adrs: [0073, 0015, 0064]
related_research: []
prior_art: [design-system-m2v88]
---

## Why

Builder's report on the running app, verbatim: *"When expanding or collapsing, the games
that remain in both views, or the movies or TV series, sometimes fade in or out. The ones
that remain shouldn't fade, they should just move."*

Root cause, confirmed in `src/Client/Motion.fs` (shipped by `design-system-m2v88`):
`Flip.plan` sets `FadeIn = true` whenever an item's width **or** height changed by
≥ 0.5px, and `Flip.play` then layers `opacity: 0 → 1` over the full `growDurationMs`
(500ms) on exactly those items.

The trap is that `plan` only ever returns keys present in **both** snapshots — an item that
genuinely appears or disappears produces no move at all and is never animated. So the fade's
only possible audience is the *surviving* items, which are precisely the set that should read
as one continuous motion. Any poster whose tile geometry shifts even slightly between the
rail and the grid (gap/column math differs) trips `sizeChanged` and starts from fully
transparent — which is why it reads as "sometimes": it fires per item, on whichever items
happen to change size.

The fade came from ADR-0073 §1, where it was meant to mask a list row's internal reflow at
the instant its box resizes. The builder has now seen that running and rejected it. **ADR-0073
§1 and §9 are already amended in place** (2026-09-12, this capture) — the decision is settled;
this task implements it.

This was predicted. The iteration-1 verifier on `design-system-m2v88` flagged it as an
eye-check note rather than a criterion: *"`Flip.play` runs the `FadeIn` opacity `0 → 1` over
the full `growDurationMs` … every tile in this specimen changes size, so the builder sees that
fade on all four."*

## What

Remove the fade from the motion primitive outright — do not threshold it, do not make it
opt-in. A surviving item only ever translates.

- **`FlipMove` loses its `FadeIn` field** (`src/Client/Motion.fs`). The record becomes
  `{ Key; Dx; Dy }`.
- **`Flip.play`'s keyframes become translate-only, unconditionally** — the ternary on
  `move.FadeIn` in the `emitJsExpr` call goes away, leaving the single
  `translate(dx,dy) → none` keyframe pair. `fill: "none"`, `growDurationMs`, `growEasing`
  all unchanged.
- **`Flip.plan` loses the `sizeChanged` computation and its threshold carve-out.** With no
  fade, a size-only change with no position delta has nothing left to animate, so it should
  be **dropped from the plan** like any other sub-threshold move: the rule collapses back to
  the plain one the criterion always described — *both axes under 0.5px ⇒ no move*. Delete
  the "A size-only change (no position change) still needs to fade in…" comment with it; it
  exists only to justify the carve-out.
- **`Motion.test.fs`**: the two `FadeIn` assertions are rewritten, not deleted — "equal-size
  boxes … `FadeIn = false`" becomes a plain translate assertion, and "a size change yields
  `FadeIn = true`" inverts into the new rule (a pure resize with no position delta produces
  no move). The other six cases stand unchanged.
- **StyleGuide "Grow Transition" specimen caption** (`src/Client/Pages/StyleGuide/Views.fs`):
  drop any claim that size-changed items fade.
- **design-system README, `Motion primitive` bullet**: the `FadeIn = true whenever a box's
  measured size changed … so a re-flowed item fades rather than stretches` clause is now
  false — replace it.

Out of scope: `src/Client/Pages/Dashboard/**` needs no change — the dashboard never reads
`FadeIn`. The intelligence README's `Card grow` bullet needs no change either: it already
says "not a subtree swap-and-fade" and never claimed survivors fade.

## Acceptance criteria

- [ ] `FlipMove` has no `FadeIn` field, and `Flip.play` emits one translate-only keyframe pair with no opacity channel, for every move, unconditionally.
- [ ] `Flip.plan` no longer computes `sizeChanged`; a move is dropped exactly when both axes are under the 0.5px threshold, with no size-based carve-out, and the comment justifying that carve-out is gone.
- [ ] `src/Client/Motion.test.fs` still has 8 passing cases: the two `FadeIn` assertions are rewritten (not dropped) so one asserts a plain translate and the other asserts a pure resize with no position delta produces no move.
- [ ] No occurrence of `FadeIn` remains anywhere under `src/Client/` (grep).
- [ ] `npm run build`, `npm test`, and `npm run test:client` pass.
- [ ] Neither the design-system README's `Motion primitive` bullet nor the StyleGuide "Grow Transition" caption claims that a size-changed item fades.
- [ ] Expanding and collapsing a dashboard card: every item present in both views only moves — no item fades in or out at either end. Checked on a poster rail (Movies to Watch), a list card that re-flows into tiles (Recently Finished), and a games card. [human-eye]
- [ ] The StyleGuide "Grow Transition" specimen's four tiles travel without fading. [human-eye]

## Notes

- ADR-0073 §1 and §9 are **already amended in place** as part of this capture — read them
  first; the worker does not need to write or amend an ADR for this task. §1 now records the
  removal and *why* (a change of intent on seeing the real thing, not a correction of a
  mistake — the masking rationale was sound in the abstract and simply lost to what it cost);
  §9 records that `plan` no longer owns a fade rule and that the threshold carve-out went with
  it.
- The consumer, `intelligence-m09d4`, is already shipped and needs no change. Its README bullet
  and its `Views.fs` wiring never referenced `FadeIn`.
- Removing a record field is a compile-error-driven change: `npm run build` will point at every
  remaining reference, so there is no silent-miss risk here (unlike the forgotten-`flipKey`
  failure mode ADR-0073 warns about).
- What is deliberately **not** being solved: the reflow the fade was masking. If a list row
  re-flowing into a grid tile turns out to visibly snap its internal layout, that is a separate
  observation to capture on its own evidence — not a reason to reinstate a fade on every
  surviving item.
