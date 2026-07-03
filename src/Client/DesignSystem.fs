module Mediatheca.Client.DesignSystem

open Feliz

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

// ── Navigation (§ 4 Sidebar nav — layered rail, design-system-t4b9k) ──

/// Sidebar nav item (base classes) — layout only. Active/inactive layers on
/// top via `navItemActive` / `navItemInactive`.
let navItem = "nav-item flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium"

/// Nav item active state — the ivory "lit lobby placard" layer: raised ivory
/// surface, dark-burgundy ink, gold icon (via `navItemActiveIconClass`), and
/// the concave corner-notch boundary against the rail/content edge.
let navItemActive = "nav-item-active"

/// Nav item inactive state
let navItemInactive = "text-base-content/70 hover:text-base-content hover:bg-base-300/50"

/// Nav item helper — returns full class string based on active state
let navItemClass isActive =
    navItem + " " + (if isActive then navItemActive else navItemInactive)

/// Wraps a nav item's icon when active — flips it to gold, distinct from the
/// ink-colored label. Apply only to the active item's icon wrapper.
let navItemActiveIconClass = "nav-item-active-icon"

/// Top nav group — primary destinations, stacked at the top of the rail.
let navGroupTop = "flex flex-col gap-1"

/// Bottom nav group — pinned to the foot of the rail via `margin-top: auto`.
let navGroupBottom = "flex flex-col gap-1 mt-auto"

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

// ─────────────────────────────────────────────────────────────────────────
// Velvet Lobby component patterns (design-system-h3q8n)
// Typed Feliz compositions for styleguide.md § 3.1, § 3.3, § 4 and Motion.
// Reference the § 1.3-1.6 tokens (spacing/radii/shadows/animation) by name
// via the CSS classes minted in index.css — no hardcoded oklch here.
// ─────────────────────────────────────────────────────────────────────────

// ── Surfaces (§ 3.1 velvet card, § 3.3 media-chrome glass) ──

/// Solid, non-overlay card surface — "velvet card" (§ 3.1). Replaces
/// `.glass-card` for page/card chrome: `surface` background + `line` ring,
/// no blur/translucency. Never use for floating overlays — those stay glass
/// per § 3.2 (ADR-0006, unchanged).
let velvetCard = "velvet-card"

/// Velvet card with the elevated hero shadow (cover art, hero panels).
let velvetCardHero = "velvet-card velvet-card-hero"

/// Narrower glass for small controls floating directly over artwork (§ 3.3)
/// — e.g. a "Change artwork" pill, a play button on a backdrop. An ADDITION
/// alongside § 3.2's mandatory overlay glass, not a replacement.
let mediaChromeGlass = "media-chrome-glass"

// ── Motion primitives (§ 1.6, § 4 Motion) — vocabulary only. Design-system
// owns the keyframes/helpers; BCs decide *where* they fire. ──

/// The gold-leaf foil sweep (~3.2s linear infinite, `--sweep`) — the one
/// animated ornament in the system. Reserved for "In focus" surfaces only
/// (the status badge, and any other In-focus-flagged surface); do not spread
/// it to ordinary elements.
let goldLeafSweep = "gold-sweep"

/// Leave-transition primitive (400ms ease-out fade + collapse, `--duration-slow`)
/// for items leaving a list/queue. Apply this class always; add
/// `leaveTransitionLeaving` right before the item unmounts. *Where* it fires
/// (e.g. a queue item being removed) is BC behavior, not owned here.
let leaveTransition = "leave-transition"

/// Modifier that triggers the leave-transition's collapsed/faded state.
let leaveTransitionLeaving = "leave-transition leave-transition-leaving"

/// Cross-fade primitive (200ms, `--duration-crossfade`) — e.g. a dashboard
/// tab-panel swap. *Where* it fires is BC behavior, not owned here.
let crossFade = "cross-fade"

// ── Status badges (§ 4 Status badges) ──

/// The six-state lifecycle vocabulary the status-badge pattern renders.
/// Generic to the pattern — not `Shared.GameStatus` (which has no `Playing`
/// state and adds `Dismissed`). Mapping a BC's real status enum onto this
/// vocabulary (or vice versa) is a BC-level concern; see the design-system
/// BC README for the discrepancy this surfaced.
type LifecycleStatus =
    | Backlog
    | InFocus
    | Playing
    | Completed
    | Abandoned
    | OnHold

