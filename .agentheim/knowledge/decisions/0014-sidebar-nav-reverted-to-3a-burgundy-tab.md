---
id: 0014
title: Sidebar active-nav tab reverted to dir 3a's burgundy fill (retracts ADR-0013's ivory placard)
scope: design-system
status: accepted
date: 2026-07-03
supersedes: [0013]
superseded_by: []
related_tasks: [design-system-grtw7]
related_research: []
---

# ADR 0014: Sidebar active tab reverted to dir 3a's burgundy fill

## Context
ADR-0013 recorded a three-way tension for the sidebar's active-nav-tab
treatment: dir 3a's own captured markup (a *burgundy* raised layer,
`background: oklch(0.22 0.035 25)` + a gold `inset 2px 0 0` left bar), a
separate user ask for a "white tab" made while viewing a pasted reference
image, and the ivory compromise ADR-0013 ultimately shipped (`oklch(0.94
0.02 75)`, dark-burgundy ink, gold icon, joined to the rail/content boundary
by a concave corner-notch).

Reviewing the shipped rail against the design session, the user reported the
sidebar nav "doesn't look like the ones from 3a yet." Re-asked directly at
`design-system-grtw7`'s refinement (2026-07-03) which of the three sources
should win, the user chose to abandon the ivory compromise entirely and
return to dir 3a's own burgundy treatment. This is not a partial
implementation or a bug fix — it is the user reversing a previously
deliberate, previously user-confirmed choice, now that they've seen it
running.

## Decision
1. Revert `.nav-item-active` to dir 3a's own values: `background:
   var(--color-nav-active-fill)` (`oklch(0.22 0.035 25)`) + `box-shadow:
   var(--ring-active)` (the pre-existing, previously-unused
   `inset 2px 0 0 oklch(0.80 0.12 82)` gold inset-left bar token). Label
   inherits the rail's default ink (no bespoke "active ink" color — dir 3a's
   own markup doesn't flip the label's color, only its weight to 600); the
   icon flips to `--color-gold` via `.nav-item-active-icon`.
2. Remove the ADR-0013 tokens and machinery entirely: `--color-nav-active-bg`,
   `--color-nav-active-ink`, `--color-nav-active-icon`, `--radius-nav-tab`,
   `--nav-notch-size`, `--shadow-nav-active`, the `.nav-item-active::before`/
   `::after` radial-gradient corner masks, and the negative-margin bleed
   (`margin-right: calc(-1 * var(--space-gap-standard))` /
   `padding-right: calc(var(--space-gap-standard) + var(--space-card))`).
   Nothing else referenced these — clean removal, no dead CSS left behind.
3. Land dir 3a's item metrics alongside the palette revert (the task's
   "non-contentious" deltas, incidentally omitted by `design-system-t4b9k`):
   13px labels (semibold only when active), 12px muted icons
   (`--color-nav-icon-muted`, `oklch(0.45 0.03 35)`), 9px/12px item padding,
   8px radius, 2px list gap, 12px list side-padding, a bottom group
   (Events/Settings) one step smaller (12px labels, 11px icons,
   `--color-nav-bottom-muted`, `oklch(0.55 0.02 40)`), and the
   "Where entertainment lives" tagline under the wordmark
   (8.5px/0.26em-tracked uppercase, `--color-ink-faint`).
4. Keep the shipped rail width (`w-64`, 256px) — dir 3a's own 216px mockup
   proportion is not adopted, since it was judged a mockup-scale artifact
   rather than a considered app-chrome decision. No profile chip — the
   single-user app has no profile/auth concept for dir 3a's "Jonas" avatar
   chip to represent.
5. Icon shape is unchanged: the sidebar keeps its established per-item SVG
   icon set (`Components/Icons.fs`, wired since `design-system-r7k2m`), just
   recolored gold on the active item. Dir 3a's own markup uses a literal `◆`
   glyph for its "Tonight"/Dashboard-equivalent active row, but that's an
   artifact of the mockup's generic glyph-per-item icon system (`▤`, `▦`,
   `◉`, `◎`, `▣`, `≡`, `✳`), not evidence that our differently-named,
   differently-iconed nav items should be swapped from meaningful SVG icons
   to a single shared Unicode diamond. Recoloring the existing icon gold
   satisfies dir 3a's "gold icon" intent without that disruptive, unstated
   icon-system replacement.

## Consequences
- This is a genuine flip-flop from ADR-0013 to dir 3a's own spec, not a
  correction of a previous mistake — ADR-0013 stands as the accurate record
  of why the ivory compromise was chosen at the time; this ADR is the record
  of why it was later abandoned. Both should be read together by anyone
  tracing the sidebar's history.
- `--ring-active` (previously minted but unused, reserved for exactly this
  purpose per ADR-0013's Context) is now load-bearing.
- The corner-notch technique and its coupling to `--space-gap-standard` (a
  risk ADR-0013 flagged) is moot — the active tab no longer bleeds past the
  nav gutter at all.
- `styleguide.md` § 4, the design-system BC README, and the live StyleGuide
  specimen are updated in lockstep to describe the burgundy tab, replacing
  the ivory-placard / corner-notch description and its ADR-0013 citation.
