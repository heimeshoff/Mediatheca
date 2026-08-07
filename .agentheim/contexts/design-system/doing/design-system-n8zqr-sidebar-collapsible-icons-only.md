---
id: design-system-n8zqr
title: Desktop sidebar rail collapses to icons only — a persisted manual toggle in the rail header, with a paper-overlay tooltip supplying each icon's label while collapsed
status: doing
type: feature
context: design-system
created: 2026-08-07
completed:
depends_on: [design-system-001, design-system-m2wvc]
blocks: []
tags: [sidebar, nav, layout, tooltip, paper-overlay, localstorage]
related_adrs: [0013, 0014, 0015, 0016]
related_research: []
prior_art: [design-system-t4b9k, design-system-grtw7, design-system-vk7rd]
---

## Why

The desktop rail is a fixed 256px column (`w-64`) on every page. On a laptop that is a
meaningful slice of a list page's width, and the labels stop earning their keep once you
know six destinations by their icons. The builder wants to reclaim that width on demand —
collapse the rail to an icons-only strip, expand it again when wanted, and have the app
remember the choice.

This is design-system work rather than a per-page concern: the rail is the shared chrome
every BC's frontend renders inside, and the collapsed state introduces a tooltip material
the system does not have yet.

## What

A manual, persisted collapse — no breakpoint magic, no hover-to-expand overlay (both were
considered and declined during capture).

**Toggle.** A control in the rail header, present in both states and keyboard-focusable.
Expanded it reads as "collapse" (chevron pointing at the rail edge); collapsed it reads as
"expand". It carries an `aria-label` that names the action for the current state.

**Collapsed rail.** Width drops from `w-64` to roughly `w-16`. Item labels are not
rendered, the tagline ("Where entertainment lives") is hidden, and the wordmark reduces to
the `Icons.mediatheca` mark alone — the "Media*theca*" lettering does not survive at 64px.
Icons optically centre in the strip; the current 12px (top group) / 11px (bottom group)
sizing may need to grow to read at that width — that is the worker's call, made against the
running rail.

**Tooltip.** While collapsed, hovering a nav item shows its label to the right of the rail
in a **paper-overlay** tooltip — opaque fill, line ring, elevation shadow, no translucency
(ADR-0016). This is a new, named composition in `DesignSystem.fs` + `index.css` with a
StyleGuide specimen, not an inline one-off: it is the system's first tooltip and other
surfaces will want it. Expanded, no tooltip fires — the label is already on screen.

**Persistence.** `localStorage`, key `mediatheca.sidebarCollapsed`. Read synchronously when
the sidebar first mounts so a collapsed rail never flashes expanded before settling. No
event, no server round-trip, nothing in the event store — this is a viewport preference,
not an observation of the user's engagement (ADR-0043's test).

**Out of scope.** Mobile is untouched: below `lg` the rail is `hidden` and `BottomNav`
owns navigation, so the toggle is unreachable there and no mobile behaviour changes.
`Components/Layout.fs`'s flex row needs no width compensation — the rail stays in flow, so
`main` reflows on its own.

## Acceptance criteria

- [ ] Playwright, desktop viewport (≥1024px): the rail renders a toggle control with an
      accessible name; activating it takes the rail's rendered width from ~256px to ~64px
      without a page reload.
- [ ] Playwright: while collapsed, no nav item label text is present in the rail's
      accessibility tree/DOM as visible text, and the tagline is not visible.
- [ ] Playwright: while collapsed, `main`'s bounding box left edge moves left by the width
      the rail gave up (no dead gutter), and the existing `min-w-0` overflow behaviour
      still holds — a horizontally scrolling poster row must not widen the page.
- [ ] Playwright: collapse the rail, reload the page, and the rail is still collapsed;
      `localStorage.getItem("mediatheca.sidebarCollapsed")` reflects the collapsed state.
      Expand it, reload, and it is expanded again.
- [ ] Playwright: with `localStorage` cleared (first-ever visit), the rail renders expanded.
- [ ] Playwright: while collapsed, hovering a nav icon reveals an element carrying the
      paper-overlay material with that item's label text, positioned to the right of the
      rail; while expanded, the same hover reveals no such element.
- [ ] The tooltip is a named composition in `src/Client/DesignSystem.fs` backed by a class
      in `src/Client/index.css` (paper-overlay material, ADR-0016 — opaque fill, no
      `backdrop-filter`), with a specimen rendered on the in-app StyleGuide page.
- [ ] Playwright: the active item's treatment is identical in both states — same computed
      background fill and same gold icon colour collapsed as expanded (design-system-m2wvc
      having already removed the inset-left bar).
- [ ] Playwright, both states, on a page taller than the viewport: the bottom group
      (Settings) stays inside the viewport when scrolled to top and does not move when
      scrolled to the bottom — design-system-vk7rd's viewport pinning is not regressed.
- [ ] Playwright, mobile viewport (<1024px): the rail is still hidden, `BottomNav` still
      renders, and no toggle control is present.
- [ ] `npm run build` exits 0 and the full Expecto suite is green.
- [ ] The BC README's "Layered sidebar nav" entry documents the collapsed state, the
      toggle, the `localStorage` key, and the tooltip composition.
- [ ] The width change reads as a smooth transition rather than a jump, and the collapsed
      strip looks balanced — icons optically centred, header mark and toggle not crowding
      each other. [human-eye]
- [ ] No expanded-then-collapsed flash on reload with a collapsed rail stored. [human-eye]

## Notes

- **Where the state lives.** Prefer local React state in the `Sidebar` component (a
  `[<ReactComponent>]` with `React.useState` seeded synchronously from `localStorage`)
  over threading a flag through the root Elmish model: nothing outside the rail reads it,
  and the BC README already sanctions components owning their own React lifecycle where
  they need one (`ActionMenu`'s open/close state is the precedent). If the collapsed flag
  later needs to be read elsewhere, promoting it to the root model is a cheap follow-up.
- **Declined during capture,** recorded so they don't get re-litigated: auto-collapse at a
  breakpoint (no user control), hover-to-expand-over-content (an overlay motion pattern the
  system doesn't have, and one that fights the tooltip), and non-persisted toggling (resets
  on every reload).
- **Depends on design-system-m2wvc** for more than sequencing: m2wvc removes the gold
  inset-left bar, which is the one active-state element that would have needed a separate
  collapsed-mode design. Both tasks edit `.nav-item-active`'s neighbourhood in `index.css`
  and the same `DesignSystem.fs` nav block, so serialising them also avoids a worktree
  conflict.
- **Second tooltip consumer already queued.** `intelligence-t8n3q` (captured the same day)
  wants a "Ctrl + K" hover tooltip on the dashboard's library-search control. Whichever
  task runs first should ship the shared composition and the other should consume it —
  do not land two tooltip implementations. This task is the one that names it a
  design-system composition with a StyleGuide specimen, so it is the natural owner.
- An ADR is plausible but not assumed — if the tooltip composition's material or placement
  rules end up being a real design decision (rather than a straight application of
  ADR-0016), write one and link it back here.
