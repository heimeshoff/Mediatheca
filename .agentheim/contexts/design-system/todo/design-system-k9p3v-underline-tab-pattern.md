---
id: design-system-k9p3v
title: Underline tab pattern — reusable DesignSystem component (dir 3a header tabs)
status: todo
type: feature
context: design-system
created: 2026-07-06
completed:
depends_on: [design-system-001]
blocks: [intelligence-dq8rk]
tags: [tabs, nav, velvet-lobby, 3a, frontend]
related_adrs: [0014]
related_research: []
prior_art: [design-system-grtw7, design-system-t4b9k]
---

## Why
Direction **3a** in `Mediatheca Directions.html` replaces filled-pill tab chrome with **text
tabs carrying a gold/orange underline** under the active tab. The Dashboard All-tab rework
(`intelligence-dq8rk`) is the first consumer, but tab-strips are shared vocabulary — the same
treatment will recur on any future tabbed surface. Rather than styling it inline on the
dashboard, this task promotes the underline tab to a **reusable DesignSystem component**, the
same call the user made for the 3a sidebar (design-system-grtw7 / ADR-0014). The dashboard then
*consumes* the shared pattern instead of owning bespoke tab CSS.

## What
Add an **underline tab** treatment to the design system — a text tab that shows the active tab
with a **Velvet Lobby secondary/gold underline** and no filled-pill background — expose it as a
typed composition in `DesignSystem.fs` (+ any CSS tokens/rule it needs in `index.css`), and
render it as a specimen on the live in-app **StyleGuide** page. Model the API on the existing
nav compositions (`navItem`/`navItemActive`/`navItemInactive` in `DesignSystem.fs`): a small set
of class strings (e.g. `underlineTab` / `underlineTabActive` / `underlineTabInactive`, or a
single helper taking `isActive`) that a caller applies to its own `Html.button`s — the component
owns the *look*, the caller owns the tab list and click wiring.

The current dashboard tab strip (`tabBar`, `src/Client/Pages/Dashboard/Views.fs:54`) is filled
pills (`bg-base-300/40` container, `bg-primary/15 … border-primary/30` active). It is the
reference for the "before" and the first caller to migrate — but the actual re-point of the
dashboard header happens in `intelligence-dq8rk`; this task delivers the shared pattern + specimen.

## Acceptance criteria
- [ ] A reusable underline-tab composition exists in `src/Client/DesignSystem.fs` (naming consistent with the existing `navItem*` family), backed by whatever tokens/CSS it needs in `src/Client/index.css`.
- [ ] Active tab is a **text label with a gold/secondary underline** (the Velvet Lobby secondary/gold accent — reuse the existing gold token, do not introduce a new colour), inactive tabs are muted text; **no filled-pill / bordered-button chrome**.
- [ ] Hover and active states are defined for the inactive→active affordance, consistent with the design system's existing interaction language.
- [ ] The pattern is rendered as a **specimen on the live StyleGuide page** (`src/Client/Pages/StyleGuide/Views.fs`) with active + inactive tabs shown.
- [ ] The design-system README's ubiquitous language gains an entry for the underline-tab pattern (term + where it lives), consistent with how other component patterns are recorded.
- [ ] `npm run build` is clean.
- [ ] No consumer is required to change in *this* task — the dashboard re-point is `intelligence-dq8rk`'s job. (If a trivial dashboard migration is convenient it may land here, but it is not required and not this task's gate.)

## Notes
- **Reference:** direction **3a** in `Mediatheca Directions.html` (repo root), the dashboard header tab strip. The underline uses the Velvet Lobby **secondary/gold** accent.
- **Precedent:** the 3a **sidebar** work (design-system-grtw7, ADR-0014) is the model for "a 3a nav treatment promoted into the shared system." Follow the same shape: tokens/CSS in `index.css`, typed compositions in `DesignSystem.fs`, a StyleGuide specimen, a README ubiquitous-language entry.
- **Worker latitude — ADR?** grtw7 wrote a superseding ADR only because it *reversed* an earlier decision. This is a net-new additive pattern with nothing to supersede, so an ADR is likely unnecessary — write one only if a genuine cross-cutting decision surfaces (e.g. the gold-underline accent choice merits recording). Don't force one.
- **Consumer waiting on this:** `intelligence-dq8rk` (Dashboard All-tab 3a layout) `depends_on` this task and will re-point `tabBar` onto the shared pattern once it lands.
