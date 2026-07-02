---
id: design-system-h3q8n
title: Velvet Lobby re-skin — component patterns & motion
status: backlog
type: feature
context: design-system
created: 2026-07-02
depends_on: [design-system-r7k2m]
blocks: []
tags: [styleguide, components, patterns, motion, re-skin, velvet-lobby]
related_adrs: [0006, 0009]
related_research: []
prior_art: [design-system-001, design-system-003]
completed:
---

## Why

Once the Velvet Lobby tokens & type foundation ([[design-system-r7k2m]]) lands, the recurring
component patterns need to be re-expressed in the new cinematic language so BC frontends have
canonical specimens to conform to. The design brief's full hi-fi set (turn 3: dashboard 3a,
game detail 3b, movies grid 3c) defines these patterns concretely.

## What

Re-skin / add the Velvet Lobby component patterns as **typed Feliz compositions in
`DesignSystem.fs`**, render them as specimens on the in-app StyleGuide page, and document them
in `styleguide.md`. These are the patterns the brief repeats across 3a/3b/3c and codifies in
the 3d system board. Kept as **one cohesive component pass** (not split) — every specimen shares
the same StyleGuide-page + `styleguide.md` destination, so they land together.

**Patterns (from the brief):**
- **Cinematic hero card** — backdrop gradient with bottom scrim, In-focus badge top-left, title in Instrument Serif, watched-with avatar stack, segmented progress, rating, gold "▶ Watch" pill.
- **Filmstrip movie row** — black strip with sprocket-hole perforations top & bottom, poster gradients inset, titles + runtime/"rec. by" below.
- **Secondary media card** — compact poster-top card with serif title, "Next: SxEy" line, segmented progress.
- **In-focus poster frame (3c)** — the reusable gold-frame treatment applied to any poster/card flagged In-focus in the movies grid; the visual sibling of the "In focus" badge. Codify it here so every BC's grid renders In-focus the same way.
- **Status badges** — game lifecycle mapped to the palette: Backlog (outline `line`), **In focus** (animated gold-leaf sweep), Playing (gold outline), Completed (green `~.7 .1 150`), Abandoned (red `~.62 .09 25`), On hold (blue `~.65 .06 240`). Pill shape, uppercase, `.14em` tracking.
- **Progress** — episodes = *segmented* bars (gold filled / `line` empty); play time = *continuous* bar with gold gradient fill.
- **Rating** — 5 gold stars; tap to set, tap again to clear (aligns with existing rating behavior).
- **Section header** — italic Instrument Serif title + uppercase mono kicker + hairline gradient rule + optional "All N →" gold link.
- **List row** — recently-played style: thumb, title, mono timestamp/duration, hairline separators.

**Motion — design-system owns the *vocabulary*, not the application:**
- **Owned here (primitives):** the gold-leaf sweep (`mtq-sweep`, ~3.2s linear infinite) **reserved for "In focus" only**, plus the shared durations/easings encoded once as keyframes/helpers — leave-transition (fade + collapse, 400ms ease-out) and cross-fade (200ms). Documented in `styleguide.md` with the "In-focus-only sweep" discipline.
- **Applied by BCs (out of scope here):** *where* the leave-transition fires (items leaving a queue) and *where* the cross-fade fires (dashboard tab changes) are dashboard/tab BC behavior — this task provides the reusable helpers; the dashboard rework wires them. Mirror the game-detail-chrome carve-out below.
- Spotlight gradient is static — never animated (a rule, documented, not a helper).

## Acceptance criteria

- [ ] Each pattern above exists as a **typed Feliz composition in `DesignSystem.fs`** (not inline in the page), referencing foundation tokens from [[design-system-r7k2m]] by name — no hardcoded oklch.
- [ ] The in-app StyleGuide page (`src/Client/Pages/StyleGuide`) shows a live specimen of each pattern: hero card, filmstrip row, secondary card, In-focus poster frame, all six status badges, both progress styles, star rating, section header, list row.
- [ ] Status badges cover the full game lifecycle (Backlog → InFocus → Playing → Completed / Abandoned / OnHold) with the palette mapping above; "In focus" is the only badge that animates.
- [ ] Motion **primitives** are encoded once (keyframes / helpers for the gold-leaf sweep, the 400ms leave-transition, the 200ms cross-fade) and documented; the gold-leaf sweep appears on In-focus surfaces only. Wiring these into queue-leave / tab-change behavior is explicitly **not** in this task.
- [ ] `styleguide.md` gains a "Component patterns" section documenting each pattern with its token references, plus a "Motion" subsection stating the vocabulary + the In-focus-only sweep discipline + the static-spotlight rule.
- [ ] `npm run build` compiles clean; specimens render on the StyleGuide route without console errors.

## Notes

- **Refinement decisions (2026-07-02, defaults applied while user away — re-confirm if desired):**
  1. **Not split** — kept as one cohesive component pass following the foundation pass.
  2. **Code home = `DesignSystem.fs` (typed), not inline.** This resolves the design-system BC README's standing open question ("migrate patterns to a dedicated `DesignSystem.fs` module vs stay inline"); the README's Open-questions bullet can be retired when this task is worked.
  3. **Motion = primitives only** — design-system owns the keyframes/helpers/discipline; queue-leave and tab cross-fade are wired by the dashboard/tab BCs (out of scope here, like the 3b game-detail chrome).
- **Reference:** same Claude Design project as [[design-system-r7k2m]] — turn 3 options **3a** (dashboard), **3b** (game detail: badges + rating, Overview/Journal tabs, HLTB tiers, play history, friends), **3c** (movies poster-grid; In-focus items get the gold frame), **3d** (system board). Read via `DesignSync` `get_file`.
- Existing `design-system-003` added an ActionMenu specimen to the StyleGuide page — follow that specimen pattern for the new ones (prior art), and treat its typed-composition style as the template for the `DesignSystem.fs` entries.
- Depends on the foundation task purely for tokens/type; can be refined in parallel but **must not be promoted ahead of [[design-system-r7k2m]]**. The two gating open decisions (glassmorphism coexistence vs ADR-0006; theme replace-in-place vs new name) live on r7k2m — this task inherits whatever r7k2m resolves and needs no separate decision on them.
- Game-detail-specific chrome (Overview/Journal tabs, HLTB tiers, session history) shown in 3b is BC-level UI (games/journal), not a design-system pattern — capture those separately in their BCs if wanted; this task only owns the reusable patterns.
