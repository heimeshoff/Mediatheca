# Mediatheca Styleguide — "Velvet Lobby"

> **Status:** Tokens & type foundation **shipped** (design-system-r7k2m: palette, typography, glass re-tint). Component patterns & motion **shipped** (design-system-h3q8n: § 1.3–1.6 tokens, § 3.1 velvet card, § 3.3 media-chrome glass, § 4 component patterns, § 4 Motion). Adopted from the *Mediatheca design brief* (Claude Design), turn 3, option **3a** — the "Velvet Lobby" desktop dashboard. The two gating decisions below (glassmorphism coexistence, theme replace-in-place) were applied as recommended defaults while the user was away and are **flagged for re-confirm**; full human sign-off on the redesign is still open.
> **Role:** Canonical, reviewable artifact for the design-system gate. Every frontend / UI task in any BC `depends_on` this document.
> **Scope:** This document describes the design system as it exists after the token + type foundation migration. `src/Client/index.css` (`@theme` + the `dim` theme's `@plugin "daisyui/theme"` block) and `src/Client/DesignSystem.fs` are authoritative for the *implemented* values (tokens, typography, glass tint); this document stays authoritative for **intent and the gate**. Component patterns (§ 4) remain **target/not-yet-shipped** — those still route through this doc's migration checklist (§ 7) via design-system-h3q8n. New tokens and patterns beyond what is captured here still route through the design-system backlog (see § 6).

The design direction in one line: **a warm cinematic editorial** — velvet-black surfaces, ivory serif titles, and a single **gold** accent used like foil, with cinema motifs (film-frame progress, sprocket-hole filmstrips, marquee "In focus" sweep) as the recurring ornament. Tagline: *"Where entertainment lives."*

**Source of truth for values:** the design brief's own system board (turn 3, "tokens & component patterns — maps to Tailwind 4 `@theme`") is reproduced faithfully below. `src/Client/index.css` (`@theme` + tokens, the `dim` theme) and `src/Client/DesignSystem.fs` (typed class compositions) are authoritative for the *implemented* values; this document stays authoritative for **intent and the gate**.

---

## 0. Migration status (read first)

Tokens and typography have landed; component patterns have not.

| Concern | Status | Detail |
|---|---|---|
| Base surface | **Shipped** | `dim` theme replaced in place: `base-200` = `bg` `oklch(0.16 0.028 20)`, `base-100` = `surface` `oklch(0.20 0.03 22)`, `base-300` = deep sidebar-rail `oklch(0.14 0.025 20)` |
| Primary accent | **Shipped** | `primary` = **Gold** `oklch(0.80 0.12 82)` (was cyan-green); `secondary`/`accent` are duller/brighter gold variants, not a second hue |
| Display font | **Shipped** | **Instrument Serif** (mixed case; italic = section-header voice), replacing Oswald |
| Body font | **Shipped** | **Instrument Sans**, replacing Inter |
| Data font | **Shipped** | **Spline Sans Mono** — new role, no legacy equivalent |
| Heading treatment | **Shipped** | Global forced `uppercase` + `0.05em` tracking on h1–h6 removed; uppercase now applied explicitly, reserved for **eyebrow labels** only |
| Text hierarchy (4 ink levels) | **Shipped** | Literal oklch steps (`--color-ink-secondary/-muted/-faint`) replace opacity-on-`base-content` |
| Overlay glassmorphism (dropdowns, modals, popovers) | **Shipped (re-tinted)** | ADR-0006's mandatory glass rule is **unchanged and unrelaxed** — `.glass-card` / `.rating-dropdown` re-tinted to the burgundy/gold palette, same opacity/blur/border spec |
| Velvet card (solid page/card surfaces) | **Shipped** | `.velvet-card` / `DesignSystem.velvetCard` (§ 3.1) — design-system-h3q8n |
| Spacing / radii / shadows / animation tokens (§ 1.3–1.6) | **Shipped** | Ported into `index.css` `:root` — design-system-h3q8n |
| Media-chrome glass (§ 3.3) | **Shipped** | `.media-chrome-glass` / `DesignSystem.mediaChromeGlass` — design-system-h3q8n |
| Component patterns (§ 4: hero card, filmstrip, secondary card, In-focus frame, status badges, progress meters, star rating, section header, list row) | **Shipped** | Typed `DesignSystem.fs` compositions + live StyleGuide specimens — design-system-h3q8n |
| Motion primitives (gold-leaf sweep, leave-transition, cross-fade) | **Shipped (vocabulary only)** | `DesignSystem.goldLeafSweep` / `.leaveTransition` / `.crossFade` — design-system-h3q8n. BCs still need to wire *where* leave-transition/cross-fade fire — out of scope for this task |
| Sidebar nav, top bar, lifecycle stepper, detail-page panels (HLTB tiers/play history/friends), avatars, poster grid page chrome | **Not yet implemented** | Documented target (§ 4) but not built as typed compositions or specimens by design-system-h3q8n — this task scoped to the patterns listed in its acceptance criteria; the rest remain future design-system backlog items |

`design-check` (`.claude/skills/design-check/references/design-rules.md`) still encodes some legacy rules (forced-uppercase heading violation, "cyan/orange/pink" semantic color description) — re-authoring it is tracked in § 7 and is out of scope for the token/type task; treat this document, not `design-check`, as canonical where they disagree in the interim.

---

## 1. Tokens

### 1.1 Palette (`oklch`) — from the brief's system board

Six primitives carry the whole system. All colors are OKLch (perceptually uniform; lets posters/backdrops pop against the dark base).

| Token | Value | Role |
|---|---|---|
| `bg` | `oklch(0.16 0.028 20)` | App background — velvet black |
| `surface` | `oklch(0.20 0.03 22)` | Cards, panels, raised chrome |
| `line` | `oklch(0.32 0.04 28)` | Hairline borders, dividers, empty progress track |
| `gold` | `oklch(0.80 0.12 82)` | The accent — CTAs, active state, rating, "In focus" foil |
| `spotlight` | `oklch(0.30 0.06 30)` | Burgundy radial glow behind the main content |
| `ink` | `oklch(0.93 0.012 60)` | Primary text — warm ivory |

**Implemented (design-system-r7k2m):** `bg`/`surface`/`ink`/`gold` are carried by the `dim` DaisyUI theme's `base-200`/`base-100`/`base-content`/`primary` slots (replaced in place in `src/Client/index.css`'s `@plugin "daisyui/theme"` block — the theme keeps the name `dim`, `data-theme="dim"` stays on `<html>`). `line` and `spotlight` have no DaisyUI-semantic slot and are minted directly as `--color-line` / `--color-spotlight` in the `@theme` block. `secondary` = the "fill gradient start" derived gold (`oklch(0.68 0.10 80)`), `accent` = the "bright foil sweep end" derived gold (`oklch(0.88 0.11 88)`) — both gold-family, per the accent-discipline rule (§ 5), not a second brand hue. `neutral` = `line`.

