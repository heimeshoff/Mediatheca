---
id: design-system-vk7rd
title: Sidebar bottom group (Admin/Settings) must pin to the bottom of the viewport, not the bottom of the document — the rail is `min-h-screen` and stretches with page content, so on any scrolling page the group sits below the fold
status: doing
type: bug
context: design-system
created: 2026-08-01
completed:
depends_on: [design-system-001]
blocks: []
tags: [sidebar, nav, layout, viewport, sticky]
related_adrs: [0014, 0015]
related_research: []
prior_art: [design-system-t4b9k, design-system-grtw7]
---

## Why

The desktop rail's bottom nav group (Admin, Settings) is supposed to sit at the foot of
the sidebar, pinned away from the primary destinations — that separation is what makes it
read as operator chrome rather than a seventh destination (design-system-t4b9k, "Layered
sidebar nav").

It does not. On any page taller than the viewport — which is nearly every list page — the
user has to scroll to the bottom of the *document* to reach Settings. The pin is against
the wrong reference frame: the group is pinned to the bottom of the page, when it should be
pinned to the bottom of the viewport.

## What

The mechanism is already almost right — `DesignSystem.navGroupBottom` carries `mt-auto`,
which correctly pushes the group to the foot of its flex column. What's wrong is the
column's height:

- `Components/Layout.fs:8` — the shell is `flex min-h-screen`, so its flex children stretch
  to the *content* height, not the viewport height.
- `Components/Sidebar.fs:59` — the rail is `hidden lg:flex flex-col w-64 min-h-screen …`.
  `min-h-screen` is a floor, not a ceiling; combined with the stretch above, the aside grows
  as tall as the tallest page content. `mt-auto` then dutifully pins the bottom group to the
  foot of *that* — the document — and it scrolls out of sight with everything else.

The fix is to make the rail a viewport-height, viewport-pinned column: `sticky top-0
h-screen` (a fixed height, not a minimum) in place of `min-h-screen`, so `mt-auto` resolves
against the viewport. Sticky is preferred over `fixed` because it keeps the rail in flow and
leaves `Layout.fs`'s flex row and the `main` column untouched — no width compensation
needed.

Short viewports need one more guard: with the rail's height now capped, the nav column must
scroll internally (`overflow-y-auto` on the `nav`) rather than clip the bottom group off the
end. The wordmark header stays fixed at the top of the rail.

Scope is desktop only (`lg:` and up). Mobile uses `BottomNav`, which is a separate surface
and already viewport-fixed — do not touch it.

## Acceptance criteria

- [ ] `Components/Sidebar.fs`'s `Html.aside` is viewport-height and viewport-pinned
      (`sticky top-0 h-screen` or equivalent), not `min-h-screen` — the rendered element's
      height equals the viewport height on a page whose content is taller than the viewport.
- [ ] Playwright, desktop viewport (≥1024px wide), on a page tall enough to scroll: with the
      page scrolled to the very top, the "Admin" and "Settings" nav links' bounding boxes are
      fully inside the viewport.
- [ ] Playwright, same page scrolled to the bottom: the "Admin" and "Settings" links'
      viewport-relative positions are unchanged from the scrolled-to-top measurement (the
      rail stays put rather than scrolling with the document).
- [ ] On a viewport too short to fit all nav items, the bottom group remains reachable —
      the rail's `nav` scrolls internally rather than clipping the group off the bottom.
- [ ] The `main` column's layout is unchanged: no horizontal gap appears beside the rail, and
      the existing `min-w-0` overflow behavior (a horizontally scrolling poster row must not
      widen the page) still holds.
- [ ] `npm run build` exits 0.
- [ ] Nothing else about the rail changed visually — wordmark, tagline, group spacing, the
      bottom group's smaller scale, and the dir-3a burgundy active tab all look as before.
      [human-eye]

## Notes

- `mt-auto` on `DesignSystem.navGroupBottom` is correct and stays — this is a height/pinning
  bug in the rail's own box, not a bug in the nav-group composition. Prefer not to touch
  `DesignSystem.fs` unless a class genuinely belongs there.
- Prior art: design-system-t4b9k introduced the top/bottom split and the `mt-auto` pin;
  design-system-grtw7 reverted the active-tab treatment to dir 3a (ADR-0014). Neither
  addressed the rail's height, so the pin has been against the document since t4b9k shipped.
- The BC README's "Layered sidebar nav" entry describes the bottom group as "pinned via
  `mt-auto`" — worth amending to name the viewport-height requirement, so the next person
  reading it doesn't reproduce the same half-fix.
- No ADR expected: this is a layout defect, not a decision. If sticky turns out to be
  unworkable and `fixed` + a `main` offset is needed instead, that *is* a decision worth an
  ADR.
