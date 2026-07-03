---
id: design-system-grtw7
title: Sidebar nav — align with dir 3a (gating: ivory ADR-0013 vs 3a burgundy active tab)
status: backlog
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

After reviewing the design session, the user says the sidebar nav elements "don't look like the ones from 3a yet". The shipped rail (design-system-t4b9k) diverges from direction 3a in two ways — one deliberate, one incidental — and the deliberate one needs an explicit user decision before this task can be promoted.

## The gating tension (must be resolved with the user)

**ADR-0013 records the user explicitly overriding 3a's active-tab treatment** during t4b9k refinement: the doc's burgundy fill + gold inset bar was replaced by the ivory placard + concave corner-notch. The new "should look like 3a" ask collides with that recorded decision. Options put to the user (AskUserQuestion on 2026-07-03 went unanswered — re-ask at refinement):

1. **Full 3a revert** — burgundy active tab `oklch(0.22 0.035 25)` + gold inset-left bar (`--ring-active`, already a token: `inset 2px 0 0 oklch(0.8 0.12 82)`), gold `◆` icon, drop the ivory placard and corner-notch. Supersedes ADR-0013 (write a superseding ADR).
2. **Keep ivory, adopt the rest** — ADR-0013 stands; only the non-contentious 3a elements below land.
3. Compare both variants live before deciding.

## Non-contentious 3a deltas (apply under either outcome)

- **Tagline** under the wordmark: "Where entertainment lives" — 8.5px, `letter-spacing: 0.26em`, uppercase, `oklch(0.52 0.04 45)`, `margin-top: 3px`. (t4b9k shipped the wordmark but omitted the tagline.)
- **Item metrics:** labels 13px (semibold only when active), icons 12px muted `oklch(0.45 0.03 35)`, item padding `9px 12px`, radius 8px, list gap 2px, list side-padding 12px.
- **Bottom group** (Events / Settings): one step smaller — 12px labels, 11px icons, `oklch(0.55 0.02 40)`.
- **Rail width:** 3a uses 216px; shipped rail is `w-64` (256px). Align or justify keeping current width.
- 3a also shows a profile chip ("Jonas" avatar) above the rail's foot behind a hairline `border-top` — likely **skip** (single-user app, no auth/profile concept); confirm with user.

## Acceptance criteria

- [ ] TBD — depends on the gating decision. Draft under option 1: active tab matches 3a exactly (burgundy fill, `--ring-active` gold bar, gold icon); ivory tokens + notch CSS removed; superseding ADR written. Draft under option 2: ivory tab untouched.
- [ ] Non-contentious deltas (tagline, item metrics, bottom-group scale, width decision) applied per the list above.
- [ ] `styleguide.md` § 4 "Sidebar nav" and the BC README's "Layered sidebar nav" entry updated in lockstep; StyleGuide specimen matches.
- [ ] `npm run build` clean.

## Notes

- Reference markup: `Mediatheca Directions.html` § `3a DESKTOP DASHBOARD`, sidebar block — all literal values quoted above.
- Filed to backlog (not todo) solely because of the ADR-0013 tension; everything else is refined. Resolving the single question promotes it.
