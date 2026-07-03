---
id: 0006
title: TailwindCSS 4 + DaisyUI 5 with mandatory glassmorphism for overlays
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: [0016]
related_tasks: []
related_research: []
---

# ADR 0006: TailwindCSS 4 + DaisyUI 5 with mandatory glassmorphism for overlays

> Backfill — the visual language stack and the load-bearing overlay rule.

## Context

The UI needs to look distinctive without becoming a custom CSS project. The author wanted utility-first CSS for velocity, semantic components for consistency, and a strong dark-mode aesthetic befitting a personal media diary.

Mediatheca's visual identity is built around **glassmorphism** for any floating surface (dropdowns, popovers, modals, panels). The look is opinionated and consistent application is what carries it — one accidentally opaque dropdown breaks the spell.

## Decision

- **TailwindCSS 4** for utility-first styling.
- **DaisyUI 5** for semantic component classes layered on top of Tailwind.
- A **custom `dim` dark theme** declared in `index.css` via `@plugin "daisyui/theme"`, selected by `data-theme="dim"` on `<html>`.
- **Glassmorphism is mandatory** for every overlay surface: dropdown, popover, modal, floating panel. Concretely: `/0.55`–`/0.70` background opacity, `backdrop-filter: blur(24px) saturate(1.2)`, subtle `oklch(... / 0.15)` border, and `inset 0 1px 0 0 oklch(100% 0 0 / 0.08)` top-edge highlight. See `.rating-dropdown` and `.glass-card` in `index.css` for reference implementations.
- **No fully opaque overlay surfaces.** This is a "no exceptions" rule; if a surface needs to be readable over noisy content, raise opacity within range or darken the underlying layer — never solidify the overlay.

The `design-check` skill enforces these rules; the [[design-system]] BC formalizes them.

## Consequences

### Positive
- Coherent visual identity that ties the whole app together.
- Fast iteration: most styling decisions are inline utility classes.
- DaisyUI's semantic classes prevent a flood of one-off class compositions.
- The glassmorphism rule is concrete enough to lint mechanically.

### Negative
- `backdrop-filter` is the source of a real gotcha — nested `backdrop-filter` elements only blur their parent's content, not the page behind it. Documented in `CLAUDE.md`; workaround is to render overlays as siblings to their blurred parent, not children.
- Tailwind 4 + DaisyUI 5 are recent; occasional ecosystem rough edges.
- Performance: `backdrop-filter` is GPU-heavy. On older devices, many simultaneous glass surfaces can stutter.
- A11y: low-contrast glass surfaces need careful foreground choices to remain readable.

### Neutral
- vite-plugin-fable / Vite 6 pairing is version-pinned (CLAUDE.md gotcha). Tied to this stack but not specific to it.

## Alternatives considered

- **Plain Tailwind, no DaisyUI** — would have meant rebuilding common component patterns by hand. DaisyUI's tokens align well with the custom theme.
- **Material UI / shadcn-style component library** — would have forced their visual conventions onto an F#/Feliz project; integration cost high, design ceiling lower.
- **Optional glassmorphism (opt-in per surface)** — rejected because consistency *is* the design. The "mandatory" rule is what gives the visual identity its bite.

## References

- `index.css` — token + theme + overlay class definitions.
- `CLAUDE.md` § "Conventions" (glassmorphism rules) and § "Gotchas" (backdrop-filter nesting).
- `src/Client/Pages/StyleGuide` — live reference.
- `design-check` skill.
