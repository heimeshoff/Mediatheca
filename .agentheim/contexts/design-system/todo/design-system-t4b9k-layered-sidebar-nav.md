---
id: design-system-t4b9k
title: Layered sidebar nav — ivory active tab, curved-corner boundary
status: todo
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
2. **Ivory active tab.** The highlighted (active) nav item is a warm **ivory**
   surface (`oklch(0.94 0.02 75)`, in the Velvet Lobby gold family) with the ink
   flipped to **dark burgundy** (`oklch(0.20 0.03 25)`) and the icon in **gold**
   (`oklch(0.55 0.16 55)`). A stronger, higher-contrast active affordance than
   today's `bg-primary/10 text-primary` — reads as a lit lobby placard — while
   staying in-palette rather than breaking to literal white. (Palette tension
   resolved with the user 2026-07-02 — see Notes; the "white" the user asked for
   is realised as this warm ivory tint, not `#fff`.)
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
- [ ] The active nav item renders as a warm **ivory** tab (`oklch(0.94 0.02 75)`)
      with **dark-burgundy** ink (`oklch(0.20 0.03 25)`) and a **gold** icon
      (`oklch(0.55 0.16 55)`), clearly distinct from the rail behind it (the
      "layered" affordance). Not literal `#fff` — the warm tint stays in the
      Velvet Lobby palette.
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
- **Palette tension — RESOLVED (2026-07-02, refinement with user present):** the
  active tab is a warm **ivory** surface (`oklch(0.94 0.02 75)`, Velvet Lobby gold
  family), **not** literal `#fff`; ink flips to **dark burgundy**
  (`oklch(0.20 0.03 25)`), icon stays **gold** (`oklch(0.55 0.16 55)`). Reads as a
  lit lobby placard while staying in-palette. Grounding: the captured design doc's
  dir 3a active item is actually a *burgundy* raised layer
  (`background:oklch(0.22 0.035 25)` + gold `inset 2px 0 0 oklch(0.8 0.12 82)`
  bar), **not** white — the white-tab ask came only from the pasted reference
  image (`uploads/pasted-1782989208456-0.png`), and the user confirmed it should
  land as the ivory tint above, not the doc's burgundy layer. These exact oklch
  values are a starting point — worker should mint them as named tokens in
  `index.css` and expose via `DesignSystem.fs`, and may nudge lightness/chroma to
  sit right against the shipped rail, keeping the ivory-in-gold-family intent.
- **Glassmorphism caveat (ADR-0006 / `CLAUDE.md`):** the rail itself uses
  `backdrop-blur-sm`. If the curved active surface or any nested overlay needs
  its own `backdrop-filter`, mind the nested-`backdrop-filter` trap — render as a
  sibling, not a child of the blurred rail.
- **Curved-corner technique — stays a worker spike (confirmed in refinement,
  2026-07-02):** the inverted-corner effect is typically done with radial-gradient
  or pseudo-element "corner masks" (two concave notches above and below the active
  tab), not a plain `border-radius`. Worker chooses the technique during work and
  writes an ADR **only if** the approach turns out non-obvious / worth pinning; no
  separate decision task, no v1 descope — the concave-corner criterion below is in
  scope for this task.
- **Styleguide gate:** this changes the design language, so it lands in the
  design-system backlog and is reviewed with the user before implementation
  (per the BC README). It self-gates — no external `depends_on`.
- Mobile uses `BottomNav.fs`, not this rail — out of scope unless the design doc's
  mobile direction (2c) is explicitly pulled in later.
