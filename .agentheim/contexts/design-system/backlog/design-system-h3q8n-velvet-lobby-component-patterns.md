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

Re-skin / add the Velvet Lobby component patterns in `DesignSystem.fs`, render them as
specimens on the in-app StyleGuide page, and document them in `styleguide.md`. These are the
patterns the brief repeats across 3a/3b/3c and codifies in the 3d system board.

**Patterns (from the brief):**
- **Cinematic hero card** — backdrop gradient with bottom scrim, In-focus badge top-left, title in Instrument Serif, watched-with avatar stack, segmented progress, rating, gold "▶ Watch" pill.
- **Filmstrip movie row** — black strip with sprocket-hole perforations top & bottom, poster gradients inset, titles + runtime/"rec. by" below.
- **Secondary media card** — compact poster-top card with serif title, "Next: SxEy" line, segmented progress.
- **Status badges** — game lifecycle mapped to the palette: Backlog (outline `line`), **In focus** (animated gold-leaf sweep), Playing (gold outline), Completed (green `~.7 .1 150`), Abandoned (red `~.62 .09 25`), On hold (blue `~.65 .06 240`). Pill shape, uppercase, `.14em` tracking.
- **Progress** — episodes = *segmented* bars (gold filled / `line` empty); play time = *continuous* bar with gold gradient fill.
- **Rating** — 5 gold stars; tap to set, tap again to clear (aligns with existing rating behavior).
- **Section header** — italic Instrument Serif title + uppercase mono kicker + hairline gradient rule + optional "All N →" gold link.
- **List row** — recently-played style: thumb, title, mono timestamp/duration, hairline separators.

**Motion (from 3d):**
- Gold-leaf sweep (`mtq-sweep`, ~3.2s linear infinite) **reserved for "In focus" only**.
- Items leaving a queue fade + collapse over 400ms ease-out.
- Tab changes cross-fade 200ms.
- Spotlight gradient is static — never animated.

## Acceptance criteria

- [ ] Each pattern above exists as a typed composition in `DesignSystem.fs` (or a documented rationale for any left inline), referencing foundation tokens from [[design-system-r7k2m]] — no hardcoded oklch.
- [ ] The in-app StyleGuide page (`src/Client/Pages/StyleGuide`) shows a live specimen of each pattern (hero card, filmstrip row, secondary card, all six status badges, both progress styles, star rating, section header, list row).
- [ ] Status badges cover the full game lifecycle (Backlog → InFocus → Playing → Completed / Abandoned / OnHold) with the palette mapping above; "In focus" is the only badge that animates.
- [ ] Motion rules encoded once (keyframes / helpers) and documented; the gold-leaf sweep appears on In-focus surfaces only.
- [ ] `styleguide.md` gains a "Component patterns" section documenting each pattern with its token references and the motion discipline.
- [ ] `npm run build` compiles clean; specimens render on the StyleGuide route without console errors.

## Notes

- **Reference:** same Claude Design project as [[design-system-r7k2m]] — turn 3 options **3a** (dashboard), **3b** (game detail: badges + rating, Overview/Journal tabs, HLTB tiers, play history, friends), **3c** (movies poster-grid; In-focus items get the gold frame), **3d** (system board). Read via `DesignSync` `get_file`.
- Existing `design-system-003` added an ActionMenu specimen to the StyleGuide page — follow that specimen pattern for the new ones (prior art).
- Depends on the foundation task purely for tokens/type; can be refined in parallel but must not be promoted ahead of [[design-system-r7k2m]].
- Game-detail-specific chrome (Overview/Journal tabs, HLTB tiers, session history) shown in 3b is BC-level UI (games/journal), not a design-system pattern — capture those separately in their BCs if wanted; this task only owns the reusable patterns.
