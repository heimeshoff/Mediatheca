---
id: 0016
title: Glassmorphism retired; paper overlay (opaque elevation) is the floating-surface material
scope: design-system
status: accepted
date: 2026-07-03
supersedes: [0006]
superseded_by: []
related_tasks: [design-system-pv3mq]
related_research: []
---

# ADR 0016: Glassmorphism retired; paper overlay is the floating-surface material

## Context

ADR-0006 (2026-05-12) made glassmorphism — semi-transparent backgrounds with `backdrop-filter: blur(24px) saturate(1.2)` — the *mandatory* style for every overlay surface (dropdowns, popovers, modals, floating panels), a "no exceptions" rule. The Velvet Lobby re-skin (`design-system-r7k2m`, `design-system-h3q8n`) re-tinted the glass to the burgundy/gold palette but kept the rule in force, while introducing `.velvet-card` as a **solid**, non-overlay page/card surface (§ 3.1) explicitly deferred from the overlay rule, plus a narrower `.media-chrome-glass` variant (§ 3.3) for small controls floating over artwork.

On 2026-07-03 the user stated directly that Mediatheca "doesn't use glassmorphism anymore" — the overlays read as "a paper-style material design at best." The semi-transparent blurred look no longer describes what the UI actually is or wants to be, and keeping "mandatory glassmorphism" on the books while the design has moved on makes the design system lie to every frontend task that reads the gate. The user resolved the replacement material directly at dispatch: **paper elevation, distinct from `.velvet-card`** — a solid opaque fill (lighter than the page backdrop), a true elevation shadow (paper lifted off the page), and a subtle line ring. No translucency, no `backdrop-filter`.

This is a deliberate reversal of ADR-0006's central overlay rule, not a correction of a mistake — ADR-0006 stands as the accurate record of why glassmorphism was chosen at the time (and its TailwindCSS 4 + DaisyUI 5 + `dim` theme decisions remain valid); this ADR is the record of why the overlay-material rule was later retired.

## Decision

1. **Glassmorphism is retired as the overlay vocabulary.** No `backdrop-filter` / `backdrop-blur`, no semi-transparent overlay backgrounds, anywhere in the shipped UI.
2. **Paper overlay is the single floating-surface material** for dropdowns, popovers, and modals: an opaque fill (`--color-paper`, `oklch(0.24 0.032 24)` — lighter than the page backdrop), a subtle line ring (`--color-line`), and a true elevation shadow (`--shadow-paper`, a drop shadow, not just a ring — paper lifted off the page). Shipped as `.paper-overlay` in `index.css` and `DesignSystem.paperOverlay` / `DesignSystem.paperDropdown` (the dropdown-specific composition: `.rating-dropdown`, reused unchanged by name since it already served ratings, action menus, and every other dropdown/context-menu call site — only its CSS body changed).
3. **Paper overlay is distinct from `velvetCard`, not a merge of the two.** `velvetCard` (page/card chrome) sits flush with the page at `base-100` with a ring-only elevation (no drop shadow). Paper overlay floats above the page with a genuine drop shadow at a lighter fill. Two materials, two jobs: chrome vs. float.
4. **`.media-chrome-glass` (§ 3.3) is retired, folded into paper overlay.** Small controls floating directly over artwork (e.g. a "Change artwork" pill) now use `paperOverlay` with pill radius applied via utility classes, rather than a separate blurred variant.
5. **Non-overlay "glass" page-chrome surfaces migrate to `velvetCard`.** `DesignSystem.glassCard` and `DesignSystem.glassSubtle` were, in practice, used for page/card chrome (stat cards, detail panels, session cards, settings cards) — not floating overlays. These call sites now use `DesignSystem.velvetCard`, completing the migration `design-system-h3q8n` had already flagged in its own code comment ("Replaces `.glass-card` for page/card chrome") but had not yet executed across call sites.
6. **Ad hoc translucent-blur utility classes (`backdrop-blur-sm`, `bg-X/NN backdrop-blur-[…]`) on buttons, badges, and chrome elements are stripped of the blur token.** Where the surrounding color was already a simple alpha tint unrelated to the glassmorphism rule (e.g. `bg-base-300/30` hover states), the tint is kept — only the blur mechanism is removed; blur, not alpha compositing, was the glassmorphism signature.
7. **CLAUDE.md, `.agentheim/context-map.md`, the design-system BC README, and the `design-check` skill (`design-rules.md`) are repointed** at paper overlay and cite this ADR in place of ADR-0006's overlay rule. `.agentheim/knowledge/index.md`'s ADR-0006 line and design-system BC description are conductor-owned and repointed by the conductor during integration (workers do not edit indexes).
8. **The `backdrop-filter` nested-element gotcha is removed from CLAUDE.md.** No overlay in the shipped UI uses `backdrop-filter` anymore, so the nesting trap no longer applies.

