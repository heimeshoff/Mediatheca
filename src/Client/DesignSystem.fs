module Mediatheca.Client.DesignSystem

// Design system composition helpers.
// Components should use these instead of hardcoding class strings.
// CSS custom properties (--glass-*, --space-*, etc.) are defined in index.css.

// ── Glass Effects ──

/// Standard glassmorphism panel (sidebar cards, detail panels)
let glassCard = "bg-base-100/55 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 rounded-xl shadow-lg"

/// Heavy glassmorphism (modals, important overlays)
let glassOverlay = "bg-base-100/70 backdrop-blur-xl rounded-2xl shadow-2xl border border-base-content/10"

/// Subtle glassmorphism (content block cards, inline panels)
let glassSubtle = "bg-base-100/50 backdrop-blur-sm"

/// Glassmorphism dropdown (rating dropdown, action menus)
let glassDropdown = "rating-dropdown"

// ── Typography (Velvet Lobby: Instrument Serif / Instrument Sans / Spline Sans Mono) ──
// Four ink levels are minted as literal oklch steps in index.css (--color-ink-*),
// not opacity fractions of base-content — see styleguide.md § 1.2.

/// Page title (h1) — hero / entity name. Instrument Serif, mixed case, tight leading.
let pageTitle = "text-4xl md:text-5xl font-display leading-none text-base-content"

/// Section header (h2) — the editorial "voice". Instrument Serif *italic* —
/// used for section titles ("Next up", "In focus") and the "theca" wordmark.
let sectionHeader = "text-2xl font-display italic text-base-content"

/// Card title (h3) — Instrument Serif, mixed case.
let cardTitle = "text-lg font-display text-base-content"

/// Eyebrow — category/label above a section. Instrument Sans, small caps-style
/// uppercase with wide tracking. Also exported as `subtitle` for existing call sites.
let eyebrow = "text-xs font-sans uppercase tracking-[0.18em] text-ink-muted"

/// Subtitle / secondary heading — alias of `eyebrow` (legacy name, same role).
let subtitle = eyebrow

/// Body text — Instrument Sans, relaxed leading, full-strength ink.
let bodyText = "text-sm font-sans leading-relaxed text-base-content"

/// Secondary text (descriptions, metadata) — ink-secondary step.
let secondaryText = "text-sm font-sans text-ink-secondary"

/// Muted text (timestamps, labels) — ink-muted step. Also exported as `metaText`.
let mutedText = "text-xs font-sans text-ink-muted"

/// Metadata text — alias of `mutedText` (matches the styleguide's semantic scale).
let metaText = mutedText

/// Faint text (placeholders, hints) — ink-faint step, the lowest-priority level.
let faintText = "text-xs font-sans text-ink-faint"

/// Data text — Spline Sans Mono. Dates, durations, counts, timecodes, ids.
/// The "data" typeface: a new role with no legacy equivalent.
let dataText = "text-xs font-mono text-ink-muted"

// ── Layout ──

/// Standard page padding (responsive)
let pagePadding = "p-4 lg:p-6"

/// Standard content max-width with centering
let pageContainer = "p-4 lg:p-6 max-w-7xl mx-auto"

/// Standard gap between items in lists/grids
let gapStandard = "gap-3"

/// Compact gap
let gapCompact = "gap-2"

/// Loose gap
let gapLoose = "gap-4"

// ── Cards ──

/// Card with hover lift effect
let cardHover = "card-hover rounded-xl"

/// Static card (no hover effect)
let cardStatic = "rounded-xl bg-base-200/50"

// ── Buttons ──

/// Pill button for filters and tags (inactive state)
let pillButton = "px-4 py-2 rounded-lg text-sm font-medium transition-all duration-200 text-base-content/60 hover:text-base-content hover:bg-base-300/50 border border-transparent"

/// Pill button (active state)
let pillButtonActive = "px-4 py-2 rounded-lg text-sm font-medium transition-all duration-200 bg-primary/15 text-primary border border-primary/30"

/// Pill button helper — returns active or inactive class based on condition
let pill isActive = if isActive then pillButtonActive else pillButton

// ── Animations ──

/// Fade in animation
let animateFadeIn = "animate-fade-in"

/// Fade in and slide up animation
let animateFadeInUp = "animate-fade-in-up"

/// Scale in animation
let animateScaleIn = "animate-scale-in"

/// Stagger grid container — children animate in with cascading delay
let staggerGrid = "stagger-grid"

// ── Poster Cards ──

/// Poster card container
let posterCard = "poster-card"

/// Poster image container with 2:3 aspect ratio
let posterImageContainer = "poster-image-container poster-shadow"

/// Poster image element
let posterImage = "poster-image"

/// Poster shine overlay
let posterShine = "poster-shine"

// ── Grids ──

/// Movie grid — responsive columns
let movieGrid = "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-3"

/// Movie grid medium — more columns, smaller cards
let movieGridMedium = "grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-7 xl:grid-cols-8 gap-2"

/// Dashboard stats grid
let statsGrid = "grid grid-cols-2 lg:grid-cols-4 gap-3"

/// Friend/catalog card grid
let cardGrid = "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-3"

/// Two-column content layout (detail pages)
let contentGridLeft = "lg:col-span-8"
let contentGridRight = "lg:col-span-4"

// ── Navigation ──

/// Sidebar nav item (base classes)
let navItem = "nav-glow flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all duration-200"

/// Nav item active state
let navItemActive = "active bg-primary/10 text-primary"

/// Nav item inactive state
let navItemInactive = "text-base-content/70 hover:text-base-content hover:bg-base-300/50"

/// Nav item helper — returns full class string based on active state
let navItemClass isActive =
    navItem + " " + (if isActive then navItemActive else navItemInactive)

// ── Stat Cards ──

/// Stat card with subtle glow effect
let statGlow = "stat-glow"

// ── Overlays ──

/// Modal backdrop (full-screen overlay behind modal)
let modalBackdrop = "absolute inset-0 bg-black/30"

/// Modal container (centered, scrollable)
let modalContainer = "fixed inset-0 z-50 flex justify-center items-start pt-[10vh]"

/// Modal panel (the actual dialog box)
let modalPanel = glassOverlay + " overflow-hidden animate-fade-in"
