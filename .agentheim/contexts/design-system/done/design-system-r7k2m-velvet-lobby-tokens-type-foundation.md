---
id: design-system-r7k2m
title: Velvet Lobby re-skin — tokens & type foundation
status: done
type: feature
context: design-system
created: 2026-07-02
completed: 2026-07-02
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

- [x] `src/Client/index.css` `@theme` block: `--font-display` → Instrument Serif, `--font-sans` → Instrument Sans, and a new `--font-mono` → Spline Sans Mono; Google Fonts `<link>` (or import) updated to load all three, Oswald/Inter removed.
- [x] Palette tokens (`bg`, `surface`, `line`, `gold`, `spotlight`, `ink` + muted text steps) expressed as Tailwind 4 `@theme` / DaisyUI theme values using the oklch values above; the `dim` theme's neutral graphite palette is **replaced in place** — the theme keeps the name `dim` and `data-theme="dim"` stays on `<html>` (resolved — see Notes), so no page's theme attribute changes.
- [x] `DesignSystem.fs` typed class compositions updated so the new font roles and surface/line/accent tokens are referenced by name, not hardcoded.
- [x] The in-app StyleGuide page (`src/Client/Pages/StyleGuide`) renders the new palette swatches (with oklch labels), the three-typeface type scale, and the italic-serif section-header voice.
- [x] `styleguide.md` (canonical, ADR-0009) updated: token table, typography section, and theme description reflect Velvet Lobby; the doc remains the source of truth for the gate.
- [x] Glassmorphism overlays (ADR-0006) re-tinted to the burgundy/gold palette and still pass the glass rule (`.glass-card`, `.rating-dropdown` reference the new tokens); no fully-opaque overlays introduced.
- [x] `npm run build` compiles clean (Fable + Tailwind), app boots with the new theme applied via `data-theme`.

## Notes

- **Reference:** Claude Design project `c19616ce-55b9-482a-8146-5d13f0fe6484`, file *Mediatheca Directions.dc.html*. Turn 3 (Velvet Lobby full hi-fi set), option **3a** (desktop dashboard) is the chosen reference; **3d** is the token/system board this task implements. Read via the `DesignSync` tool (`get_file`), not WebFetch (the `/design/` URL 403s).
- **Resolved (2026-07-02) — glassmorphism coexistence → keep the rule, re-tint.** ADR-0006's mandatory glassmorphism stays in force; the re-skin only re-parameterizes the glass *tint* (`.glass-card`, `.rating-dropdown`) to the burgundy/gold palette. No ADR-0006 amendment, no relaxation of the "no fully-opaque overlay" rule — lowest blast radius (ADR-0006 is `scope: global` and `design-check` enforces it across every BC). The brief's solid cinematic surfaces are *page/card* backgrounds, not floating overlays, so there's no genuine conflict. Reflected in the glassmorphism acceptance criterion. *(Default applied while user away — re-confirm if desired; amending ADR-0006 to allow solid structural panels remains a possible later decision task.)*
- **Resolved (2026-07-02) — theme replace vs. add → replace `dim` in place.** Overwrite the `dim` theme's values with the Velvet Lobby oklch tokens; keep the name `dim` and `data-theme="dim"` on `<html>`. Simplest, touches no page's theme attribute. *(Default applied while user away — re-confirm if desired.)* The trade-off: no coexisting-theme path without a later rename — acceptable because a light mode and the cool "Modern recolor" variant (turn 4) are both out of scope for v1 (recorded in the "Deferred variant" note below and the BC README's light-theme open question). If either is later wanted, a follow-up task introduces a named theme then.
- **Deferred variant.** Turn 4 offered a cool "Modern recolor" (graphite hue 260 + electric-lime/amber accent). Not chosen; recorded here in case a second theme is wanted later. The brief's "try next" also floats a cyan accent and a light-mode pass — out of scope for this task.
- **Accessibility check** during work: gold text (`.80 .12 82`) on burgundy `bg` and on `surface`, plus muted text steps, should meet contrast for their sizes.
- Component patterns (hero card, filmstrip row, segmented progress, status badges, gold-leaf sweep, star rating) are **not** in this task — they are [[design-system-h3q8n]], which depends on these tokens.

## Outcome

Shipped the Velvet Lobby token + type foundation. The `dim` DaisyUI theme was replaced **in place** (name/`data-theme` unchanged) with the burgundy-black/gold palette (`base-100/200/300` = surface/bg/deep-rail, `base-content` = ink, `primary/secondary/accent` = gold family, `neutral` = line); `--color-line` and `--color-spotlight` were minted directly since they have no DaisyUI slot; the four ink-hierarchy steps became literal oklch tokens (`--color-ink-secondary/-muted/-faint`) instead of opacity fractions. Fonts swapped Oswald/Inter → Instrument Serif / Instrument Sans / Spline Sans Mono via `@fontsource` packages (`App.fs`, `package.json`); the global forced-uppercase heading rule was removed from `index.css`. `DesignSystem.fs`'s type-scale helpers were retargeted to the new fonts/ink tokens, keeping the existing `bodyText`/`secondaryText`/`mutedText`/`faintText` ladder while adding the brief's literal role names (`eyebrow`, `metaText`, `dataText`) as aliases. `.glass-card` and `.rating-dropdown` (including item hover/active states) were re-tinted to the burgundy/gold palette — ADR-0006's mandatory glassmorphism rule for overlays was kept in full force, not relaxed; `glassOverlay`/`glassSubtle` re-tint automatically via the `base-100` change. The StyleGuide page's Typography section now shows the three-typeface scale, an explicit italic-voice specimen, and the new `dataText` role; the Colors section adds an oklch-labeled "Velvet Lobby Primitives" swatch set and an oklch-labeled ink-hierarchy table. `styleguide.md` was reconciled with the shipped code — most importantly § 3 was corrected: the pre-existing draft had described overlays (dropdowns/modals) converting to solid "velvet" surfaces, which conflicted with the resolved gating decision and ADR-0006; it now correctly states overlays stay mandatory glass (re-tinted) while only page/card backgrounds trend toward a solid "velvet card" (deferred, not implemented, tracked under design-system-h3q8n). Spacing/radii/shadow/animation tokens and all § 4 component patterns remain explicitly out of scope and marked "target — not yet implemented." `npm run build` compiles clean (Fable + Tailwind); confirmed the new utility classes (`bg-gold`, `text-ink-secondary`, `bg-line`, italic) are present in the generated CSS.

Key files: `src/Client/index.css`, `src/Client/App.fs`, `src/Client/DesignSystem.fs`, `src/Client/Pages/StyleGuide/Views.fs`, `package.json`, `.agentheim/contexts/design-system/styleguide.md`, `.agentheim/contexts/design-system/README.md`, `CLAUDE.md`.
