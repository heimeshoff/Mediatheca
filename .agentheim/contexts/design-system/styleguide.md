# Mediatheca Styleguide

> **Status:** Awaiting human sign-off (design-system-001, criterion 5).
> **Role:** Canonical, reviewable artifact for the design-system gate. Every frontend / UI task in any BC `depends_on` this document.
> **Scope:** This document *formalizes what already exists* in the running app. It does not change the design system. New tokens, new patterns, and retired patterns each go through the design-system backlog (see § 6).

This styleguide consolidates three pre-existing sources into one reviewable place:

- `src/Client/index.css` — CSS custom properties (tokens), the `dim` theme, and named utility classes.
- `src/Client/DesignSystem.fs` — typed F# compositions of Tailwind/DaisyUI classes that components reference instead of hardcoding strings.
- `src/Client/Pages/StyleGuide/Views.fs` — the live, in-app reference page rendering every pattern in situ.

It references code by file + line; it does not duplicate it. When this doc and the code disagree, **the code is authoritative for values; this doc is authoritative for intent and the gate.** Drift between them is a finding (see § 7 cross-check).

---

## 1. Tokens

Two layers (see `StyleGuide/Views.fs:86-155`, the "Two-Layer Architecture" overview):

- **Layer 1 — CSS custom properties** in `index.css:9-54` (`:root`). The primitive values.
- **Layer 2 — F# `DesignSystem` helpers** in `DesignSystem.fs`. Typed class compositions; components reference these.

### Glass effect tokens (`index.css:11-21`)

| Token | Value | Meaning |
|---|---|---|
| `--glass-bg-opacity-light` | `0.55` | Lightest overlay (least important / most see-through) |
| `--glass-bg-opacity-medium` | `0.60` | `.glass-card`, `.rating-dropdown` background base |
| `--glass-bg-opacity-heavy` | `0.70` | Modals / important overlays |
| `--glass-blur-subtle` | `12px` | `.glass-card`, `glassSubtle` |
| `--glass-blur-standard` | `24px` | `glassCard`, `.rating-dropdown` |
| `--glass-blur-heavy` | `40px` | Reserved (heaviest) |
| `--glass-saturate` | `1.2` | Saturation boost on backdrop-filter |
| `--glass-border-opacity` | `0.15` | Overlay border (`base-content/15`) |
| `--glass-highlight-opacity` | `0.08` | Top-edge inset highlight |
| `--glass-shadow-opacity` | `0.6` | Drop-shadow alpha |

### Spacing scale (`index.css:23-28`)

| Token | Value | F# helper | Use |
|---|---|---|---|
| `--space-page-mobile` | `1rem` | `pagePadding` (`p-4`) | Mobile page padding |
| `--space-page-desktop` | `1.5rem` | `pagePadding` (`lg:p-6`) | Desktop page padding |
| `--space-gap-compact` | `0.5rem` | `gapCompact` (`gap-2`) | Tight lists, inline groups |
| `--space-gap-standard` | `0.75rem` | `gapStandard` (`gap-3`) | Grids, card lists, default |
| `--space-gap-loose` | `1rem` | `gapLoose` (`gap-4`) | Section breaks |

### Border radii (`index.css:30-34`)

| Token | Value | Tailwind | Use |
|---|---|---|---|
| `--radius-card` | `1rem` | `rounded-xl` | Cards |
| `--radius-button` | `0.5rem` | `rounded-lg` | Buttons, pills |
| `--radius-avatar` | `9999px` | `rounded-full` | Avatars |
| `--radius-poster` | `0.375rem` | `rounded-md` | Posters |

### Animation durations (`index.css:36-39`)

| Token | Value | Use |
|---|---|---|
| `--duration-fast` | `0.15s` | Dropdowns, state changes |
| `--duration-normal` | `0.25s` | Hover effects |
| `--duration-slow` | `0.4s` | Page loads, stagger grids |

### Shadows (`index.css:41-46`)

`--shadow-card`, `--shadow-card-hover`, `--shadow-dropdown` (includes the inset highlight), `--shadow-poster`, `--shadow-poster-hover`. Elevated elements (modals, dropdowns, cards, posters) carry a shadow; flat content does not.

### Typography tokens (`index.css:48-53`)

- `--tracking-heading: 0.05em` — applied to all `h1`–`h6` automatically (`index.css:56-60`).
- Text-hierarchy opacities: primary `1`, secondary `0.7`, muted `0.5`, faint `0.4`. These four values are the **only** legal opacities for text content (enforced by `design-check` rule 3).

---

## 2. Typography

Live reference: `StyleGuide/Views.fs:159-280`.

