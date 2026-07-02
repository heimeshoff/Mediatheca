---
id: design-system-t4b9k
title: Layered sidebar nav — white active tab, curved-corner boundary
status: backlog
type: feature
context: design-system
created: 2026-07-02
completed:
depends_on: []
blocks: []
tags: [navigation, sidebar, glassmorphism, layout]
related_adrs: [0009]
related_research: []
prior_art: [design-system-h3q8n]
---

## Why
The desktop sidebar (`src/Client/Components/Sidebar.fs` + `DesignSystem.navItemClass`)
currently renders a flat list of eight items with a subtle gold-tinted active state
(`bg-primary/10 text-primary`). The "Mediatheca Directions" design doc pushes the
navigation toward a more deliberate, **layered** treatment: a distinct top group of
primary destinations, a bottom group pinned to the foot of the rail, and a much
stronger active-item affordance. This is the app's primary wayfinding surface and it
should read as intentional Velvet Lobby chrome, not a default DaisyUI menu.

## What
Rework the desktop sidebar's visual language and item grouping to match the design
direction, with the three specifics the user called out on top of it:

1. **Layered menu.** The rail reads as stacked depth rather than a flat list —
   the active item sits on its own raised/recessed layer distinct from the rail
   surface behind it (per the design doc's sidebar treatment).
2. **White active tab.** The highlighted (active) nav item is a white / light
   surface — a stronger, higher-contrast active affordance than today's
   `bg-primary/10 text-primary`. (Note the palette tension below — this is a
   deliberate departure from the burgundy/gold active state and should be
   confirmed against the Velvet Lobby palette during refinement/review.)
3. **Curved-corner left boundary.** The boundary on the left side (rail ↔ content
   edge) curves *around the corners* of the active item — the inverted-corner /
   notched treatment where the active tab appears to join the content panel and
   the panel's edge sweeps concavely around the tab's top and bottom corners.
4. **Top / bottom split.** Primary destinations (Dashboard, Movies, TV Series,
   Games, Catalogs, Friends) stay grouped at the **top**; **Events** and
   **Settings** are pinned to the **bottom** of the rail. In the design doc this
   is done with `margin-top:auto` on the bottom group — the app's flat single
   `<ul>` in `Sidebar.fs` needs to split into a top group and an auto-pushed
   bottom group.

## Acceptance criteria
- [ ] Desktop sidebar splits its items into a top group (Dashboard, Movies, TV
      Series, Games, Catalogs, Friends) and a bottom group (Events, Settings)
      pinned to the foot of the rail; visual order otherwise preserved.
- [ ] The active nav item renders as a white / light-surfaced tab, clearly
      distinct from the rail behind it (the "layered" affordance).
- [ ] The active item's left boundary uses the curved / inverted-corner treatment
      against the content edge (boundary curves around the active tab's corners).
- [ ] Inactive items and hover states keep a coherent hierarchy with the new
      active treatment (no orphaned `bg-primary/10` styling left behind).
- [ ] The new nav pattern is added to the design-system canonical artifacts:
      `DesignSystem.fs` (`navItemClass` / a new layered-nav composition), the
      styleguide (`styleguide.md`) and the live StyleGuide page reflect it.
- [ ] `npm run build` compiles clean; the sidebar renders correctly at `lg`+.

## Notes
- **Design source:** "Mediatheca Directions" doc (claude.ai/design project
  `c19616ce-55b9-482a-8146-5d13f0fe6484`, file `Mediatheca Directions.dc.html`).
  Read it with the `DesignSync` tool (`get_file`) — it is **not** WebFetch-able.
  The relevant sidebar markup is direction **3a** ("Velvet Lobby — desktop
  dashboard"): a `216px` dark rail (`oklch(0.14 0.025 20)`), a top nav group,
  and a bottom group with `margin-top:auto` holding Events + Settings + profile.
  Direction 3a's active item uses a burgundy fill + gold inset-left bar
  (`box-shadow:inset 2px 0 0 oklch(0.8 0.12 82)`) — the user's "white tab +
  curved boundary" is a stronger override on top of that, likely drawn from the
  pasted reference image in the doc (`uploads/pasted-1782989208456-0.png`;
  couldn't be decoded here — worker should view it via DesignSync during work).
- **Palette tension to resolve in refinement/review:** a pure-white active tab is
  a real departure from the just-shipped Velvet Lobby burgundy-black/gold palette
  (design-system-r7k2m / -h3q8n). Confirm with the user whether "white" means a
  literal white surface or a light warm/ivory tint consistent with the palette,
  and whether the ink flips to a dark color on the active tab. This is the main
  reason the task is in `backlog` rather than `todo`.
- **Glassmorphism caveat (ADR-0006 / `CLAUDE.md`):** the rail itself uses
  `backdrop-blur-sm`. If the curved active surface or any nested overlay needs
  its own `backdrop-filter`, mind the nested-`backdrop-filter` trap — render as a
  sibling, not a child of the blurred rail.
- **Curved-corner technique:** the inverted-corner effect is typically done with
  radial-gradient or pseudo-element "corner masks" (two concave notches above and
  below the active tab), not a plain `border-radius`. Worth a small spike/decision
  during work on how to express it in Tailwind 4 / DesignSystem.fs.
- **Styleguide gate:** this changes the design language, so it lands in the
  design-system backlog and is reviewed with the user before implementation
  (per the BC README). It self-gates — no external `depends_on`.
- Mobile uses `BottomNav.fs`, not this rail — out of scope unless the design doc's
  mobile direction (2c) is explicitly pulled in later.
