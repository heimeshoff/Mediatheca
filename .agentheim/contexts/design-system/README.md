# Design system

## Purpose
The **cross-cutting visual language** for Mediatheca's UI. Owns typography, color tokens, the dim theme, the paper-overlay floating-surface rules, Feliz/DaisyUI component patterns, and the in-app StyleGuide page. Gates frontend work in every BC.

## Classification
**supporting** — Custom-built, but its job is to *enable* the core BCs' UIs, not to differentiate the product.

## Actors
Single user, in a developer role.

## Ubiquitous language

- **Token** — a primitive design value (color, spacing, radius, opacity). Defined in `index.css` under the `dim` theme.
- **Theme** — currently a single dark theme called `dim`, selected by `data-theme="dim"` on `<html>` via DaisyUI 5's `@plugin "daisyui/theme"`. Palette replaced in place (2026-07-02, design-system-r7k2m) with the "Velvet Lobby" burgundy-black/gold palette — the theme keeps the name `dim`.
- **Paper overlay** (2026-07-03, design-system-pv3mq, ADR-0016) — the project's floating-surface material, replacing glassmorphism: an opaque fill (`--color-paper`, lighter than the page), a subtle line ring (`--color-line`), and a true elevation shadow (`--shadow-paper` — paper lifted off the page). No translucency, no `backdrop-filter`. Used by every dropdown, popover, modal, and floating panel (`.paper-overlay` / `DesignSystem.paperOverlay`; the dropdown-specific composition kept its historical class name `.rating-dropdown` / `DesignSystem.paperDropdown` across the rename). Small controls floating over artwork (the old § 3.3 "media-chrome glass") were folded into this same material rather than kept as a separate blurred variant.
- **Surface** — any element rendered above the page. Floating overlays (dropdowns, popovers, modals) use the **paper overlay** material (ADR-0016). Non-overlay page/card surfaces use the solid **velvet card** treatment (§ 3.1, `.velvet-card` / `DesignSystem.velvetCard`) instead — shipped 2026-07-02, design-system-h3q8n. The two materials are deliberately distinct: velvet card sits flush with the page (ring-only elevation); paper overlay floats above it (true drop shadow, opaque fill lighter than the page).
- **Component pattern** — a recurring Feliz / DaisyUI combination. Reusable, BC-agnostic patterns (e.g. rating dropdown, catalog card, poster rail) live as **typed Feliz compositions directly in `DesignSystem.fs`** (resolved 2026-07-02, design-system-h3q8n — see Open questions below); component-specific chrome that needs its own React lifecycle (e.g. `ActionMenu`'s open/close state) still lives in `Components/`.
- **`bordered` retired across the whole form-control family** (design-system-dib4q, design-system-q4ebg) — DaisyUI 5 dropped the v4-era `bordered` modifier on `input`, `select`, **and** `textarea` alike; all three render bordered by default now, and Feliz.DaisyUI 5.x (verified in both 5.2.0 and 5.3.0's `Modifiers.fs`) exposes no `.bordered` member for any of them. `Daisy.input`/`Daisy.select`/`Daisy.textarea` calls must never carry `.bordered` — it is `FS0039` (does not exist), and `vite-plugin-fable` treats that as non-fatal to bundle emission, emitting a throwing placeholder that blanks the whole `#feliz-app` root at render instead of a compile failure. `file-input-bordered` is the sole survivor of the old naming (a distinct, still-valid DaisyUI 5 class for the file-input variant). dib4q fixed the `input` call sites by a one-time grep-and-delete that left no durable rule behind it, and the identical mistake landed on `select`/`textarea` the very next day (q4ebg) — this entry is the fix for *that* gap, not just the code.
- **Motion primitive** — a keyframe/transition owned by design-system as *vocabulary*, not application: the gold-leaf sweep (`DesignSystem.goldLeafSweep`, reserved for "In focus" surfaces only), the leave-transition (`leaveTransition`/`leaveTransitionLeaving`, 400ms fade+collapse), and the cross-fade (`crossFade`, 200ms). *Where* these fire (a queue item leaving, a dashboard tab swap) is BC behavior — design-system ships the primitive, the owning BC wires the application. The spotlight gradient is deliberately static and has no motion primitive. Both sweep carriers (`.gold-sweep`, `.in-focus-frame`) freeze under `prefers-reduced-motion: reduce` (design-system-bky6v) rather than disabling the gold-fill signal outright.
- **In-focus poster frame** — `DesignSystem.inFocusFrame child` (`.in-focus-frame` + `.in-focus-frame-inner` in `index.css`) wraps any poster/card element with the sweeping gold-gradient border + glow, the visual sibling of the In-focus status badge. Originally shipped static (design-system-h3q8n); the sweep animation and the status badge's sweep were both bug-fixed/added in design-system-bky6v — the badge bug was a `background` shorthand on `.status-badge` clobbering `.gold-sweep`'s `background-image`, fixed via `background-color`. Function signature unchanged across the fix.
- **In-focus pill** (2026-07-03, design-system-fq3vp) — `DesignSystem.inFocusPill` (`.in-focus-pill` in `index.css`), the compact on-poster "✦ Focus" badge from the 3c grid direction (8.5px/700/0.18em tracking, dark ink on solid gold, top-left over the artwork). A genuinely separate composition from `statusBadge InFocus` (list rows, hero, detail) so the two can diverge freely — the poster-grid pairing is `inFocusFrame` (animated) + `inFocusPill` (deliberately solid, no `.gold-sweep`, no new keyframe): one moving element per poster, motion economy against the frame's sweep directly behind it.
- **Lifecycle status vocabulary** — `DesignSystem.LifecycleStatus` (Backlog/InFocus/Playing/Completed/Abandoned/OnHold) is the status-badge pattern's own six-state vocabulary, matching the design brief. It is **not** the same as `Shared.GameStatus` (Backlog/InFocus/Completed/Abandoned/OnHold/Dismissed — no `Playing`, has `Dismissed`). This discrepancy surfaced during design-system-h3q8n and is tracked as a Games BC backlog item; mapping one onto the other (or keeping them deliberately distinct) is a Games BC decision, not design-system's.
- **StyleGuide page** — the live, in-app reference at `src/Client/Pages/StyleGuide` rendering every component pattern in situ.
- **Typography ("Velvet Lobby")** — Instrument Serif (`font-display`, display & titles, mixed case; *italic* is the section-header/wordmark voice) + Instrument Sans (`font-sans`, body/UI) + Spline Sans Mono (`font-mono`, dates/durations/counts/ids), loaded via self-hosted `@fontsource` packages. Replaced Oswald/Inter in place (2026-07-02, design-system-r7k2m); the forced-uppercase heading rule was retired — uppercase now signals only an eyebrow/data label.
- **3c list-page type tiers** (2026-07-03, design-system-snpnv) — additions to the editorial scale for dense list-page chrome: `gridCaptionTitle`/`gridCaptionMeta`/`gridCaptionPair` (poster-grid / filmstrip captions — a deliberately *sans* voice, distinct from `cardTitle`'s serif), `listPageHeaderTitle`/`listPageHeaderCount`/`listPageHeaderPattern` (serif title baseline-paired with a mono count line), and `filterPill` (active gold-fill / inactive line-bordered toggle chip, `.filter-pill*` in `index.css`). No existing helper renamed; wiring into an actual list page is BC-level work.
- **Ink hierarchy** — four literal oklch text-color steps (`ink`, `ink-secondary`, `ink-muted`, `ink-faint`), minted as named Tailwind tokens in `index.css` and consumed via `DesignSystem.fs`'s `bodyText`/`secondaryText`/`mutedText`(`metaText`)/`faintText`. Replaces the legacy opacity-on-`base-content` approach.
- **Layered sidebar nav** (2026-07-03, design-system-t4b9k; active tab reverted to dir 3a by design-system-grtw7) — the desktop rail (`Components/Sidebar.fs`) splits items into a top group (Dashboard/Movies/TV Series/Games/Catalogs/Friends) and a bottom group (Events/Settings, one step smaller — 12px labels, 11px icons) pinned via `mt-auto`, with a tagline ("Where entertainment lives") under the wordmark. The active item is dir 3a's own **burgundy fill** (`--color-nav-active-fill`) with a gold inset-left bar (`--ring-active`) and a gold icon — reverted from ADR-0013's ivory placard + concave corner-notch, which the user abandoned after seeing it running (see ADR-0014, supersedes ADR-0013). `DesignSystem.navItemClass`/`navItemIconClass`/`navItemActiveIconClass`/`navGroupTop`/`navGroupBottom`/`navTagline`; the old `.nav-glow` left-edge bar remains retired.
- **Underline tab** (2026-07-06, design-system-k9p3v) — the header-tab sibling of the dir-3a sidebar nav: a text tab strip with a **gold underline** (`--color-gold`, the same token as the sidebar's active icon/inset-bar — no new colour) under the active tab, and **no filled-pill / bordered-button chrome**. `DesignSystem.underlineTabClass isActive` (or the raw `underlineTab`/`underlineTabActive`/`underlineTabInactive` class strings) — the caller renders its own `Html.button` list and wires clicks; the composition owns only the look. `.underline-tab`/`.underline-tab-active` in `index.css` supply the `::after` underline bar. Specimen on the StyleGuide page's "Underline Tabs" section, directly under "Sidebar Nav". First consumer is the Dashboard's All-tab header strip (`intelligence-dq8rk`, not yet migrated as of this entry — this task shipped the shared pattern only).

## Aggregates

No domain aggregates. The design system is content + rules, not behavior.

## Key events / commands

None. This BC produces UI artifacts (CSS tokens, Feliz components, documentation), not events.

## Relationships with other contexts

- **Open host / shared kernel for:** every frontend-bearing BC (Movies, Series, Games, Journal, Friends, Curation, Intelligence, Integration, Administration). All of them conform to design-system tokens and patterns.

## The styleguide gate (load-bearing)

**Every frontend / UI task in any BC must `depends_on` a design-system task** (anchor: [`design-system-001-formalize-styleguide`](done/design-system-001-formalize-styleguide.md), done). Per ADR-0015, the gate no longer reviews against a standalone prose document — it reviews against the **living design system**: `DesignSystem.fs` (typed compositions) + `index.css` (tokens/values), inspected via the running in-app StyleGuide page. The gate keeps its force; only its meaning shifted from "conform to `styleguide.md`" to "conform to the live system, reviewed on the running StyleGuide page." Refer captures of frontend tasks back here.

When the design system changes (new token, new pattern, retired pattern), the change goes through this BC's backlog so the gate stays meaningful.

## Existing assets (mature project)

The **canonical, reviewable artifact** is the live **in-app StyleGuide page** (`src/Client/Pages/StyleGuide`), backed by `DesignSystem.fs` and `index.css` (ADR-0015, superseding ADR-0009). `styleguide.md`, the original standalone document produced by `design-system-001`, was retired 2026-07-03 (`design-system-sg8kd`) and archived to `.workflow.archived/styleguide.md` as a historical record — it is no longer read or updated.

Underlying sources:
- `src/Client/index.css` — token definitions, dim theme, `.paper-overlay`, `.rating-dropdown`, `.velvet-card`, etc. (authoritative for *values*).
- `src/Client/DesignSystem.fs` — typed Feliz/Tailwind class compositions used by components (authoritative for pattern *intent*).
- `src/Client/Pages/StyleGuide` — the live, in-app reference page; the review surface for the gate.
- `CLAUDE.md` § "Conventions" — paper-overlay rule (ADR-0016, supersedes ADR-0006's glassmorphism rule; the old `backdrop-filter` nesting gotcha was removed from § "Gotchas" since no overlay uses `backdrop-filter` anymore).
- The `design-check` skill (`.claude/skills/design-check/`) — audits code against the system.

## Open questions

- Whether to introduce a light theme. Currently dim-only.

**Resolved (2026-07-02, design-system-h3q8n):** component patterns migrate to `DesignSystem.fs` as typed Feliz compositions (not inline in pages) — see the Component pattern entry in Ubiquitous language above.
