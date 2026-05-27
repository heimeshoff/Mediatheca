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

The **canonical, reviewable artifact** is [`styleguide.md`](styleguide.md) (produced by `design-system-001`). It consolidates the sources below and is the source of truth for the frontend task gate. Read it first.

Underlying sources it formalizes:
- `src/Client/index.css` — token definitions, dim theme, `.glass-card`, `.rating-dropdown`, etc. (authoritative for *values*).
- `src/Client/DesignSystem.fs` — typed Feliz/Tailwind class compositions used by components.
- `src/Client/Pages/StyleGuide` — the live, in-app reference page.
- `CLAUDE.md` § "Conventions" and "Gotchas" — glassmorphism rule + backdrop-filter trap (reproduced verbatim in `styleguide.md`; `CLAUDE.md` now points at the styleguide as canonical — ADR 0009).
- The `design-check` skill (`.claude/skills/design-check/`) — audits code against the system.

## Open questions

- Whether to introduce a light theme. Currently dim-only.
- Whether component patterns should migrate to a dedicated `DesignSystem.fs` module or stay inline in pages.
