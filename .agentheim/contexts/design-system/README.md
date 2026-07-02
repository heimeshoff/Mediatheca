# Design system

## Purpose
The **cross-cutting visual language** for Mediatheca's UI. Owns typography, color tokens, the dim theme, the glassmorphism overlay rules, Feliz/DaisyUI component patterns, and the in-app StyleGuide page. Gates frontend work in every BC.

## Classification
**supporting** — Custom-built, but its job is to *enable* the core BCs' UIs, not to differentiate the product.

## Actors
Single user, in a developer role.

## Ubiquitous language

- **Token** — a primitive design value (color, spacing, radius, opacity). Defined in `index.css` under the `dim` theme.
- **Theme** — currently a single dark theme called `dim`, selected by `data-theme="dim"` on `<html>` via DaisyUI 5's `@plugin "daisyui/theme"`. Palette replaced in place (2026-07-02, design-system-r7k2m) with the "Velvet Lobby" burgundy-black/gold palette — the theme keeps the name `dim`.
- **Glassmorphism** — the project's mandatory overlay style: semi-transparent background (0.55–0.70 opacity), `backdrop-filter: blur(24px) saturate(1.2)`, subtle border, top-edge highlight. Used by every dropdown, popover, modal, floating panel. Re-tinted to burgundy/gold (`.glass-card`, `.rating-dropdown`) alongside the Velvet Lobby palette; the rule itself is unchanged (ADR-0006).
- **Surface** — any element rendered above the page. Floating overlays (dropdowns, popovers, modals) follow the mandatory glassmorphism rules (§ 3.2, ADR-0006, unchanged). Non-overlay page/card surfaces use the solid **velvet card** treatment (§ 3.1, `.velvet-card` / `DesignSystem.velvetCard`) instead — shipped 2026-07-02, design-system-h3q8n. A narrower **media-chrome glass** (§ 3.3, `.media-chrome-glass` / `DesignSystem.mediaChromeGlass`, `blur(6px)`) exists for small controls floating directly over artwork — an addition alongside § 3.2, not a replacement.
- **Component pattern** — a recurring Feliz / DaisyUI combination. Reusable, BC-agnostic patterns (e.g. rating dropdown, catalog card, poster rail) live as **typed Feliz compositions directly in `DesignSystem.fs`** (resolved 2026-07-02, design-system-h3q8n — see Open questions below); component-specific chrome that needs its own React lifecycle (e.g. `ActionMenu`'s open/close state) still lives in `Components/`.
- **Motion primitive** — a keyframe/transition owned by design-system as *vocabulary*, not application: the gold-leaf sweep (`DesignSystem.goldLeafSweep`, reserved for "In focus" surfaces only), the leave-transition (`leaveTransition`/`leaveTransitionLeaving`, 400ms fade+collapse), and the cross-fade (`crossFade`, 200ms). *Where* these fire (a queue item leaving, a dashboard tab swap) is BC behavior — design-system ships the primitive, the owning BC wires the application. The spotlight gradient is deliberately static and has no motion primitive.
- **Lifecycle status vocabulary** — `DesignSystem.LifecycleStatus` (Backlog/InFocus/Playing/Completed/Abandoned/OnHold) is the status-badge pattern's own six-state vocabulary, matching the design brief. It is **not** the same as `Shared.GameStatus` (Backlog/InFocus/Completed/Abandoned/OnHold/Dismissed — no `Playing`, has `Dismissed`). This discrepancy surfaced during design-system-h3q8n and is tracked as a Games BC backlog item; mapping one onto the other (or keeping them deliberately distinct) is a Games BC decision, not design-system's.
- **StyleGuide page** — the live, in-app reference at `src/Client/Pages/StyleGuide` rendering every component pattern in situ.
- **Typography ("Velvet Lobby")** — Instrument Serif (`font-display`, display & titles, mixed case; *italic* is the section-header/wordmark voice) + Instrument Sans (`font-sans`, body/UI) + Spline Sans Mono (`font-mono`, dates/durations/counts/ids), loaded via self-hosted `@fontsource` packages. Replaced Oswald/Inter in place (2026-07-02, design-system-r7k2m); the forced-uppercase heading rule was retired — uppercase now signals only an eyebrow/data label.
- **Ink hierarchy** — four literal oklch text-color steps (`ink`, `ink-secondary`, `ink-muted`, `ink-faint`), minted as named Tailwind tokens in `index.css` and consumed via `DesignSystem.fs`'s `bodyText`/`secondaryText`/`mutedText`(`metaText`)/`faintText`. Replaces the legacy opacity-on-`base-content` approach.
- **Layered sidebar nav** (2026-07-03, design-system-t4b9k) — the desktop rail (`Components/Sidebar.fs`) splits items into a top group (Dashboard/Movies/TV Series/Games/Catalogs/Friends) and a bottom group (Events/Settings) pinned via `mt-auto`. The active item is its own raised **ivory** layer (`--color-nav-active-bg`, gold family — a deliberate in-palette compromise, not literal white and not the design doc's burgundy fill; see ADR-0013) with dark-burgundy ink and a gold icon, joined to the rail/content boundary by a **concave corner-notch** (`--nav-notch-size`, radial-gradient corner masks). `DesignSystem.navItemClass`/`navItemActiveIconClass`/`navGroupTop`/`navGroupBottom`; retires the old `.nav-glow` left-edge bar entirely.

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

**Resolved (2026-07-02, design-system-h3q8n):** component patterns migrate to `DesignSystem.fs` as typed Feliz compositions (not inline in pages) — see the Component pattern entry in Ubiquitous language above.