- **Display font:** Oswald (`font-display`), used for **all** headings — always `uppercase` with `0.05em` tracking. Auto-applied to `h1`–`h6` via `index.css:56-60`; use the explicit class when rendering heading-like text on non-heading elements.
- **Body font:** Inter (`font-sans`), the default — body text, labels, buttons. Normal case.

Both loaded from Google Fonts; declared in `index.css:4-7` (`@theme`).

### Semantic type scale (`DesignSystem.fs:21-45`)

| Helper | Semantic role | Composition |
|---|---|---|
| `pageTitle` | h1 / page heading | `text-4xl font-display uppercase tracking-wider text-gradient-primary` |
| `sectionHeader` | h2 / section heading | `text-2xl font-display uppercase tracking-wider` |
| `cardTitle` | h3 / card heading | `text-lg font-display uppercase tracking-wider` |
| `subtitle` | secondary heading / label | `text-sm font-display uppercase tracking-wider` |
| `bodyText` | body / paragraph | `text-base text-base-content` |
| `secondaryText` | descriptions, metadata (70%) | `text-sm text-base-content/70` |
| `mutedText` | timestamps, labels (50%) | `text-xs text-base-content/50` |
| `faintText` | placeholders, hints (40%) | `text-xs text-base-content/40` |

**Decision (from the live page, `Views.fs:275-278`):** condensed display font (Oswald) + clean sans (Inter) creates hierarchy without decoration. Rejected: single font (monotone), serif (too formal for a media app).

---

## 3. Glassmorphism rules

This is the project's load-bearing overlay style. The spec below is reproduced **verbatim** from `CLAUDE.md` § "Conventions" so the styleguide is self-contained for review. `CLAUDE.md` now points at this document as the canonical artifact (see § 7 and ADR 0009).

### The overlay spec (verbatim from CLAUDE.md § Conventions)

> **Glassmorphism for all overlays**: Every dropdown, popover, modal, and floating panel MUST use glassmorphism — semi-transparent background (`/0.55`–`/0.70` opacity), `backdrop-filter: blur(24px) saturate(1.2)`, subtle border (`oklch(… / 0.15)`), and `inset 0 1px 0 0 oklch(100% 0 0 / 0.08)` highlight. Never use fully opaque backgrounds on overlays. See `.rating-dropdown` and `.glass-card` in `index.css` for reference.

### The backdrop-filter nesting gotcha (verbatim from CLAUDE.md § Gotchas)

> **`backdrop-filter` breaks on nested elements**: If a parent has `backdrop-filter` (e.g. `backdrop-blur-sm`), any child's `backdrop-filter` will only blur the parent's content, not the page behind it. Fix: render glassmorphic dropdowns/popovers as **siblings** to the blurred parent, not children. Wrap both in a plain `position: relative` container without `backdrop-filter`.

Correct sibling pattern (from `design-check/references/design-rules.md:38-47`):

```fsharp
Html.div [
    prop.className "relative"  // wrapper: NO backdrop-filter
    prop.children [
        Html.div [ prop.className "glassCard ..." ]                  // panel with blur
        Html.div [ prop.className "absolute z-50 rating-dropdown" ]  // dropdown with its own blur
    ]
]
```

### Glass levels (`DesignSystem.fs:9-19`; live demos `Views.fs:592-737`)

| Helper | Opacity / blur | Use case | Reference |
|---|---|---|---|
| `glassCard` | `/0.55`, `blur-[24px]`, `saturate-[1.2]`, `border-base-content/15` | Sidebar panels, detail cards | `DesignSystem.fs:10` |
| `glassOverlay` | `/0.70`, `blur-xl` | Modals, important overlays | `DesignSystem.fs:13` |
| `glassSubtle` | `/0.50`, `blur-sm` | Inline panels, content blocks | `DesignSystem.fs:16` |
| `glassDropdown` (`.rating-dropdown`) | `/0.65`, `blur(24px) saturate(1.2)`, inset highlight | Dropdowns, action menus | `DesignSystem.fs:19`, `index.css:269-281` |

Lighter opacity = more see-through = less important. Never fully opaque on a floating element.

---

## 4. Component patterns

Every pattern visible on the live StyleGuide page, with its anatomy and a code reference. The live page section order is fixed in `StyleGuide/Types.fs:3-13` and `Views.fs:1789-1827`.

### Glass card / overlay / subtle / dropdown
Covered in § 3. Live demos: `Views.fs:592-737`.

