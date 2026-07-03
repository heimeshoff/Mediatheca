# Mediatheca Design System Rules

## Source of Truth

- **Canonical design system (intent & gate):** the live in-app StyleGuide page (`src/Client/Pages/StyleGuide`) is the source of truth for design-system intent and the frontend task gate (ADR 0015), backed by `DesignSystem.fs` and `index.css` below.
- **CSS tokens & classes:** `src/Client/index.css`
- **F# class compositions:** `src/Client/DesignSystem.fs`
- **Components:** `src/Client/Components/` and `src/Client/Pages/*/Views.fs`

## Rule Categories

### 1. Paper Overlay (MANDATORY for all floating surfaces — ADR-0016, supersedes ADR-0006's glassmorphism rule)

Every dropdown, popover, modal, and floating panel MUST use the paper-overlay material — never glassmorphism.

**Required properties on overlays:**
- Opaque fill: `--color-paper` (never a translucent `/NN` background on a floating surface)
- Elevation shadow: `--shadow-paper` (a true drop shadow — paper lifted off the page)
- Subtle line ring: `--color-line`
- No `backdrop-filter` / `backdrop-blur` anywhere on an overlay

**Predefined paper classes (prefer these):**
| DesignSystem helper | Use case |
|---|---|
| `paperOverlay` | Modals, floating panels, small controls over artwork |
| `paperDropdown` / `.rating-dropdown` | Dropdowns, action menus, context menus |
| `velvetCard` | Page/card chrome (NOT a floating overlay — flush with the page, ring-only elevation) |

**Violations to flag:**
- `backdrop-filter` / `backdrop-blur` anywhere in the codebase (fully retired)
- Semi-transparent (`/NN` opacity) backgrounds on a dropdown, popover, modal, or floating panel
- `velvetCard` used for a floating surface, or `paperOverlay`/`paperDropdown` used for page/card chrome — the two materials are deliberately distinct and must not be collapsed

### 2. Typography

**Font families:**
- Headings (h1-h6): `font-display` (Oswald) - auto-applied via CSS, but explicit class in Tailwind
- Body: `font-sans` (Inter) - default

**Heading convention:** All headings get `uppercase tracking-wider` automatically via CSS. In F#/Tailwind, use DesignSystem helpers:
- `pageTitle` for h1
- `sectionHeader` for h2
- `cardTitle` for h3
- `subtitle` for secondary headings

**Text hierarchy (opacity):**
| Level | Class | Opacity |
|---|---|---|
| Primary | `text-base-content` | 1.0 |
| Secondary | `text-base-content/70` | 0.7 |
| Muted | `text-base-content/50` | 0.5 |
| Faint | `text-base-content/40` | 0.4 |

**Violations to flag:**
- Hardcoded colors instead of `text-base-content` with opacity
- Opacity values not in the set {1.0, 0.7, 0.5, 0.4} for text content
- Headings missing `font-display` when using custom elements instead of h1-h6 tags
- Missing `uppercase` or `tracking-wider` on heading-like elements

### 3. Theme & Colors

- Theme: `data-theme="dim"` on `<html>` (custom DaisyUI dark theme)
- Color palette uses OKLch color space
- Semantic colors: `primary` (cyan), `secondary` (orange), `accent` (pink), `info`, `success`, `warning`, `error`

**Violations to flag:**
- Hardcoded hex/rgb/hsl colors instead of DaisyUI semantic tokens (`primary`, `base-content`, etc.)
- Using oklch values directly in F# code instead of referencing DaisyUI classes
- Exception: oklch values are fine in `index.css` where they define the design tokens

### 4. Spacing & Layout

**Page padding:** Use `DesignSystem.pagePadding` (`p-4 lg:p-6`) or `DesignSystem.pageContainer`

**Grids (responsive columns):**
- Poster/card grids: `grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6`
- Stats grid: `grid-cols-2 lg:grid-cols-4`
- Use DesignSystem helpers: `movieGrid`, `cardGrid`, `statsGrid`

**Violations to flag:**
- Non-responsive grids (fixed column count without breakpoints)
- Hardcoded padding instead of DesignSystem helpers
- Jumping more than 1 column between adjacent breakpoints

### 5. Animations

**Standard durations:**
- Fast (0.15s): dropdowns, state changes
- Normal (0.25s): hover effects
- Slow (0.4s): page loads, stagger grids

**Standard classes:** `animate-fade-in`, `animate-fade-in-up`, `animate-scale-in`, `stagger-grid`

**Violations to flag:**
- Custom animation durations far outside the 0.15-0.4s range
- Missing entrance animations on modals/dropdowns
- Inline transition styles instead of using DesignSystem/CSS classes

### 6. Shadows

**Standard shadow tokens (defined in CSS):**
- Card: `shadow-lg` / `--shadow-card`
- Card hover: `--shadow-card-hover`
- Dropdown: `--shadow-dropdown` (includes inset highlight)
- Poster: `--shadow-poster` / `--shadow-poster-hover`

**Violations to flag:**
- Custom `box-shadow` values that don't match the token system
- Missing shadow on elevated elements (modals, dropdowns, cards)

### 7. DaisyUI 5 Component Usage

**Prefer DaisyUI components:** `Daisy.button`, `Daisy.input`, `Daisy.card`, `Daisy.badge`, `Daisy.alert`, `Daisy.loading`, `Daisy.dock`

**Violations to flag:**
- Reimplementing components that DaisyUI provides (custom buttons, inputs, badges)
- Using DaisyUI 4 patterns (class-based like `btn btn-primary` instead of Feliz DSL `Daisy.button`)

### 8. DesignSystem.fs Usage

**Always prefer DesignSystem helpers** over inline class strings for:
- Paper overlays, velvet-card chrome, typography, layout, cards, buttons/pills, animations, grids, navigation

**Violations to flag:**
- Duplicating class strings that already exist in DesignSystem.fs
- Using raw Tailwind where a DesignSystem helper exists