let private statusBadgeClass (status: LifecycleStatus) =
    match status with
    | Backlog -> "status-badge status-badge-backlog"
    | InFocus -> "status-badge status-badge-in-focus " + goldLeafSweep
    | Playing -> "status-badge status-badge-playing"
    | Completed -> "status-badge status-badge-completed"
    | Abandoned -> "status-badge status-badge-abandoned"
    | OnHold -> "status-badge status-badge-on-hold"

let private statusBadgeLabel (status: LifecycleStatus) =
    match status with
    | Backlog -> "Backlog"
    | InFocus -> "In focus"
    | Playing -> "Playing"
    | Completed -> "Completed"
    | Abandoned -> "Abandoned"
    | OnHold -> "On hold"

/// Status badge pill (§ 4 Status badges) — uppercase, `0.14em` tracking, one
/// hue per lifecycle state. "In focus" is the only variant that animates.
let statusBadge (status: LifecycleStatus) : ReactElement =
    Html.span [
        prop.className (statusBadgeClass status)
        prop.text (statusBadgeLabel status)
    ]

// ── Progress meters (§ 4 Progress meters, two kinds) ──

/// Segmented ("film-frame") progress — one bar per episode. `filled` bars
/// render gold, the remaining `total - filled` render `line`-empty.
let progressSegmented (filled: int) (total: int) : ReactElement =
    Html.div [
        prop.className "progress-segmented"
        prop.children [
            for i in 1 .. (max total 1) do
                Html.div [
                    prop.key (string i)
                    prop.className ("progress-segment" + (if i <= filled then " progress-segment-filled" else ""))
                ]
        ]
    ]

/// Continuous progress — single track with a gold-gradient fill. `fraction`
/// is clamped to 0.0-1.0. For time/percent quantities (play time, HLTB).
let progressContinuous (fraction: float) : ReactElement =
    let pct = System.Math.Clamp(fraction, 0.0, 1.0) * 100.0
    Html.div [
        prop.className "progress-continuous"
        prop.children [
            Html.div [
                prop.className "progress-continuous-fill"
                prop.style [ style.width (length.percent pct) ]
            ]
        ]
    ]

// ── Star rating (§ 4 Star rating) ──

/// Five gold stars. `value` is 1-5 (0 = unset). Tap a star to set; tap the
/// currently-set star again to clear — aligns with the rating-dropdown's
/// existing clear affordance. Controlled: caller owns state via `onChange`.
let starRating (value: int) (onChange: int -> unit) : ReactElement =
    Html.div [
        prop.className "flex items-center gap-1"
        prop.children [
            for i in 1 .. 5 do
                Html.button [
                    prop.key (string i)
                    prop.type' "button"
                    prop.className ("text-lg leading-none transition-colors " + (if i <= value then "text-gold" else "text-line"))
                    prop.text "★"
                    prop.onClick (fun _ -> onChange (if i = value then 0 else i))
                ]
        ]
    ]

// ── Section header (§ 4 Section header) ──

/// The editorial section-header signature — italic serif title + optional
/// uppercase eyebrow kicker + a hairline fade rule + an optional right-aligned
/// gold "All N ->" link. Distinct from the plain `sectionHeader` type-scale
/// string above (this is the full structural pattern).
let sectionHeaderPattern (title: string) (eyebrowText: string option) (link: (string * (unit -> unit)) option) : ReactElement =
    Html.div [
        prop.className "flex flex-col gap-1"
        prop.children [
            match eyebrowText with
            | Some e -> Html.span [ prop.className eyebrow; prop.text e ]
            | None -> ()
            Html.div [
                prop.className "flex items-center gap-4"
                prop.children [
                    Html.h2 [ prop.className sectionHeader; prop.text title ]
                    Html.div [ prop.className "section-rule" ]
                    match link with
                    | Some (label, onClickHandler) ->
                        Html.button [
                            prop.type' "button"
                            prop.className "text-gold text-sm font-sans whitespace-nowrap hover:underline"
                            prop.text label
                            prop.onClick (fun _ -> onClickHandler ())
                        ]
                    | None -> ()
                ]
            ]
        ]
    ]