### Pill button (filter / tag toggle)
**Use:** filter bars, nav tabs, tag selection, the styleguide's own section nav. **Anatomy:** active = `bg-primary/15 text-primary border-primary/30`; inactive = transparent, hover reveals. **Helper:** `DesignSystem.pill isActive` (`DesignSystem.fs:74-81`). **Live:** `Views.fs:1142-1189`.

### PosterCard (grid)
**Use:** movie/media grid pages. 2:3 aspect-ratio poster, hover shine + lift, info overlay; renders as a link to detail. **Anatomy:** `poster-card` > `poster-image-container poster-shadow` > `poster-image` + `poster-shine` overlay (CSS `index.css:158-208`). **Component:** `PosterCard.view slug name year posterRef ratingBadge` (`Components/PosterCard.fs:9`); route variant `viewForRoute` (`:62`). **Live:** `Views.fs:896-946`.

### PosterCard thumbnail
**Use:** list/row layouts (Dashboard, FriendDetail, CatalogDetail, EntryList list mode). **Component:** `PosterCard.thumbnail posterRef alt` (`Components/PosterCard.fs:114`). **Live:** `Views.fs:949-966`.

### ModalPanel
**Use:** dialogs covering the viewport (cannot render inline). Glassmorphism overlay, backdrop-click closes. **Anatomy:** `DesignSystem.modalContainer` (`fixed inset-0 z-50`) + `DesignSystem.modalPanel` (`glassOverlay + animate-fade-in`) (`DesignSystem.fs:151-158`). **Component:** `ModalPanel.view title onClose content` (`Components/ModalPanel.fs:51`), `viewWithFooter` (`:54`), `viewCustom` (`:6`). **Live:** `Views.fs:969-1002`.

### FriendPill
**Use:** displaying friend references. Three variants. **Component:** `FriendPill.view friend` (badge, `Components/FriendPill.fs:8`), `viewWithRemove friend onRemove` (with X, `:25`), `viewInline friend` (text link, `:49`). **Live:** `Views.fs:1004-1061`.

### ActionMenu
**Use:** contextual action menus / dropdowns (kebab menus, hero action buttons). Glassmorphic per § 3. **Component:** `ActionMenu.view items` (`Components/ActionMenu.fs:60`), `heroView` (`:147`), `heroViewSections` (`:208`). Not yet demoed on the live page — *finding F-2, see § 7.*

### Icons
**Use:** Heroicons-based SVGs. Standard `w-6 h-6`; small variants `w-4 h-4` (`recommendedBy`, `play`); brand `Icons.mediatheca` `w-8 h-8`. **Component:** `Components/Icons.fs`. **Live (catalog of available icons):** `Views.fs:1063-1139`.

### Card hover / poster hover
**Use:** lift-on-hover affordance. `cardHover` = `card-hover rounded-xl` (`DesignSystem.fs:67`, CSS `index.css:148-156`); poster hover = scale + shine via `.poster-card:hover` (`index.css:167-208`). **Live:** `Views.fs:823-876`.

### Entrance animations & stagger grid
`animateFadeIn`, `animateFadeInUp`, `animateScaleIn`, `staggerGrid` (`DesignSystem.fs:86-95`; CSS keyframes `index.css:98-145`). Durations 0.15s–0.4s; stagger adds 40ms per child. **Live:** `Views.fs:741-821`.

### Grids
`movieGrid`, `movieGridMedium`, `statsGrid`, `cardGrid` (`DesignSystem.fs:114-123`). Responsive; never jump more than one column between adjacent breakpoints.

### Sidebar nav item
`navItemClass isActive` = `nav-glow` + active/inactive (`DesignSystem.fs:131-142`; CSS glow `index.css:210-229`). Used by `Components/Sidebar.fs`.

### ContentBlockEditor (Content Blocks)
**Use:** rich notes attached to movies. Inline-edit blocks (text/quote/callout/code/image), markdown-style `[text](url)` links, smart-paste, drag-to-reorder. No card chrome — blocks read as plain text. **Component:** `ContentBlockEditor.view blocks onAdd onUpdate onRemove onChangeType onReorder onUploadScreenshot onGroupBlocks onUngroupBlock` (`Components/ContentBlockEditor.fs:373`). **Live:** `Views.fs:1245-1388`.

### Content Zone (RowPair drag layout)
**Use:** Notion-like two-column grouping of content blocks via left/right drop zones; gap-based reordering with green indicator lines. Same `ContentBlockEditor.view` with `onGroupBlocks`/`onUngroupBlock` supplied. **Live:** `Views.fs:1472-1602`.