**Derived surfaces & accents** (used in 3a/3b, compose from the primitives — mint a token only if reused):

| Purpose | Value | Status |
|---|---|---|
| Sidebar rail (deeper than `bg`) | `oklch(0.14 0.025 20)` | Shipped — `base-300` |
| Active nav surface | `oklch(0.22 0.035 25)` | Not yet implemented (component pattern, h3q8n) |
| Gold — bright end of foil sweep | `oklch(0.88 0.11 88)` | Shipped — `accent` |
| Gold — fill gradient start | `oklch(0.68 0.10 80)` | Shipped — `secondary` |
| Avatar (self, gold-tinted) | bg `oklch(0.34 0.05 25)`, text `oklch(0.85 0.08 82)` | Not yet implemented (component pattern, h3q8n) |

### 1.2 Text hierarchy (four ink levels)

Text opacity is expressed as OKLch lightness steps on the warm-neutral hue, **not** alpha on `ink`. These four are the only legal levels for text content (the `design-check` "four-value hierarchy" rule carries over, retargeted to these values):

| Level | Value | Use |
|---|---|---|
| Primary | `oklch(0.93 0.012 60)` (`ink`) | Titles, key figures |
| Secondary | `oklch(0.74 0.015 45)` | Body copy, descriptions |
| Muted | `oklch(0.62 0.02 40)` | Metadata, inactive nav, captions |
| Faint | `oklch(0.52 0.04 45)` | Eyebrow tagline, placeholders, watermarks |

Mono watermarks over imagery use `oklch(1 0 0 / 0.4)` (white at 40%) rather than an ink step, since they sit on unpredictable artwork.

**Implemented:** `ink` = `base-content`; secondary/muted/faint are minted as `--color-ink-secondary` / `--color-ink-muted` / `--color-ink-faint` in `index.css`'s `@theme` block (`text-ink-secondary` etc.), and consumed by `DesignSystem.secondaryText` / `.mutedText` (alias `.metaText`) / `.faintText`.

> **Implemented (design-system-h3q8n):** spacing, radii, shadows, and animation tokens (§ 1.3–1.6 below, including the gold-leaf sweep keyframes) are ported into `index.css`'s `:root` block under the "Velvet Lobby spacing/radii/shadows/animation" comment, and consumed by the § 4 component patterns (velvet card, hero card, filmstrip, progress meters, status badges). design-system-r7k2m shipped only the palette (§ 1.1), text hierarchy (§ 1.2), typography (§ 2), and the glass re-tint (§ 3.4); this task added the rest.

### 1.3 Spacing (implemented — design-system-h3q8n)

Content rhythm from 3a: page gutter `32px`, section stack `26px`, card interior `14–18px`, tight inline groups `6–12px`. Map to the existing scale on migration:

