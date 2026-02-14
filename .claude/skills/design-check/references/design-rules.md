# Mediatheca Design System Rules

## Source of Truth

- **CSS tokens & classes:** `src/Client/index.css`
- **F# class compositions:** `src/Client/DesignSystem.fs`
- **Components:** `src/Client/Components/` and `src/Client/Pages/*/Views.fs`

## Rule Categories

### 1. Glassmorphism (MANDATORY for all overlays)

Every dropdown, popover, modal, and floating panel MUST use glassmorphism.

**Required properties on overlays:**
- Semi-transparent background: opacity `/0.55` to `/0.70` (never fully opaque)
- `backdrop-filter: blur(24px) saturate(1.2)` (or Tailwind equivalents)
- Subtle border: `border-base-content/15` or `oklch(... / 0.15)`
- Inset highlight: `box-shadow: inset 0 1px 0 0 oklch(100% 0 0 / 0.08)`

**Predefined glass classes (prefer these):**
| DesignSystem helper | Use case |
|---|---|
| `glassCard` | Sidebar panels, detail cards |
| `glassOverlay` | Modals, important overlays |
| `glassSubtle` | Inline panels, content blocks |
| `glassDropdown` / `.rating-dropdown` | Dropdowns, action menus |

**Violations to flag:**
- `bg-base-100` / `bg-base-200` / `bg-base-300` without opacity on any overlay element
- Missing `backdrop-filter` / `backdrop-blur` on overlay elements
- Opacity outside the 0.50-0.70 range on overlays

### 2. backdrop-filter Nesting (CRITICAL gotcha)

If a parent has `backdrop-filter`, any child's `backdrop-filter` only blurs the parent's content, not the page behind it.

**Correct pattern:**
```fsharp
Html.div [
    prop.className "relative"  // wrapper: NO backdrop-filter
    prop.children [
        Html.div [ prop.className "glassCard ..." ]   // panel with blur
        Html.div [ prop.className "absolute z-50 rating-dropdown" ] // dropdown with its own blur
    ]
]
```

**Violations to flag:**
- An element with `backdrop-blur` / `backdrop-filter` nested inside another element that also has `backdrop-blur` / `backdrop-filter`
- Glassmorphic dropdown/popover rendered as child of a glassmorphic parent

### 3. Typography

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

### 4. Theme & Colors

- Theme: `data-theme="dim"` on `<html>` (custom DaisyUI dark theme)
- Color palette uses OKLch color space
- Semantic colors: `primary` (cyan), `secondary` (orange), `accent` (pink), `info`, `success`, `warning`, `error`

**Violations to flag:**
- Hardcoded hex/rgb/hsl colors instead of DaisyUI semantic tokens (`primary`, `base-content`, etc.)
- Using oklch values directly in F# code instead of referencing DaisyUI classes
- Exception: oklch values are fine in `index.css` where they define the design tokens

### 5. Spacing & Layout

**Page padding:** Use `DesignSystem.pagePadding` (`p-4 lg:p-6`) or `DesignSystem.pageContainer`

**Grids (responsive columns):**
- Poster/card grids: `grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6`
- Stats grid: `grid-cols-2 lg:grid-cols-4`
- Use DesignSystem helpers: `movieGrid`, `cardGrid`, `statsGrid`

**Violations to flag:**
- Non-responsive grids (fixed column count without breakpoints)
- Hardcoded padding instead of DesignSystem helpers
- Jumping more than 1 column between adjacent breakpoints

### 6. Animations

**Standard durations:**
- Fast (0.15s): dropdowns, state changes
- Normal (0.25s): hover effects
- Slow (0.4s): page loads, stagger grids

**Standard classes:** `animate-fade-in`, `animate-fade-in-up`, `animate-scale-in`, `stagger-grid`

**Violations to flag:**
- Custom animation durations far outside the 0.15-0.4s range
- Missing entrance animations on modals/dropdowns
- Inline transition styles instead of using DesignSystem/CSS classes

### 7. Shadows

**Standard shadow tokens (defined in CSS):**
- Card: `shadow-lg` / `--shadow-card`
- Card hover: `--shadow-card-hover`
- Dropdown: `--shadow-dropdown` (includes inset highlight)
- Poster: `--shadow-poster` / `--shadow-poster-hover`

**Violations to flag:**
- Custom `box-shadow` values that don't match the token system
- Missing shadow on elevated elements (modals, dropdowns, cards)

### 8. DaisyUI 5 Component Usage

**Prefer DaisyUI components:** `Daisy.button`, `Daisy.input`, `Daisy.card`, `Daisy.badge`, `Daisy.alert`, `Daisy.loading`, `Daisy.dock`

**Violations to flag:**
- Reimplementing components that DaisyUI provides (custom buttons, inputs, badges)
- Using DaisyUI 4 patterns (class-based like `btn btn-primary` instead of Feliz DSL `Daisy.button`)

### 9. DesignSystem.fs Usage

**Always prefer DesignSystem helpers** over inline class strings for:
- Glass effects, typography, layout, cards, buttons/pills, animations, grids, navigation, overlays

**Violations to flag:**
- Duplicating class strings that already exist in DesignSystem.fs
- Using raw Tailwind where a DesignSystem helper exists