### EntryList (gallery / list database view)
**Use:** switchable Gallery (poster grid) / List (detail rows) view of media entries; layout toggle is local React state. **Component:** `EntryList.view props` (`Components/EntryList.fs:205`); `EntryItem` = Slug / Name / Year / PosterRef / Rating / RoutePrefix; caller supplies `RenderListRow`. **Live:** `Views.fs:1686-1785`.

---

## 5. Theme

- **Single theme:** `dim`, a custom DaisyUI 5 dark theme defined in `index.css:62-95` via `@plugin "daisyui/theme"`. Selected by `data-theme="dim"` on `<html>`. No light theme today (open question — see README).
- **Color space:** OKLch throughout, for perceptually uniform vibrancy. Dark base lets posters/backdrops pop.
- **Semantic colors:** `primary` (cyan-green, CTAs/nav highlights), `secondary` (orange, social/friends), `accent` (magenta, attention), plus `info` / `success` / `warning` / `error`. Live swatches: `Views.fs:298-446`.

### How to add a token
1. Add the CSS custom property to `:root` in `index.css` (or to the `dim` theme block if it is a theme color).
2. If components will reference it, add a typed `DesignSystem.fs` helper that composes the corresponding Tailwind/DaisyUI class — components must not hardcode the raw value.
3. Add a specimen to the relevant `StyleGuide/Views.fs` section so the live page stays complete.
4. Update this document and route the change through the design-system backlog (§ 6).

### When NOT to introduce a new token
- A semantic DaisyUI color (`primary`, `base-content`, …) already expresses the intent — use it with opacity rather than a new hue.
- The value is a one-off; prefer composing from the existing scale (spacing, radii, durations) over minting a new primitive.
- Text opacity outside `{1.0, 0.7, 0.5, 0.4}` — not allowed; map to the nearest hierarchy level instead.

---

## 6. Review process

This document is the design-system **gate artifact**.

- **The gate (load-bearing):** every frontend / UI task in any BC must declare `depends_on: [design-system-001-formalize-styleguide]`. The `model` skill applies this automatically per the gate rule in each frontend-bearing BC's README. The user signs off on this styleguide before any such task is promoted to `todo/`.
- **Changing the design system** (new token, new pattern, retired pattern) is never an inline edit during feature work. It is its own design-system backlog item, so the gate stays meaningful. Implementation tasks *conform* to the styleguide; they do not extend it.
- **Keeping it honest:** when a design-system change lands, update (a) `index.css` / `DesignSystem.fs`, (b) the live `StyleGuide` page, (c) this document, and (d) the `design-check` skill rules — in lockstep. Any divergence among the four is a finding for the next design-system task.
- **`design-check`** (`.claude/skills/design-check/`) is the automated companion: run it on changed `src/Client/**/*.fs|*.css` to audit conformance to the rules formalized here.

---

## 7. design-check cross-check (criterion: drift is a finding)

Cross-checked this document against `.claude/skills/design-check/references/design-rules.md` and `SKILL.md`. The encoded rules and this doc align on: glassmorphism overlay spec and the four glass helpers (rule 1 / § 3); backdrop-filter nesting (rule 2 / § 3); typography fonts + the four-value text hierarchy (rule 3 / § 2); `dim` theme + OKLch + semantic colors, no hardcoded hex/rgb (rule 4 / § 5); responsive grids and `pagePadding` (rule 5 / § 4); animation durations 0.15–0.4s and standard classes (rule 6 / § 4); shadow token system (rule 7 / § 1); DaisyUI 5 Feliz DSL usage (rule 8); preferring `DesignSystem.fs` helpers (rule 9 / § 4).

**Findings (drift to resolve in follow-up design-system backlog items, not in this formalization task):**

- **F-1 — Source-of-truth wording.** `design-rules.md:3-7` lists `index.css` and `DesignSystem.fs` as "source of truth" but predates this `styleguide.md`. Now that this is the canonical reviewable artifact, the skill's "Source of Truth" section should add a pointer to `styleguide.md`. Captured as `design-system-002`.
- **F-2 — ActionMenu not on the live page.** `Components/ActionMenu.fs` is a real, glassmorphic, recurring overlay pattern (kebab/hero menus) but has no specimen in `StyleGuide/Views.fs`. The live page is meant to render every recurring pattern. Captured as `design-system-003`.

Neither finding blocks the gate; both are documentation/coverage debt, queued in the design-system backlog.

---

## Sign-off

- [ ] **Human review (criterion 5):** the user has read and signed off on this styleguide. Sign-off is recorded as a one-line note in the protocol entry that closes `design-system-001`. Until then, this document is *ready for review* but the gate is not yet open for promoting frontend tasks to `todo/`.
