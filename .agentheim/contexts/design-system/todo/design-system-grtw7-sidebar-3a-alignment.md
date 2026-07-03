---
id: design-system-grtw7
title: Sidebar nav — align with dir 3a (full revert to burgundy active tab, supersedes ADR-0013)
status: todo
type: feature
context: design-system
created: 2026-07-03
completed:
depends_on: [design-system-001]
blocks: []
tags: [sidebar, nav, velvet-lobby, 3a, adr-0013]
related_adrs: [0013, 0009]
related_research: []
prior_art: [design-system-t4b9k, design-system-r7k2m]
---

## Why

After reviewing the design session, the user says the sidebar nav elements "don't look like the ones from 3a yet". The shipped rail (design-system-t4b9k) diverged from direction 3a in the active-tab treatment (deliberately, per ADR-0013) and omitted several 3a details (incidentally). Both are now being brought into line with 3a.

## Decision (resolved 2026-07-03, refinement)

The gating tension — ADR-0013's user-chosen **ivory placard + concave corner-notch** vs. 3a's **burgundy active tab** — is resolved in favor of **full 3a revert**. The user, re-asked at refinement, chose to abandon the ivory treatment and return to 3a's burgundy fill. This **supersedes ADR-0013**; a superseding ADR is part of the work (see acceptance criteria).

Secondary decisions taken at the same time:
- **Rail width:** keep the shipped `w-64` (256px). 3a's 216px was a mockup proportion, not adopted.
- **Profile chip:** skip. Single-user app has no profile/auth concept, so 3a's "Jonas" avatar chip carries no meaning.

## What

Revert the sidebar's active-tab treatment to 3a (burgundy fill, gold inset-left bar, gold ◆ icon), remove the now-abandoned ivory tokens and corner-notch machinery, and land the non-contentious 3a details t4b9k omitted (tagline, item metrics, bottom-group scale). Keep the current rail width; add no profile chip.

## Acceptance criteria

**Active tab — full 3a revert (supersedes ADR-0013):**
- [ ] Active nav tab uses 3a's burgundy fill `oklch(0.22 0.035 25)` with the gold inset-left bar (`--ring-active`, already a token: `inset 2px 0 0 oklch(0.8 0.12 82)`) and a gold `◆` icon.
- [ ] The ivory active-tab tokens (`--color-nav-active-bg` / `-ink` / `-icon`) and the entire concave corner-notch machinery (`--nav-notch-size`, the `::before`/`::after` radial-gradient corner masks, the negative-margin bleed) are removed — no dead CSS or unused tokens left behind.
- [ ] A superseding ADR is written recording the revert to 3a's burgundy tab and *why* the earlier ivory choice was reversed; it lists `0013` in `supersedes`, and ADR-0013's frontmatter `superseded_by` is updated to point at the new ADR (bidirectional).

**Non-contentious 3a deltas:**
- [ ] Tagline under the wordmark: "Where entertainment lives" — 8.5px, `letter-spacing: 0.26em`, uppercase, `oklch(0.52 0.04 45)`, `margin-top: 3px`.
- [ ] Item metrics: labels 13px (semibold only when active), icons 12px muted `oklch(0.45 0.03 35)`, item padding `9px 12px`, radius 8px, list gap 2px, list side-padding 12px.
- [ ] Bottom group (Events / Settings): one step smaller — 12px labels, 11px icons, `oklch(0.55 0.02 40)`.
- [ ] Rail width stays at `w-64` (256px) — 3a's 216px deliberately not adopted.
- [ ] No profile chip added.

**Docs & build (lockstep):**
- [ ] `styleguide.md` § 4 "Sidebar nav" and the BC README's "Layered sidebar nav" ubiquitous-language entry are updated to describe the burgundy active tab (replacing the ivory-placard / corner-notch description and its ADR-0013 citation); the StyleGuide page specimen matches.
- [ ] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3a DESKTOP DASHBOARD`, sidebar block — all literal values quoted above.
- The ivory tab shipped under ADR-0013 as a "deliberate, user-confirmed divergence"; that confirmation has now been reversed by the user. The superseding ADR should reference 0013's Context (the burgundy-vs-white-vs-ivory three-way) so the history reads coherently rather than looking like a flip-flop with no record.
- Everything here is refined; promoted to todo on resolution of the single gating question (2026-07-03).
