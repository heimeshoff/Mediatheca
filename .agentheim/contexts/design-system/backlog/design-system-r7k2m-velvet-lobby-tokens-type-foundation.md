---
id: design-system-r7k2m
title: Velvet Lobby re-skin — tokens & type foundation
status: backlog
type: feature
context: design-system
created: 2026-07-02
completed:
depends_on: []
blocks: [design-system-h3q8n]
tags: [styleguide, tokens, typography, theme, re-skin, velvet-lobby]
related_adrs: [0006, 0009]
related_research: []
prior_art: [design-system-001]
---

## Why

The current visual language is a generic DaisyUI "dim" dark theme (Oswald + Inter,
neutral graphite tokens). A design exploration ("Mediatheca design brief", Claude Design
project `c19616ce-…`, file *Mediatheca Directions.dc.html*) converged on a warm, cinematic
editorial identity — **Velvet Lobby** — that gives the app a distinctive point of view fitting
a "where entertainment lives" media library. The user selected **Velvet Lobby (warm)** with
**variant 3a** (the desktop dashboard) as the reference screen.

Because the styleguide gates every frontend task in every BC (ADR-0009), the re-skin lands
here first as a token + type foundation. Component patterns follow in [[design-system-h3q8n]].

## What

Replace the foundation layer of the design system — palette tokens and typography — with the
Velvet Lobby direction, wired as Tailwind 4 `@theme` tokens and reflected in the canonical
`styleguide.md` and the in-app StyleGuide page. This is the "System board (3d)" of the brief.

**Palette (oklch) — from the 3d system board:**

| token       | oklch          | role                          |
|-------------|----------------|-------------------------------|
| `bg`        | `.16 .028 20`  | app background (burgundy-black)|
| `surface`   | `.20 .03 22`   | cards / raised surfaces       |
| `line`      | `.32 .04 28`   | borders / hairlines           |
| `gold`      | `.80 .12 82`   | accent (the single brand hue) |
| `spotlight` | `.30 .06 30`   | radial top glow behind main   |
| `ink`       | `.93 .012 60`  | primary text                  |

Muted text steps observed in 3a: `oklch(.74 .015 45)`, `oklch(.62 .02 40)`, `oklch(.55 .03 40)`.
The main region carries a static `radial-gradient(90% 42% at 50% -4%, oklch(.30 .06 30 / .85), transparent 70%)` spotlight.

**Typography — replaces Oswald + Inter entirely:**
- **Instrument Serif** — display & titles; *italic* is the signature voice for section headers ("Next up", "In focus") and the "theca" wordmark.
- **Instrument Sans** — body, labels, UI. Weights 400–700.
- **Spline Sans Mono** — dates, durations, counts, ids (the "data" typeface). New role, no current equivalent.

## Acceptance criteria

- [ ] `src/Client/index.css` `@theme` block: `--font-display` → Instrument Serif, `--font-sans` → Instrument Sans, and a new `--font-mono` → Spline Sans Mono; Google Fonts `<link>` (or import) updated to load all three, Oswald/Inter removed.
- [ ] Palette tokens (`bg`, `surface`, `line`, `gold`, `spotlight`, `ink` + muted text steps) expressed as Tailwind 4 `@theme` / DaisyUI theme values using the oklch values above; the `dim` theme's neutral graphite palette is replaced (decide replace-in-place vs new theme name — see Notes).
- [ ] `DesignSystem.fs` typed class compositions updated so the new font roles and surface/line/accent tokens are referenced by name, not hardcoded.
- [ ] The in-app StyleGuide page (`src/Client/Pages/StyleGuide`) renders the new palette swatches (with oklch labels), the three-typeface type scale, and the italic-serif section-header voice.
- [ ] `styleguide.md` (canonical, ADR-0009) updated: token table, typography section, and theme description reflect Velvet Lobby; the doc remains the source of truth for the gate.
- [ ] Glassmorphism overlays (ADR-0006) re-tinted to the burgundy/gold palette and still pass the glass rule (`.glass-card`, `.rating-dropdown` reference the new tokens); no fully-opaque overlays introduced.
- [ ] `npm run build` compiles clean (Fable + Tailwind), app boots with the new theme applied via `data-theme`.

## Notes

- **Reference:** Claude Design project `c19616ce-55b9-482a-8146-5d13f0fe6484`, file *Mediatheca Directions.dc.html*. Turn 3 (Velvet Lobby full hi-fi set), option **3a** (desktop dashboard) is the chosen reference; **3d** is the token/system board this task implements. Read via the `DesignSync` tool (`get_file`), not WebFetch (the `/design/` URL 403s).
- **Open decision — glassmorphism coexistence.** The brief is almost entirely solid cinematic surfaces + gradients and shows no overlays, yet ADR-0006 mandates glassmorphism for every dropdown/modal/popover. Resolve during refinement: keep the glass rule but re-parameterize its tint to the new palette (recommended — least disruptive), or amend ADR-0006. This is the main thing gating promotion to `todo/`.
- **Open decision — theme replace vs. add.** Overwrite the `dim` theme values in place (keeps `data-theme="dim"` everywhere) or introduce a new theme name (e.g. `velvet`) and switch the `<html>` attribute. Replace-in-place is simpler and avoids touching every page; new-name is cleaner if a light mode or the cool "Modern recolor" variant (turn 4) might later coexist.
- **Deferred variant.** Turn 4 offered a cool "Modern recolor" (graphite hue 260 + electric-lime/amber accent). Not chosen; recorded here in case a second theme is wanted later. The brief's "try next" also floats a cyan accent and a light-mode pass — out of scope for this task.
- **Accessibility check** during work: gold text (`.80 .12 82`) on burgundy `bg` and on `surface`, plus muted text steps, should meet contrast for their sizes.
- Component patterns (hero card, filmstrip row, segmented progress, status badges, gold-leaf sweep, star rating) are **not** in this task — they are [[design-system-h3q8n]], which depends on these tokens.
