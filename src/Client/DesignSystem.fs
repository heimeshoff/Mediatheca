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

// ── Typography ──

/// Page title (h1) — large gradient text
let pageTitle = "text-4xl font-display uppercase tracking-wider text-gradient-primary"

/// Section header (h2) — with accent bar convention
let sectionHeader = "text-2xl font-display uppercase tracking-wider"

/// Card title (h3)
let cardTitle = "text-lg font-display uppercase tracking-wider"

/// Subtitle / secondary heading
let subtitle = "text-sm font-display uppercase tracking-wider"

/// Body text
let bodyText = "text-base text-base-content"

/// Secondary text (descriptions, metadata)
let secondaryText = "text-sm text-base-content/70"

/// Muted text (timestamps, labels)
let mutedText = "text-xs text-base-content/50"

/// Faint text (placeholders, hints)
let faintText = "text-xs text-base-content/40"

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