| Token | Value | Use |
|---|---|---|
| `--space-page` | `2rem` (`p-8`, desktop) | Main content gutter |
| `--space-section` | `1.625rem` (`gap-[26px]`) | Between dashboard sections |
| `--space-card` | `1rem`–`1.125rem` | Card interior padding |
| `--space-gap-compact` | `0.5rem` (`gap-2`) | Inline groups, list rows |
| `--space-gap-standard` | `0.75rem` (`gap-3`) | Card grids |

### 1.4 Border radii (implemented — design-system-h3q8n)

| Token | Value | Use |
|---|---|---|
| `--radius-card` | `0.625rem` (`10px`) | Cards, panels, hero, backdrop |
| `--radius-panel` | `0.5rem` (`8px`) | Nav items, link rows, small chips |
| `--radius-pill` | `999px` | Badges, status pills, search field, CTA |
| `--radius-poster` | `0.125rem` (`2px`) | Filmstrip posters (tight, print-like) |
| `--radius-avatar` | `9999px` | Avatars |

### 1.5 Shadows & elevation (implemented — design-system-h3q8n)

Elevation is carried by **shadow + a `line`-colored ring**, not by translucency.

| Token | Value | Use |
|---|---|---|
| `--shadow-hero` | `0 20px 44px -18px oklch(0 0 0 / 0.85), 0 0 0 1px oklch(0.34 0.04 30)` | Hero, cover art |
| `--shadow-card` | `0 0 0 1px oklch(0.30 0.03 26)` | Standard velvet card (ring only) |
| `--shadow-filmstrip` | `0 16px 36px -18px oklch(0 0 0 / 0.9)` | Black filmstrip well |
| `--ring-active` | `inset 2px 0 0 oklch(0.80 0.12 82)` | Gold left-edge on active nav item |

### 1.6 Animation (implemented — design-system-h3q8n)

| Token | Value | Use |
|---|---|---|
| `--duration-fast` | `0.15s` | State changes, hover |
| `--duration-normal` | `0.25s` | Card lift, transitions |
| `--duration-slow` | `0.4s` | Leave-transition (fade + collapse) — see § 4 Motion |
| `--duration-crossfade` | `0.2s` | Cross-fade — see § 4 Motion |
| `--sweep` | `3.2s linear infinite` | Gold-leaf foil sweep on "In focus" |

The signature motion is the **gold-leaf sweep** (`@keyframes gold-leaf-sweep` moving `background-position` from `200% 0` → `-200% 0` across a 5-stop gold gradient on `background-size:200% 100%`, class `.gold-sweep` / `DesignSystem.goldLeafSweep`). Reserved for the "In focus" badge/state only — it is the one animated ornament; do not spread it to ordinary elements. Implemented `index.css` (`.gold-sweep`) + `DesignSystem.fs` (`goldLeafSweep`, `statusBadge InFocus`) — see § 4 Motion for the full primitive set (leave-transition, cross-fade) and the discipline.

---

## 2. Typography — Implemented (design-system-r7k2m)