// ── List row (§ 4 Recently-played list) ──

/// Recently-played style row — thumb, title, mono timestamp/duration,
/// hairline separators (`.list-row` handles the bottom hairline).
let listRow (thumb: ReactElement) (title: string) (meta: string) : ReactElement =
    Html.div [
        prop.className "list-row flex items-center gap-3 py-3"
        prop.children [
            thumb
            Html.span [ prop.className (bodyText + " flex-1 truncate"); prop.text title ]
            Html.span [ prop.className dataText; prop.text meta ]
        ]
    ]

// ── 3c list-page chrome (§ 2 Typography, design-system-snpnv) ──
// Dense poster-grid captions, list-page header, and filter pills — a
// deliberately *sans* voice distinct from `cardTitle` (serif, velvet cards).

/// Grid card title — dense poster-grid / filmstrip caption. Instrument Sans
/// (NOT Instrument Serif like `cardTitle`) — the poster grid reads better
/// upright at small sizes than in the serif-adjacent card voice.
let gridCaptionTitle = "text-[12px] font-sans font-semibold leading-[1.3] text-base-content"

/// Grid meta — the muted second line beneath a grid caption title, e.g.
/// "2024 · rec. by Sam".
let gridCaptionMeta = "text-[10.5px] font-sans text-ink-muted"

/// Grid caption pair composition — title + meta stacked, as seen beneath
/// poster-grid cards and filmstrip tiles.
let gridCaptionPair (title: string) (meta: string) : ReactElement =
    Html.div [
        prop.className "flex flex-col gap-0.5"
        prop.children [
            Html.span [ prop.className gridCaptionTitle; prop.text title ]
            Html.span [ prop.className gridCaptionMeta; prop.text meta ]
        ]
    ]

/// List-page header title — Instrument Serif, fixed 34px (distinct from
/// `pageTitle`'s responsive 4xl/5xl hero scale; this is the dense list-page size).
let listPageHeaderTitle = "text-[34px] font-display leading-none text-base-content"

/// List-page header count — Spline Sans Mono, baseline-paired with the title,
/// e.g. "148 titles · 12 in focus".
let listPageHeaderCount = "text-[11px] font-mono text-ink-muted"

/// List-page header pattern — serif title baseline-aligned with a mono count
/// line (3c: "148 titles · 12 in focus").
let listPageHeaderPattern (title: string) (count: string) : ReactElement =
    Html.div [
        prop.className "flex items-baseline gap-[14px]"
        prop.children [
            Html.h1 [ prop.className listPageHeaderTitle; prop.text title ]
            Html.span [ prop.className listPageHeaderCount; prop.text count ]
        ]
    ]

/// Filter pill — active/inactive toggle chip for list-page filter bars.
/// Active = weight 600 dark ink on gold fill; inactive = ink-secondary with a
/// hairline border. Backed by `.filter-pill`/`.filter-pill-active`/`.filter-pill-inactive`
/// in index.css.
let filterPill (label: string) (isActive: bool) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.type' "button"
        prop.className ("filter-pill " + (if isActive then "filter-pill-active" else "filter-pill-inactive"))
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

// ── In-focus poster frame (§ 4 Poster grid) ──

/// Wraps any poster/card element with the reusable gold-frame "In focus"
/// treatment — the visual sibling of the "In focus" status badge. Every BC's
/// poster grid should render In-focus items through this wrapper.
let inFocusFrame (child: ReactElement) : ReactElement =
    Html.div [
        prop.className "in-focus-frame relative"
        prop.children [ child ]
    ]

// ── Movies filmstrip (§ 4 Movies filmstrip) ──

type FilmstripItem = {
    PosterRef: string option
    Title: string
    Meta: string
}