## Consequences

- One floating-surface material instead of four graduated "glass levels" (`glassCard`/`glassOverlay`/`glassSubtle`/`glassDropdown`) — simpler vocabulary, one specimen to keep honest on the StyleGuide page.
- No more `backdrop-filter` GPU cost or the documented nesting gotcha; removes a whole class of "is this dropdown nested under a blurred ancestor" bugs.
- A11y: opaque paper is unconditionally legible over any backdrop — no low-contrast glass tuning needed.
- `.rating-dropdown` kept its CSS class name across the change (only its body changed from translucent+blurred to opaque+shadowed) — this avoided touching ~30 call sites across `ActionMenu.fs`, `EntryList.fs`, `ContentBlockEditor.fs`, and every detail page's rating dropdown, since none of those call sites reference "glass" by name.
- `DesignSystem.glassCard`/`glassOverlay`/`glassSubtle`/`glassDropdown`/`mediaChromeGlass` bindings are deleted, not deprecated-and-kept — any future PR reintroducing them would need a new ADR, which is the intended friction.
- Visual look changes for page-chrome cards that used to render as `glassCard` (Dashboard stat cards, Settings cards, SeriesDetail session cards) — they now render as `velvetCard`, whose ring-only elevation reads flatter than the old drop-shadowed glass look. This is intentional per the user's direction, not a regression.

## Alternatives rejected

- **Reuse `.velvet-card` exactly for overlays too (option b in the task notes)** — one surface vocabulary for both page chrome and floating panels. Rejected by the user directly: overlays should read as paper lifted above the page, visually separable from a page card; collapsing the two loses that separation.
- **Keep glassmorphism as an opt-in, non-mandatory style** — rejected because the user's framing was "we don't use glassmorphism anymore," not "make it optional." A living opt-in glass class would remain a standing invitation to reintroduce exactly the look being retired.
- **Phase the retirement (docs first, code later)** — rejected per the task's scope note; the user's framing was a definitive, full retirement, not a staged rollout.

## References

- `src/Client/index.css` — `--color-paper`, `--shadow-paper`, `.paper-overlay`, `.rating-dropdown` (updated body), `.velvet-card` (unchanged, now the sole page-chrome surface).
- `src/Client/DesignSystem.fs` — `paperOverlay`, `paperDropdown`, `modalPanel`, `velvetCard`/`velvetCardHero`.
- `src/Client/Pages/StyleGuide` — the "Paper Overlay" section (formerly "Glassmorphism").
- `CLAUDE.md` § "Conventions" (paper overlay rule replaces the glassmorphism bullet; the backdrop-filter gotcha removed from § "Gotchas").
- `design-check` skill (`.claude/skills/design-check/references/design-rules.md`).
- ADR-0006 (superseded by this ADR; its TailwindCSS/DaisyUI/`dim`-theme decisions remain valid).
- ADR-0015 (`styleguide.md` retired; in-app StyleGuide page authoritative) — the precedent this ADR's "retire X + supersede its ADR + repoint CLAUDE.md/README/design-check" shape follows.
