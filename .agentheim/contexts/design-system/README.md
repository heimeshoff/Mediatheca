# Design system

## Purpose
The **cross-cutting visual language** for Mediatheca's UI. Owns typography, color tokens, the dim theme, the glassmorphism overlay rules, Feliz/DaisyUI component patterns, and the in-app StyleGuide page. Gates frontend work in every BC.

## Classification
**supporting** — Custom-built, but its job is to *enable* the core BCs' UIs, not to differentiate the product.

## Actors
Single user, in a developer role.

## Ubiquitous language

- **Token** — a primitive design value (color, spacing, radius, opacity). Defined in `index.css` under the `dim` theme.
- **Theme** — currently a single dark theme called `dim`, selected by `data-theme="dim"` on `<html>` via DaisyUI 5's `@plugin "daisyui/theme"`.
- **Glassmorphism** — the project's mandatory overlay style: semi-transparent background (0.55–0.70 opacity), `backdrop-filter: blur(24px) saturate(1.2)`, subtle border, top-edge highlight. Used by every dropdown, popover, modal, floating panel.
- **Surface** — any element rendered above the page; surfaces follow glassmorphism rules.
- **Component pattern** — a recurring Feliz / DaisyUI combination (e.g. the rating dropdown, the catalog card, the rail of posters).
- **StyleGuide page** — the live, in-app reference at `src/Client/Pages/StyleGuide` rendering every component pattern in situ.
- **Typography** — Oswald (`font-display`, headings) + Inter (`font-sans`, body), loaded from Google Fonts.

## Aggregates

No domain aggregates. The design system is content + rules, not behavior.

## Key events / commands

None. This BC produces UI artifacts (CSS tokens, Feliz components, documentation), not events.

## Relationships with other contexts

- **Open host / shared kernel for:** every frontend-bearing BC (Movies, Series, Games, Journal, Friends, Curation, Intelligence, Integration, Administration). All of them conform to design-system tokens and patterns.

## The styleguide gate (load-bearing)

**Every frontend / UI task in any BC must `depends_on` the design-system styleguide task** (currently [`design-system-001-formalize-styleguide`](todo/design-system-001-formalize-styleguide.md)). The styleguide is reviewed and signed off by the user before any BC implements UI against it. Refer captures of frontend tasks back here.

When the styleguide changes (new token, new pattern, retired pattern), the change goes through this BC's backlog so the gate stays meaningful.

## Existing assets (mature project)

The design system already exists in the running app:
- `index.css` — token definitions, dim theme, `.glass-card`, `.rating-dropdown`, etc.
- `CLAUDE.md` § "Conventions" and "Gotchas" — the glassmorphism rules and backdrop-filter nesting trap.
- `src/Client/DesignSystem.fs` (if present) — shared Feliz components.
- `src/Client/Pages/StyleGuide` — live reference.
- The `design-check` skill — audits code against the system.

The first task in this BC's backlog (formalize-styleguide) consolidates these into a single reviewable `styleguide.md` so the gate has a canonical artifact.

## Open questions

- Whether to introduce a light theme. Currently dim-only.
- Whether component patterns should migrate to a dedicated `DesignSystem.fs` module or stay inline in pages.