/// Filmstrip movie row — black sprocket-holed well with a row of flex-1
/// posters (196px tall, 3a proportions) filling its full width edge to
/// edge, captions (title + meta, e.g. runtime/"rec. by") beneath the strip.
let filmstripRow (items: FilmstripItem list) : ReactElement =
    Html.div [
        prop.className "flex flex-col"
        prop.children [
            Html.div [
                prop.className "filmstrip"
                prop.children [
                    Html.div [ prop.className "filmstrip-sprocket mb-[7px]" ]
                    Html.div [
                        prop.className "flex gap-2.5 px-4"
                        prop.children [
                            for item in items do
                                Html.div [
                                    prop.key item.Title
                                    prop.className "flex-1 h-[196px] rounded-[var(--radius-poster)] bg-base-300 overflow-hidden"
                                    prop.children [
                                        match item.PosterRef with
                                        | Some ref ->
                                            Html.img [
                                                prop.src $"/images/{ref}"
                                                prop.alt item.Title
                                                prop.className "w-full h-full object-cover"
                                            ]
                                        | None ->
                                            Html.div [ prop.className "w-full h-full bg-gradient-to-br from-base-300 to-base-200" ]
                                    ]
                                ]
                        ]
                    ]
                    Html.div [ prop.className "filmstrip-sprocket mt-[7px]" ]
                ]
            ]
            Html.div [
                prop.className "flex gap-2.5 px-4 pt-[10px]"
                prop.children [
                    for item in items do
                        Html.div [
                            prop.key (item.Title + "-caption")
                            prop.className "flex-1 flex flex-col gap-0.5 min-w-0"
                            prop.children [
                                Html.span [
                                    prop.className "font-sans text-[12px] font-semibold leading-[1.35] text-base-content truncate"
                                    prop.text item.Title
                                ]
                                Html.span [
                                    prop.className "font-sans text-[10.5px] text-ink-muted truncate"
                                    prop.text item.Meta
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]

// ── Secondary media card (§ 4 Secondary series/entity card) ──

type SecondaryCardProps = {
    Title: string
    NextLabel: string
    ProgressFilled: int
    ProgressTotal: int
}

/// Compact poster-top card — serif title, "Next: SxEy" line, segmented
/// mini-progress. Velvet card with the standard `--shadow-card`.
let secondaryMediaCard (props: SecondaryCardProps) : ReactElement =
    Html.div [
        prop.className (velvetCard + " p-3 flex flex-col gap-2 w-40")
        prop.children [
            Html.h3 [ prop.className cardTitle; prop.text props.Title ]
            Html.p [ prop.className mutedText; prop.text props.NextLabel ]
            progressSegmented props.ProgressFilled props.ProgressTotal
        ]
    ]

// ── Cinematic hero card (§ 4 TV "Next up" hero) ──

type HeroCardProps = {
    Title: string
    InFocus: bool
    WatchedWith: string list
    ProgressFilled: int
    ProgressTotal: int
    Rating: int
    OnRatingChange: int -> unit
    OnWatchClick: unit -> unit
}

/// The cinematic hero card — backdrop gradient panel (velvet-card-hero) with
/// the In-focus badge top-left, serif title, overlapping watched-with avatar
/// stack, segmented episode progress, star rating, and the gold "Watch" pill.
let heroCard (props: HeroCardProps) : ReactElement =
    Html.div [
        prop.className (velvetCardHero + " relative overflow-hidden p-5 flex flex-col justify-end gap-3 min-h-[220px]")
        prop.children [
            if props.InFocus then
                statusBadge InFocus
                |> fun badge ->
                    Html.div [
                        prop.className "absolute top-4 left-4"
                        prop.children [ badge ]
                    ]
            Html.div [
                prop.className "flex flex-col gap-2"
                prop.children [
                    Html.h3 [ prop.className pageTitle; prop.text props.Title ]
                    if not props.WatchedWith.IsEmpty then
                        Html.div [
                            prop.className "flex items-center -space-x-3"
                            prop.children [
                                for initial in props.WatchedWith do
                                    Html.div [
                                        prop.key initial
                                        prop.className "w-8 h-8 rounded-full ring-2 ring-base-100 bg-line flex items-center justify-center text-xs font-sans text-ink-secondary"
                                        prop.text initial
                                    ]
                            ]
                        ]
                    progressSegmented props.ProgressFilled props.ProgressTotal
                    Html.div [
                        prop.className "flex items-center justify-between mt-1"
                        prop.children [
                            starRating props.Rating props.OnRatingChange
                            Html.button [
                                prop.type' "button"
                                prop.className "bg-gold text-primary-content rounded-full px-4 py-2 text-sm font-sans font-semibold"
                                prop.text "▶ Watch"
                                prop.onClick (fun _ -> props.OnWatchClick ())
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
