---
id: design-system-grtw7
title: Sidebar nav — align with dir 3a (full revert to burgundy active tab, supersedes ADR-0013)
status: done
type: feature
context: design-system
created: 2026-07-03
completed: 2026-07-03
depends_on: [design-system-001]
blocks: []
tags: [sidebar, nav, velvet-lobby, 3a, adr-0013]
related_adrs: [0013, 0009, 0014]
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
- [x] Active nav tab uses 3a's burgundy fill `oklch(0.22 0.035 25)` with the gold inset-left bar (`--ring-active`, already a token: `inset 2px 0 0 oklch(0.8 0.12 82)`) and a gold `◆` icon.
- [x] The ivory active-tab tokens (`--color-nav-active-bg` / `-ink` / `-icon`) and the entire concave corner-notch machinery (`--nav-notch-size`, the `::before`/`::after` radial-gradient corner masks, the negative-margin bleed) are removed — no dead CSS or unused tokens left behind.
- [x] A superseding ADR is written recording the revert to 3a's burgundy tab and *why* the earlier ivory choice was reversed; it lists `0013` in `supersedes`, and ADR-0013's frontmatter `superseded_by` is updated to point at the new ADR (bidirectional).

**Non-contentious 3a deltas:**
- [x] Tagline under the wordmark: "Where entertainment lives" — 8.5px, `letter-spacing: 0.26em`, uppercase, `oklch(0.52 0.04 45)`, `margin-top: 3px`.
- [x] Item metrics: labels 13px (semibold only when active), icons 12px muted `oklch(0.45 0.03 35)`, item padding `9px 12px`, radius 8px, list gap 2px, list side-padding 12px.
- [x] Bottom group (Events / Settings): one step smaller — 12px labels, 11px icons, `oklch(0.55 0.02 40)`.
- [x] Rail width stays at `w-64` (256px) — 3a's 216px deliberately not adopted.
- [x] No profile chip added.

**Docs & build (lockstep):**
- [x] `styleguide.md` § 4 "Sidebar nav" and the BC README's "Layered sidebar nav" ubiquitous-language entry are updated to describe the burgundy active tab (replacing the ivory-placard / corner-notch description and its ADR-0013 citation); the StyleGuide page specimen matches.
- [x] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3a DESKTOP DASHBOARD`, sidebar block — all literal values quoted above.
- The ivory tab shipped under ADR-0013 as a "deliberate, user-confirmed divergence"; that confirmation has now been reversed by the user. The superseding ADR should reference 0013's Context (the burgundy-vs-white-vs-ivory three-way) so the history reads coherently rather than looking like a flip-flop with no record.
- Everything here is refined; promoted to todo on resolution of the single gating question (2026-07-03).
- ADR-0014 written; see it for the worker's interpretation call on "gold ◆ icon": the sidebar keeps its established per-item SVG icon set (`Components/Icons.fs`), recolored gold on the active item, rather than being swapped to dir 3a's literal Unicode diamond glyph — that glyph is an artifact of the mockup's generic glyph-per-item icon system, not evidence our SVG icon set should be replaced (an unstated, disruptive scope expansion).

## Outcome

Reverted the sidebar's active-tab treatment from ADR-0013's ivory placard + concave corner-notch back to dir 3a's own burgundy fill (`--color-nav-active-fill`, `oklch(0.22 0.035 25)`) + gold inset-left bar (the previously-unused `--ring-active` token) + gold icon (`--color-gold`). Removed `--color-nav-active-bg`/`-ink`/`-icon`, `--radius-nav-tab`, `--nav-notch-size`, `--shadow-nav-active`, the `::before`/`::after` corner-mask pseudo-elements, and the negative-margin bleed — confirmed no remaining references anywhere in `src/`. Landed the non-contentious 3a deltas: tagline ("Where entertainment lives", `DesignSystem.navTagline`) under the wordmark in `Components/Sidebar.fs`; item metrics (13px labels/semibold-when-active, 12px muted icons via `--color-nav-icon-muted`, 9px/12px padding, 8px radius, 2px list gap, 12px side-padding); a smaller bottom group (12px labels, 11px icons, `--color-nav-bottom-muted`) scoped via a new `.nav-group-bottom` CSS class. Kept `w-64` rail width and added no profile chip, per the task's secondary decisions.

Key files: `src/Client/index.css` (tokens `@theme` block + the `.nav-item*`/`.nav-group-bottom` rule block), `src/Client/DesignSystem.fs` (`navItem`/`navItemActive`/`navItemInactive`/`navItemIconClass`/`navItemActiveIconClass`/`navGroupTop`/`navGroupBottom`/`navTagline`), `src/Client/Components/Sidebar.fs` (tagline markup, icon class wiring), `src/Client/Pages/StyleGuide/Views.fs` (updated specimen). `.agentheim/knowledge/decisions/0014-sidebar-nav-reverted-to-3a-burgundy-tab.md` (new, supersedes 0013) and `.agentheim/knowledge/decisions/0013-sidebar-nav-ivory-tab-and-corner-notch.md` (`superseded_by: [0014]`). `.agentheim/contexts/design-system/styleguide.md` (top status banner, palette table, § 4 Sidebar nav, "Shipped" checklist, Sign-off) and `.agentheim/contexts/design-system/README.md` ("Layered sidebar nav" ubiquitous-language entry) updated in lockstep. `npm run build` compiles clean (172 modules transformed, no errors).