Three families, each with one job (from the system board's "Type" specimen). Loaded via `@fontsource` npm packages (self-hosted `importSideEffects` calls in `src/Client/App.fs`, the same pattern the legacy Oswald/Inter fonts used) — not an external Google Fonts `<link>`.

| Family | Role | Notes |
|---|---|---|
| **Instrument Serif** | Display & titles | Page/section/card headings, hero & entity names. **Mixed case.** Italic is the "section voice" — used for section titles ("*Next up*", "*In focus*") and the word "*theca*" in the wordmark. |
| **Instrument Sans** | Body, labels, UI | Default. Weights 400–700. Buttons, nav, metadata, form controls. |
| **Spline Sans Mono** | Data | Dates, durations, counts, timecodes, HLTB hours, oklch specimens. Often `UPPERCASE` with letter-spacing for tabular labels. |

### Semantic type scale — implemented `DesignSystem.fs` helpers

| Helper | Role | Composition (as shipped) |
|---|---|---|
| `pageTitle` | Hero / entity name | Instrument Serif, `text-4xl md:text-5xl`, `leading-none`, ink |
| `sectionHeader` | Section heading | Instrument Serif **italic**, `text-2xl`, ink |
| `cardTitle` | Card / entity heading | Instrument Serif, `text-lg`, ink |
| `eyebrow` (alias: `subtitle`, kept for existing call sites) | Category / label above a section | Instrument Sans, `text-xs`, `uppercase`, `tracking-[0.18em]`, ink-muted |
| `bodyText` | Paragraph / description | Instrument Sans, `text-sm`, `leading-relaxed`, ink (full strength) |
| `secondaryText` | Descriptions, metadata | Instrument Sans, `text-sm`, ink-secondary |
| `mutedText` (alias: `metaText`) | Timestamps, labels, metadata | Instrument Sans, `text-xs`, ink-muted |
| `faintText` | Placeholders, hints | Instrument Sans, `text-xs`, ink-faint |
| `dataText` | Dates, durations, counts, ids | Spline Sans Mono, `text-xs`, ink-muted |

The shipped helper set keeps the codebase's existing four-tier ladder (`bodyText` > `secondaryText` > `mutedText` > `faintText`, used across ~40 call sites app-wide) rather than fully renaming to the brief's abstract role names; `eyebrow`/`metaText`/`dataText` are added as the brief's literal names (aliases where a legacy helper already covers the role) so both vocabularies resolve to the same implementation.

**Decision:** serif display (Instrument Serif, with italic as a distinct "voice") + clean sans body + mono for data creates an *editorial programme* feel — the library reads like a cinema listing, not a dashboard. **Uppercase is retired as the heading treatment** (it was Oswald's job — the global forced-uppercase CSS rule on h1–h6 was removed); uppercase now signals only an **eyebrow/data label**. Rejected: condensed all-caps headings (too utilitarian for the velvet direction).

---

## 3. Surfaces & overlays

**Resolved (2026-07-02, design-system-r7k2m):** ADR-0006's mandatory glassmorphism rule for overlays is **kept in full force, only re-tinted** — it is *not* demoted or relaxed. The brief's solid "velvet" surfaces (§ 3.1) apply to **page/card backgrounds**, which are not overlays, so there is no genuine conflict. *(Default applied while the user was away — flagged for re-confirm; amending ADR-0006 to allow solid structural panels remains a possible later decision task, see the task's Notes.)*

### 3.1 Velvet card — solid page/card surfaces (implemented — design-system-h3q8n)

The container that replaces `.glass-card` for **non-overlay** page/card chrome:

- Background: `surface` (`oklch(0.20 0.03 22)`, `var(--color-base-100)`)
- Border/elevation: a `line`-colored `1px` ring (`--shadow-card`); heroes add the drop-shadow (`--shadow-hero`) via the `.velvet-card-hero` modifier
- Radius: `--radius-card` (`10px`)
- No blur, no translucency.

Implemented: `.velvet-card` / `.velvet-card-hero` in `index.css`; `DesignSystem.velvetCard` / `DesignSystem.velvetCardHero` compose them (used by `heroCard` and `secondaryMediaCard`, `Pages/StyleGuide/Views.fs` "Velvet Lobby Patterns" section).

Rationale: the velvet direction gets its depth from **layered opaque darks + hairlines + shadow**, and lets artwork (posters, backdrops) provide the color and light. Translucent panels would mud the poster-driven palette. This governs **cards, panels, and page chrome** — never floating overlays, which stay glass per § 3.2.

### 3.2 Overlays — glassmorphism stays mandatory, re-tinted (shipped)

Every dropdown, popover, modal, and floating panel **MUST** still use glassmorphism, unchanged from ADR-0006: semi-transparent background (`/0.55`–`/0.70` opacity), `backdrop-filter: blur(24px) saturate(1.2)`, subtle border (`oklch(… / 0.15)`), and the `inset 0 1px 0 0 oklch(100% 0 0 / 0.08)` highlight. Never fully opaque. This is reproduced verbatim in `CLAUDE.md` (ADR-0009).

Velvet Lobby re-tints the **color**, not the rule:

| Class | Background | Border |
|---|---|---|
| `.glass-card` | `oklch(0.20 0.03 22 / 0.6)` (`surface`) | `oklch(0.93 0.012 60 / 0.08)` (`ink`) |
| `.rating-dropdown` | `oklch(0.14 0.025 20 / 0.65)` (deep sidebar-rail tone) | `oklch(0.93 0.012 60 / 0.15)` (`ink`) |

`.rating-dropdown-item` hover/active states move from the legacy primary-green tint to gold: hover `oklch(0.80 0.12 82 / 0.1)`, active fill `oklch(0.80 0.12 82 / 0.15)` with a matching `/0.2` border. `DesignSystem.glassOverlay` / `.glassSubtle` (which compose `bg-base-100/*` rather than a raw color) re-tint automatically once `base-100` = `surface` in the `dim` theme — no F# changes needed for those two.

### 3.3 Media chrome — a narrower glass variant for controls over artwork (implemented — design-system-h3q8n)

The brief additionally calls for a *narrower* glass spec for small controls floating directly over artwork ("Change artwork" pill, video play button on a backdrop, 3b) — subtler than the standard overlay glass so the image reads through:
- Background: `oklch(0.14 0.025 20 / 0.6)`
- `backdrop-filter: blur(6px)` (subtle — enough to legibilize, not frost)
- Border: `oklch(1 0 0 / 0.15)`

Implemented: `.media-chrome-glass` in `index.css`, `DesignSystem.mediaChromeGlass`, specimen in the StyleGuide "Velvet Lobby Patterns" section. This is an **addition alongside**, not a replacement for, § 3.2's mandatory overlay glass — dropdowns/modals/popovers still use the full spec.

### 3.4 The `backdrop-filter` nesting gotcha (still applies wherever glass is used)

> If a parent has `backdrop-filter`, a child's `backdrop-filter` blurs only the parent's content, not the page behind it. Render glass chrome as a **sibling** of the blurred element, wrapped in a plain `position: relative` container without `backdrop-filter`.

---

## 4. Component patterns

Anatomy of every recurring pattern in the 3a dashboard (plus the sibling 3b detail / 3c grid the same system produces). These are the specs the migrated `Components/**` and the live `StyleGuide` page must render.

**Implementation status (design-system-h3q8n):** the patterns below marked **Implemented** exist as typed Feliz compositions in `src/Client/DesignSystem.fs`, backed by CSS in `src/Client/index.css`, with a live specimen in `src/Client/Pages/StyleGuide/Views.fs`'s "Velvet Lobby Patterns" section (`velvetLobbyPatternsSection`). Patterns marked **Not yet implemented** remain documented target only — future design-system backlog items; this task's acceptance criteria scoped it to the hero card, filmstrip, secondary card, In-focus poster frame, status badges, progress meters, star rating, section header, and list row.

### Sidebar nav (desktop rail) — *not yet implemented*
Deep rail (`oklch(0.14 0.025 20)`), `216px`, `line` right border. Wordmark at top: "Media" + italic gold "*theca*", with the faint uppercase tagline beneath. Primary items (Tonight ◆ / Movies / TV Series / Games / Friends / Catalogs) then a bottom group (Events / Settings / avatar + name). **Active item** = `surface` background + `--ring-active` (inset gold left edge) + gold glyph; inactive = muted ink, no fill.

### Top bar — *not yet implemented*
Section tabs (All / Movies / TV Series / Games) as an underline nav — active tab carries a `2px` gold bottom-border; inactive muted. Right-aligned **search pill**: `surface` fill, `line` border, `--radius-pill`, `⌕` glyph + "Search your library…" placeholder.

### Section header — *Implemented (design-system-h3q8n)*
The editorial signature, used above every section: **Instrument Serif italic title** ("*Next up*", "*In focus*", "*Recently played*") + an **eyebrow** category label (uppercase, letter-spaced, muted) + a **hairline rule** that fades out (`linear-gradient(90deg, oklch(0.34 0.04 30), transparent)`), optionally a right-aligned gold "All 12 →" link.

`.section-rule` (`index.css`) + `DesignSystem.sectionHeaderPattern title eyebrow link` (`DesignSystem.fs`). Specimen: StyleGuide "Velvet Lobby Patterns" § Section Header.

### TV "Next up" hero — *Implemented (design-system-h3q8n)*
Backdrop gradient panel (velvet card, `--shadow-hero`) → **In focus** gold-sweep badge top-left, mono "backdrop · tmdb" watermark bottom-right → serif entity title + overlapping **friend avatars** ("with Mara & Alex") → episode meta line → **segmented episode progress** (film-frame bars, § 4 "Progress") + rating + gold **▶ Watch** pill.

`DesignSystem.heroCard { Title; InFocus; WatchedWith; ProgressFilled; ProgressTotal; Rating; OnRatingChange; OnWatchClick }` composes `velvetCardHero` + `statusBadge InFocus` + `progressSegmented` + `starRating`. The "backdrop · tmdb" watermark and full gradient backdrop imagery are left to the calling BC (this pattern renders the card chrome, not the artwork). Specimen: StyleGuide § Cinematic Hero Card.

### Secondary series / entity card — *Implemented (design-system-h3q8n)*
Backdrop thumb with top-fade, serif title, "Next: S2 E3 · 44 min" meta, segmented mini-progress, "2/12 episodes" count. Velvet card, `--shadow-card`.

`DesignSystem.secondaryMediaCard { Title; NextLabel; ProgressFilled; ProgressTotal }`. The backdrop-thumb top-fade is left to the caller (this pattern renders the text/progress chrome). Specimen: StyleGuide § Secondary Media Card.

### Movies filmstrip — *Implemented (design-system-h3q8n)*
The cinema motif: a **black (`#000`) well** with **sprocket-hole** strips top and bottom (`repeating-linear-gradient(90deg, transparent 0 7px, oklch(0.3 0.01 60) 7px 15px, transparent 15px 22px)`), a row of poster tiles (`--radius-poster`, `2px`) inside, and captions (title + runtime + "rec. by / with") beneath the strip. Carries `--shadow-filmstrip`.

`.filmstrip` (`index.css`) + `DesignSystem.filmstripRow [ { PosterRef; Title; Meta } ]`. Specimen: StyleGuide § Movies Filmstrip.

### Game row — *not yet implemented*
Horizontal velvet card: capsule thumbnail + title + **HLTB progress bar** (continuous, gold gradient fill) with mono "18h / ~34h" + a **status pill** (§ Status badges). Composable today from the shipped `DesignSystem.velvetCard` + `progressContinuous` + `statusBadge` primitives, but not built as its own typed composition/specimen.

### Recently-played list — *Implemented (design-system-h3q8n)*
Divider-separated rows (`line` bottom border): small thumb + title (+ "· with Alex") + right-aligned mono "yesterday · 2.4h".

`.list-row` (`index.css`) + `DesignSystem.listRow thumb title meta`. Specimen: StyleGuide § List Row.

### Status badges (lifecycle) — *Implemented (design-system-h3q8n)*
Pill badges, uppercase, letter-spaced. Each state has its own hue:

| State | Text / border |
|---|---|
| Backlog | `oklch(0.62 0.02 40)` / `oklch(0.36 0.03 30)` (muted outline) |
| ✦ In focus | dark ink on the **animated gold-leaf sweep** gradient (filled) |
| Playing | `oklch(0.80 0.12 82)` / `oklch(0.50 0.08 82)` (gold outline) |
| Completed | `oklch(0.70 0.10 150)` / `oklch(0.45 0.07 150)` (green) |
| Abandoned | `oklch(0.62 0.09 25)` / `oklch(0.42 0.07 25)` (red) |
| On hold | `oklch(0.65 0.06 240)` / `oklch(0.42 0.05 240)` (blue) |

`.status-badge*` (`index.css`) + `DesignSystem.statusBadge (status: DesignSystem.LifecycleStatus)`. **Note:** `LifecycleStatus` is this pattern's own vocabulary (Backlog/InFocus/Playing/Completed/Abandoned/OnHold), distinct from `Shared.GameStatus` (which has no `Playing` state and instead has `Dismissed`) — see the design-system BC README for the discrepancy this surfaced; mapping a BC's real status enum onto this vocabulary is a BC-level decision. Specimen: StyleGuide § Status Badges.

### Progress meters (two kinds) — *Implemented (design-system-h3q8n)*
- **Segmented** ("film-frame") — one bar per episode; filled = `gold`, empty = `oklch(0.32 0.03 30)`. For countable units (episodes).
- **Continuous** — single track (`line`) with a gold-gradient fill (`linear-gradient(90deg, oklch(0.68 0.1 80), oklch(0.85 0.11 86))`). For time/percent (play time, HLTB).

`.progress-segmented`/`.progress-continuous*` (`index.css`) + `DesignSystem.progressSegmented filled total` / `DesignSystem.progressContinuous fraction`. Specimen: StyleGuide § Progress Meters.

### Star rating — *Implemented (design-system-h3q8n)*
Five stars; filled = `gold`, empty = `oklch(0.36 0.03 30)`; optional mono numeric ("4.2"). Interaction: **tap to set, tap again to clear.**

`DesignSystem.starRating value onChange` — controlled component, caller owns state. Specimen: StyleGuide § Star Rating (numeric label deferred to the caller; the pattern renders the five stars only).

### Lifecycle stepper (detail pages) — *not yet implemented*
Horizontal Backlog → In focus → Playing → Completed with connector rules; the current stage renders as the filled gold-sweep pill, past stages as solid dots, future stages as hollow ringed dots.

### Detail-page panels (3b) — *not yet implemented*
Right-column velvet cards: **HLTB tiers** (labeled bars — Main story / Main+extra / My time [gold] / Completionist), **Play history** (mono date + duration rows, `+` add affordance), **Friends** (Owned by / Recommended by / Played with — avatars, "since JUN 20", dashed "pending" badge). Left column: cover art (`--shadow-hero`) + external-link rows (Steam / Website / HLTB). Trailers: 16:9 player + a thumbnail strip, active thumb ringed in gold.

### Poster grid (3c list page) — In-focus frame *Implemented (design-system-h3q8n)*, page chrome *not yet implemented*
Poster-grid list page with filter pills; **"In focus" items get the gold frame** (a gold ring/border marking them out from the grid).

The gold-frame treatment itself: `.in-focus-frame` (`index.css`) + `DesignSystem.inFocusFrame child` — wraps any poster/card element, the visual sibling of the In-focus status badge. Specimen: StyleGuide § In-Focus Poster Frame. The filter-pill grid page chrome around it is not built by this task.

### Avatars — *not yet implemented*
Circular, initial-based, per-person hue. Self = gold-tinted (`oklch(0.34 0.05 25)` / gold text). Groups overlap (`-11px` margin) with a `2px` `surface`-colored ring separating them. A minimal overlapping avatar stack ships inline inside `DesignSystem.heroCard`'s `WatchedWith` list, but not as its own reusable primitive/specimen yet.

### Motion — *Implemented, vocabulary only (design-system-h3q8n)*

Design-system owns the motion **vocabulary**, not its application. Three primitives, encoded once:

| Primitive | Spec | `index.css` | `DesignSystem.fs` |
|---|---|---|---|
| Gold-leaf sweep | `--sweep` (3.2s linear infinite), 5-stop gold gradient sweeping `background-position` | `@keyframes gold-leaf-sweep`, `.gold-sweep` | `goldLeafSweep` |
| Leave-transition | `--duration-slow` (400ms ease-out), fade + collapse (opacity, max-height, margin) | `.leave-transition` / `.leave-transition-leaving` | `leaveTransition` / `leaveTransitionLeaving` |
| Cross-fade | `--duration-crossfade` (200ms) | `.cross-fade` | `crossFade` |

**Discipline:**
- The gold-leaf sweep is reserved for **"In focus" surfaces only** (the status badge, the hero card's In-focus badge) — it is the one animated ornament in the system; do not spread it to ordinary elements.
- *Where* the leave-transition fires (e.g. items leaving a queue) and *where* the cross-fade fires (e.g. dashboard tab-panel swaps) is **BC behavior, out of scope for design-system** — this task ships the reusable primitives; the owning BC (e.g. a future dashboard rework) wires the application.
- The **spotlight gradient is static** — never animated. This is a rule, not a helper; there is no spotlight animation primitive to reach for.

Specimen: StyleGuide "Velvet Lobby Patterns" § Motion (documents the three primitives; does not demonstrate queue-leave/tab-crossfade wiring, which is out of scope here).

---

## 5. Theme & color usage

- **Single dark theme**, velvet-black based. (A light theme remains an open question — the whole direction is built for dark.)
- **Color space:** OKLch throughout.
- **Accent discipline:** there is **one** accent — gold — and it is spent sparingly, "like foil." Gold marks: the active/CTA, the current lifecycle state, ratings, "In focus", and the wordmark's italic. Everything else is velvet + ink. Resist adding a second accent hue; lifecycle **status** colors (green/red/blue) are functional signals, not brand accents, and appear only on status badges.
- **Artwork is the color.** Posters and backdrops supply the vivid hues; the chrome stays neutral so the media pops. The burgundy `spotlight` is the only ambient tint — a soft radial behind the main column.

### How to add a token
1. Add the value to `@theme` / `:root` in `index.css` (theme color → the theme block).
2. If components reference it, add a typed `DesignSystem.fs` helper composing the Tailwind class — components never hardcode raw values.
3. Add a specimen to the live `StyleGuide/Views.fs`.
4. Update this document and route the change through the design-system backlog (§ 6).

### When NOT to add a token
- One of the six primitives (or a documented derived value) already expresses it — compose, don't mint.
- You want a second brand accent — you don't; gold is the accent.
- A text opacity outside the four ink levels (§ 1.2) — map to the nearest level.

---

## 6. Review process

This document is the design-system **gate artifact**.

- **The gate:** every frontend / UI task in any BC declares `depends_on: [design-system-001-formalize-styleguide]`. The user signs off on this styleguide before such tasks promote to `todo/`. **This revision is a redesign — sign-off here also authorizes the code migration (§ 0) as its own design-system backlog item.**
- **Changing the design system** (new token/pattern, retired pattern) is never an inline edit during feature work — it is its own design-system backlog item, so the gate stays meaningful. Implementation tasks *conform*; they do not extend.
- **Keeping it honest:** when a design-system change lands, update in lockstep (a) `index.css` / `DesignSystem.fs`, (b) the live `StyleGuide` page, (c) this document, (d) the `design-check` rules. Divergence among the four is a finding.
- **`design-check`** audits conformance. Some of its rules currently encode the **legacy** system (forced-uppercase headings, "cyan/orange/pink" color description) and must be re-authored — tracked in § 7, out of scope for the token/type task. Its glassmorphism rule (§ 1 of `design-rules.md`) is still accurate and needs no change.

---

## 7. Migration checklist (supersedes the old drift cross-check)

The redesign is not fully shipped until these are done in lockstep. Track remaining items as design-system backlog items under this gate.

**Shipped (design-system-r7k2m):**
- [x] **Palette tokens** — `dim` theme's `base-100/200/300`, `base-content`, `primary/secondary/accent`, `neutral`, `info/success/warning/error` replaced in place with the Velvet Lobby palette (§ 1.1); `--color-line` / `--color-spotlight` minted directly; theme keeps the name `dim`, `data-theme="dim"` stays on `<html>`.
- [x] **Text hierarchy** — `--color-ink-secondary` / `--color-ink-muted` / `--color-ink-faint` minted as literal oklch steps (§ 1.2), replacing opacity-on-`base-content`.
- [x] **Fonts** — swapped Oswald/Inter for Instrument Serif + Instrument Sans + Spline Sans Mono (`@fontsource` packages); retargeted `@theme` font tokens (added `--font-mono`); removed the global forced-uppercase/tracking rule on h1–h6.
- [x] **`DesignSystem.fs`** — retargeted the type-scale helpers (§ 2: `pageTitle`, `sectionHeader`, `cardTitle`, `eyebrow`/`subtitle`, `bodyText`, `secondaryText`, `mutedText`/`metaText`, `faintText`) and added the new `dataText` (mono) helper. Glass helpers (`glassCard`, `glassOverlay`, `glassSubtle`, `glassDropdown`) were **not** replaced — overlays stay glass per § 3.2, re-tinted via the underlying CSS/theme change, no F# signature change needed.
- [x] **Glassmorphism re-tint** — `.glass-card` / `.rating-dropdown` (and their item hover/active states) re-tinted to the burgundy/gold palette in `index.css`; ADR-0006's rule unchanged (§ 3.2).
- [x] **Live StyleGuide page** — Typography section shows the three-typeface scale, the italic-voice specimen, and the new `dataText` role; Colors section shows the six Velvet Lobby primitives and the four ink-hierarchy steps with literal oklch labels.

**Shipped (design-system-h3q8n):**
- [x] **Spacing / radii / shadows / animation tokens** (§ 1.3–1.6), including the gold-leaf sweep keyframes (`@keyframes gold-leaf-sweep`) — ported into `index.css`'s `:root` block.
- [x] **Velvet card** (§ 3.1) and the narrower **media-chrome glass** (§ 3.3) — `.velvet-card`/`.velvet-card-hero`/`.media-chrome-glass` in `index.css`, `DesignSystem.velvetCard`/`velvetCardHero`/`mediaChromeGlass`.
- [x] **Components** (§ 4) — cinematic hero card, secondary media card, movies filmstrip, In-focus poster frame, status badges (six-state `LifecycleStatus`), segmented + continuous progress meters, star rating, section-header pattern, list row — all typed `DesignSystem.fs` compositions backed by `index.css`.
- [x] **Motion primitives** (§ 4 Motion) — gold-leaf sweep, leave-transition, cross-fade encoded once as CSS classes + `DesignSystem.fs` helpers; the In-focus-only-sweep discipline and static-spotlight rule documented. BC-level wiring (queue-leave, tab cross-fade) is explicitly out of scope.
- [x] **Live StyleGuide page** — new "Velvet Lobby Patterns" section (`velvetLobbyPatternsSection`, `src/Client/Pages/StyleGuide/Views.fs`) renders a specimen of every shipped pattern above.

**Not yet implemented (future design-system backlog items, not blocking this task):**
- [ ] **Sidebar nav, top bar, lifecycle stepper, detail-page panels (HLTB tiers/play history/friends), avatars, game row, poster-grid page chrome** (§ 4) — documented target, not built as typed compositions/specimens. Some (game row) are composable today from shipped primitives (`velvetCard` + `progressContinuous` + `statusBadge`) but not packaged as their own pattern.
- [ ] **`design-check`** — re-author `references/design-rules.md`: retarget typography rules (serif display, no forced uppercase, mono data role), retarget the color/token rule descriptions to the velvet palette, and add the new component-pattern classes. The glassmorphism rule itself needs no change.
- [ ] **`CLAUDE.md`** — the Fonts line was updated to the three Velvet Lobby families by design-system-r7k2m; the glassmorphism rule was left untouched (correct — it didn't change). No new CLAUDE.md-level convention was introduced by design-system-h3q8n (component patterns are BC-internal, not cross-cutting gotchas) — revisit if a later task finds otherwise.
- [ ] **Games BC status-vocabulary reconciliation** — `Shared.GameStatus` (Backlog/InFocus/Completed/Abandoned/OnHold/Dismissed) has no `Playing` state, while this pattern's `LifecycleStatus` (matching the design brief) does and lacks `Dismissed`. Reconciling — or deciding they're intentionally different vocabularies — is a Games BC decision, tracked as a new backlog item.

---

## Sign-off

- [x] **Token & type foundation implemented** (design-system-r7k2m, 2026-07-02): palette, text hierarchy, typography, and the glassmorphism re-tint are shipped in code (`index.css`, `DesignSystem.fs`, `App.fs`) and reflected here. `npm run build` compiles clean.
- [x] **Component patterns & motion implemented** (design-system-h3q8n, 2026-07-02): § 1.3–1.6 tokens, § 3.1 velvet card, § 3.3 media-chrome glass, § 4 component patterns (hero card, filmstrip, secondary card, In-focus frame, status badges, progress meters, star rating, section header, list row), and § 4 Motion primitives (gold-leaf sweep, leave-transition, cross-fade) are shipped in code (`index.css`, `DesignSystem.fs`) with live StyleGuide specimens and reflected here. `npm run build` compiles clean.
- [ ] **Human review — full redesign sign-off:** the two gating decisions (glassmorphism coexistence, theme replace-in-place) were resolved with recommended defaults while the user was away and are **flagged for re-confirm**. The user has not yet reviewed the shipped tokens/typography/component patterns in the running app. Until then, treat this as *implemented pending confirmation*, not a fully closed gate.
