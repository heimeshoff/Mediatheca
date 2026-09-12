---
id: design-system-m2v88
title: Grow / shared-element FLIP motion primitive — a new `Motion.fs` with a pure `Flip.plan`, a WAAPI `snapshot`/`play`/`growSurface` shell, `flipKey`, a reduced-motion gate, `--duration-grow` (0.5s) / `--ease-grow` tokens, and a StyleGuide specimen (ADR-0073)
status: todo
type: feature
context: design-system
created: 2026-09-12
completed:
depends_on: []
blocks: [intelligence-m09d4]
tags: [motion, animation, flip, styleguide, dashboard, tokens]
related_adrs: [0073, 0015, 0064]
related_research: []
prior_art: [design-system-h3q8n, design-system-bky6v]
---

## Why

The dashboard's card-expand action (commit 74e1ab8, intelligence) currently swaps two React
subtrees: the collapsed card vanishes and the expanded card appears at full size in the same
frame. The builder wants a real grow — the card reaches full size in roughly half a second
and the items that were already on screen visibly travel to their expanded positions.

Per the design-system motion doctrine (README, "Motion primitive"), design-system ships the
motion *vocabulary* and the owning BC wires the application. No shared-element / FLIP
primitive exists yet; the closest vocabulary is `leaveTransition` (400ms) and `crossFade`
(200ms). ADR-0073 settles the technique: hand-rolled, key-based FLIP played through WAAPI,
not the View Transitions API. This task ships that primitive; `intelligence-m09d4` wires it
into the dashboard.

## What

Add `src/Client/Motion.fs` (compiled before `DesignSystem.fs`, or as a section of it — the
worker picks; the public surface below is what matters):

- `Box` (viewport-coordinate `Left`/`Top`/`Width`/`Height`, plain `getBoundingClientRect`
  values — the caller settles any scroll *before* the after-snapshot, so the travel that
  plays is the on-screen journey including the scroll shift) and `FlipMove`
  (`Key`, `Dx`, `Dy`, `FadeIn: bool`) records.
- **Pure** `Flip.plan : Map<string, Box> -> Map<string, Box> -> FlipMove list` — the
  intersection of the two key maps only (keys present in just one map are ignored), a
  sub-pixel threshold (moves under 0.5px in both axes are dropped), `FadeIn = true` when
  the box's width or height changed (so a list row re-flowing into a grid tile fades
  rather than stretches — translate-only, never `scale`).
- DOM shell: `Flip.snapshot : Browser.Types.Element -> Map<string, Box>` reading every
  `[data-flip-key]` descendant via `getBoundingClientRect` (viewport coordinates, no scroll
  offset added — see `Box`);
  `Flip.play : Browser.Types.Element -> FlipMove list -> Animation list` inverting each
  move with `Element.animate` (`translate(dx,dy)` → `none`, optional opacity `0 → 1` over
  the cross-fade duration, `fill: "none"`, `--duration-grow`/`--ease-grow`); `growSurface`
  animating a surface's measured `height` old→new with `overflow: hidden`. Callers keep the
  returned `Animation` handles so they can `cancel()` on re-entry.
- `Motion.flipKey : string -> IReactProperty list` (or equivalent) that emits `prop.key` and
  `data-flip-key` together from one value, so the React key and the FLIP key cannot drift.
- `Motion.prefersReducedMotion : unit -> bool` (`matchMedia`) and `play`/`growSurface`
  short-circuiting to no-ops when it is true — items are simply already at their final
  positions (the same freeze-at-final-state stance as `.gold-sweep`, design-system-bky6v).
- Tokens in `index.css`: `--duration-grow: 0.5s` and `--ease-grow` (a standard ease-out /
  `cubic-bezier`, worker's call), mirrored as F# constants `Motion.growDurationMs` /
  `Motion.growEasing` with a comment on both sides naming the mirror (WAAPI cannot read a
  CSS custom property without a `getComputedStyle` round trip).
- A "Grow transition" specimen on the in-app StyleGuide page (ADR-0015 gate): a handful of
  toy tiles that toggle between a compact and a grown layout using the primitive.

## Acceptance criteria

- [ ] `src/Client/Motion.test.fs` (Fable.Mocha under Vitest, ADR-0064) covers `Flip.plan`: equal-size boxes yield a pure translate with `FadeIn = false`; a size change yields `FadeIn = true`; before-only and after-only keys produce no move; a sub-threshold move (< 0.5px both axes) is dropped; the plan's length never exceeds `min(|before|, |after|)`.
- [ ] `npm run build`, `npm test`, and `npm run test:client` pass with the new module and test file included in `Client.fsproj`.
- [ ] `--duration-grow` and `--ease-grow` exist in `index.css`; `Motion.growDurationMs` and `Motion.growEasing` exist with the same values, and a comment on each side names the mirror.
- [ ] `Motion.flipKey` sets both `prop.key` and `data-flip-key` from one value (a unit test renders or inspects the property list).
- [ ] The StyleGuide "Grow transition" specimen: toggling it makes the tiles visibly and smoothly travel to their grown positions in roughly half a second; with `prefers-reduced-motion: reduce` emulated, they snap with no travel. [human-eye]

## Notes

- Rationale, rejected alternatives (View Transitions API, CSS-only size transition, a
  layout-animation library, before-rects in the Elmish model) and the `withReactSynchronous`
  / `createRoot` finding live in ADR-0073 — read it first.
- WAAPI-driven, not class-toggling: `fill: "none"` leaves no inline-style residue, so no
  cleanup step. Translate-only to avoid distorting text whose box changed size.
- `Flip.plan` is the pure core and carries every judgement (threshold, fade rule,
  intersection). `snapshot`/`play`/`growSurface` touch `getBoundingClientRect`,
  `Element.animate`, `matchMedia` and are deliberately untested — same division as
  `LocalCopyRemovalDialog`'s pure phase machine over an untested async shell.
- Open judgement calls best settled on the running page, not here: whether the surface's
  height grow should lead the item travel by a few tens of milliseconds or share one curve;
  whether `Flip.plan` needs an output cap beyond the natural `LIMIT n` intersection bound
  (ship without).
- Prior art is curated, not matcher-scored: `design-system-h3q8n` shipped the existing
  motion vocabulary (leave-transition, cross-fade); `design-system-bky6v` set the
  reduced-motion freeze stance.
- Consumer: `intelligence-m09d4`. Any later two-subtree swap elsewhere (a detail-page tab, a
  re-flowing list) can reuse this without new vocabulary.
