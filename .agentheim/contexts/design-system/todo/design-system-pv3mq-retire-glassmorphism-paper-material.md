---
id: design-system-pv3mq
title: Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)
status: todo
type: refactor
context: design-system
created: 2026-07-03
completed:
depends_on: []
blocks: []
tags: [glassmorphism, overlays, surface, tokens, adr-0006, velvet-lobby]
related_adrs: [0006, 0015]
related_research: []
prior_art: [design-system-h3q8n, design-system-sg8kd]
---

## Why

Glassmorphism is no longer the material Mediatheca uses. The user's read: the
overlays are "a paper-style material design at best" — the semi-transparent,
`backdrop-filter: blur()` treatment enshrined as the project's *mandatory* overlay
style (ADR-0006, CLAUDE.md § Conventions) no longer describes what the UI actually
is or wants to be. The Velvet Lobby re-skin already moved non-overlay surfaces to a
solid **velvet card** (`design-system-h3q8n`); glassmorphism now survives only as
the overlay vocabulary (§ 3.2) and the small media-chrome glass (§ 3.3), and the
user wants that gone too. Keeping a "mandatory glassmorphism" rule on the books while
the design has moved on makes the design system lie to every frontend task that reads
the gate.

## What

Retire glassmorphism as the design system's overlay vocabulary and replace it with a
solid **paper / material** treatment (opaque surface + elevation shadow, no
translucency, no `backdrop-filter`). This is a full retirement across the living
system and its governing artifacts — not just a StyleGuide edit.

Surfaces to change (code):
- `src/Client/DesignSystem.fs` — `glassCard`, `glassOverlay`, `glassSubtle`,
  `glassDropdown`, `mediaChromeGlass`, `modalPanel` (built on `glassOverlay`), and
  the `§ 3.3 media-chrome glass` block. Replace with the paper/material composition(s).
- `src/Client/index.css` — `.glass-card`, `.rating-dropdown`, `.media-chrome-glass`,
  the `--glass-*` custom properties, and the ~24 glass/backdrop references.
- Consuming call sites: `Components/ActionMenu.fs`, `Components/EntryList.fs`,
  `Components/ContentBlockEditor.fs`, `Pages/Dashboard/Views.fs`,
  `Pages/{MovieDetail,SeriesDetail,GameDetail,Settings}/Views.fs` — every dropdown,
  popover, modal, and floating panel restyled to the paper material.
- `src/Client/Pages/StyleGuide/{Types,Views}.fs` — remove the glassmorphism section
  and its specimens; add the replacement material's specimen(s) so the gate stays honest.

Governing artifacts to repoint (docs / decisions):
- **ADR-0006** (global, "TailwindCSS 4 + DaisyUI 5 with mandatory glassmorphism for
  overlays") — write a superseding ADR (bidirectional `superseded_by`), following the
  `sg8kd`→ADR-0015 / `grtw7`→ADR-0014 precedent (superseding ADR is worker output,
  written in lockstep with the code removal so the ADR never desyncs from shipped code).
- `CLAUDE.md` § Conventions ("Glassmorphism for all overlays" bullet) and § Gotchas
  (the `backdrop-filter` nesting trap — likely removable once no overlay blurs).
- `.agentheim/context-map.md` — the Design system context entry (core language lists
  "glassmorphism, backdrop-filter, surface").
- `.agentheim/knowledge/index.md` — the design-system BC description + the ADR-0006 line.
- `contexts/design-system/README.md` — the **Glassmorphism**, **Surface**, and related
  ubiquitous-language entries; the Existing assets note.
- `.claude/skills/design-check/` (`design-rules.md`) — the glassmorphism audit rules.

## Acceptance criteria

- [ ] No `backdrop-filter` / `backdrop-blur` / translucent-overlay styling remains in
      `DesignSystem.fs`, `index.css`, or any page/component view (grep-clean).
- [ ] All dropdowns, popovers, modals, and floating panels render as the agreed paper /
      material surface (opaque fill + elevation shadow) and are legible over any backdrop.
- [ ] The StyleGuide page's glassmorphism section is gone and replaced by the new
      overlay-material specimen(s); the page compiles and renders.
- [ ] `npm run build` is clean (Fable compiles, no dead CSS, no dangling `.glass-*` refs).
- [ ] ADR-0006 is superseded by a new ADR (bidirectional link); CLAUDE.md, context-map,
      index, README, and the design-check skill no longer prescribe glassmorphism.
- [ ] The `design-check` skill audits against the new material, not glassmorphism.
- [ ] The `backdrop-filter` nested-element gotcha is removed from CLAUDE.md if no overlay
      relies on backdrop-filter anymore (or retained with a note if one edge case survives).

## Notes

**Open question — the replacement material (resolve in refine; likely a small ADR
decision baked into the superseding ADR-0006 replacement):** what exactly is "paper /
material"?
- **(a) Paper / solid elevation** — solid opaque surface (velvet-card-like fill) lifted
  with an elevation shadow; distinct overlay vocabulary from page chrome.
- **(b) Reuse `.velvet-card` exactly** — one surface vocabulary for both page chrome and
  overlays (surface bg + line ring + shadow), no separate overlay material.
- The user's words ("paper style material design at best") lean toward (a) — a genuine
  paper elevation with shadow — but this is the user's call. Confirm before work.

**Prior art / dependencies:**
- `design-system-h3q8n` (Velvet Lobby component patterns) introduced `.velvet-card`,
  `.media-chrome-glass`, and the surface split (§ 3.1/§ 3.2/§ 3.3) this task collapses.
  Read it — the replacement material should relate cleanly to `velvet-card`.
- `design-system-sg8kd` (retire styleguide.md → ADR-0015) is the pattern to follow for a
  "retire X + supersede its ADR + repoint CLAUDE.md/README/design-check" change.

**Scope note:** captured as full retirement per the user's definitive framing ("we don't
use glassmorphism anymore"). If a later review wants a phased retirement (docs first,
code later), split this task at refine time.

This is a design-system task itself, so it carries no styleguide-gate `depends_on`.
Because it rewrites shared overlay vocabulary consumed by many BCs' views, prefer running
it as an isolated single-task batch (no sibling frontend work in flight) to avoid churn.
