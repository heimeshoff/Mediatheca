module Mediatheca.Client.DesignSystem

open Feliz

// Design system composition helpers.
// Components should use these instead of hardcoding class strings.
// CSS custom properties (--color-paper, --space-*, etc.) are defined in index.css.

// ── Paper Overlay (ADR-0016: opaque elevation, replaces glassmorphism) ──

/// Paper overlay — solid opaque fill (lighter than the page) + elevation
/// shadow + a subtle line ring. The floating-surface vocabulary: dropdowns,
/// popovers, modals. Distinct from `velvetCard` (page chrome, flush with the
/// page, ring-only elevation) — overlays float above the page with a true
/// drop shadow. No translucency, no backdrop-filter (ADR-0016 supersedes
/// ADR-0006's mandatory glassmorphism).
let paperOverlay = "paper-overlay"

/// Paper dropdown (rating dropdown, action menus, context menus) — same
/// paper-overlay material with dropdown-specific padding/min-width/animation.
let paperDropdown = "rating-dropdown"

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

// ── Navigation (§ 4 Sidebar nav — dir 3a active tab, design-system-grtw7;
//    supersedes the ADR-0013 ivory placard + concave corner-notch) ──

/// Sidebar nav item (base classes) — dir 3a item metrics (layout only; font
/// size/weight/color live in `navItemActive` / `navItemInactive`, index.css,
/// so the bottom group's smaller scale, `navGroupBottom`, can cascade over
/// them predictably).
let navItem = "nav-item flex items-center gap-[11px] px-3 py-[9px] rounded-lg font-sans"

/// Nav item active state — dir 3a's burgundy fill (`--color-nav-active-fill`)
/// + gold inset-left bar (`--ring-active`). Reverted from the ADR-0013 ivory
/// placard + concave corner-notch boundary (see the superseding ADR).
let navItemActive = "nav-item-active"

/// Nav item inactive state — muted ink (`--color-ink-muted`), no fill,
/// subtle hover background.
let navItemInactive = "nav-item-inactive hover:text-base-content hover:bg-base-300/50"

/// Nav item helper — returns full class string based on active state
let navItemClass isActive =
    navItem + " " + (if isActive then navItemActive else navItemInactive)

/// Wraps every nav item's icon (dir 3a: 12px top group / 11px bottom group,
/// via `navGroupBottom`'s CSS scope; muted by default). Pass
/// `navItemActiveIconClass` for the active item instead — flips it to gold.
let navItemIconClass = "nav-item-icon"
let navItemActiveIconClass = "nav-item-icon nav-item-active-icon"

/// Top nav group — primary destinations, stacked at the top of the rail.
let navGroupTop = "flex flex-col gap-[2px]"

/// Bottom nav group — Events/Settings, one step smaller (dir 3a: 12px
/// labels, 11px icons, `--color-nav-bottom-muted`), pinned to the foot of
/// the rail via `margin-top: auto`.
let navGroupBottom = "nav-group-bottom flex flex-col gap-[2px] mt-auto"

/// Tagline under the wordmark (dir 3a): "Where entertainment lives".
let navTagline = "text-[8.5px] font-sans uppercase tracking-[0.26em] text-ink-faint mt-[3px] whitespace-nowrap"

// ── Underline tab (§ 4 dir 3a header tabs, design-system-k9p3v) ──
// The header-tab sibling of the dir-3a sidebar nav above: a text tab with a
// gold underline under the active tab, no filled-pill / bordered-button
// chrome. The caller renders its own `Html.button`s and owns the tab list +
// click wiring; this composition owns only the look.

/// Underline tab (base classes) — layout + reset only (no fill, no border);
/// the gold underline bar itself is drawn by `.underline-tab-active::after`
/// in index.css (`--color-gold`, the same token as the sidebar's active
/// icon/inset-bar — no new colour introduced).
let underlineTab = "underline-tab relative bg-transparent border-0 px-1 pb-[10px] font-sans text-sm cursor-pointer transition-colors duration-200"

/// Underline tab active state — full-ink label (weight 600) + gold underline.
let underlineTabActive = "underline-tab-active text-base-content font-semibold"

/// Underline tab inactive state — muted ink, hovers toward the active ink as
/// an affordance hint (mirrors `navItemInactive`'s hover language).
let underlineTabInactive = "underline-tab-inactive text-ink-muted hover:text-base-content"

/// Underline tab helper — returns full class string based on active state,
/// same isActive-branch shape as `navItemClass` / `filterPill`.
let underlineTabClass isActive =
    underlineTab + " " + (if isActive then underlineTabActive else underlineTabInactive)

// ── Stat Cards ──

/// Stat card with subtle glow effect
let statGlow = "stat-glow"

// ── Overlays ──

/// Modal backdrop (full-screen overlay behind modal)
let modalBackdrop = "absolute inset-0 bg-black/30"

/// Modal container (centered, scrollable)
let modalContainer = "fixed inset-0 z-50 flex justify-center items-start pt-[10vh]"

/// Modal panel (the actual dialog box)
let modalPanel = paperOverlay + " overflow-hidden animate-fade-in"

// ─────────────────────────────────────────────────────────────────────────
// Velvet Lobby component patterns (design-system-h3q8n)
// Typed Feliz compositions for styleguide.md § 3.1, § 3.3, § 4 and Motion.
// Reference the § 1.3-1.6 tokens (spacing/radii/shadows/animation) by name
// via the CSS classes minted in index.css — no hardcoded oklch here.
// ─────────────────────────────────────────────────────────────────────────

// ── Surfaces (§ 3.1 velvet card) ──

/// Solid, non-overlay card surface — "velvet card" (§ 3.1). Page/card
/// chrome: `surface` background + `line` ring, no blur/translucency. Never
/// use for floating overlays — those use `paperOverlay`/`paperDropdown`
/// (ADR-0016). The old § 3.3 "media-chrome glass" variant for small
/// controls over artwork was retired by ADR-0016 — such controls now use
/// `paperOverlay` (or a plain solid fill) directly.
let velvetCard = "velvet-card"

/// Velvet card with the elevated hero shadow (cover art, hero panels).
let velvetCardHero = "velvet-card velvet-card-hero"

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
/// poster grid should render In-focus items through this wrapper. Renders as
/// an animated sweeping gold-gradient border (`.in-focus-frame`) with an
/// inner clipping layer (`.in-focus-frame-inner`) — signature unchanged from
/// the earlier static-ring version, so existing call sites are unaffected.
let inFocusFrame (child: ReactElement) : ReactElement =
    Html.div [
        prop.className "in-focus-frame relative"
        prop.children [
            Html.div [
                prop.className "in-focus-frame-inner"
                prop.children [ child ]
            ]
        ]
    ]

/// Compact on-poster "✦ Focus" pill (§ 4 Poster grid, design-system-fq3vp) —
/// the 3c grid-badge variant: small, top-left, directly on the artwork.
/// Deliberately a SOLID gold fill, not `goldLeafSweep` -- it always co-occurs
/// with `inFocusFrame`'s animated sweeping border directly behind it, so a
/// second sweep on an ~8.5px pill would compete rather than read as life (see
/// § 4 Motion discipline). A separate composition from `statusBadge InFocus`
/// (list rows, hero, detail) so the two can diverge freely -- render as a
/// sibling positioned over a poster, e.g.:
///   Html.div [ prop.className "relative"; prop.children [ poster; inFocusPill ] ]
let inFocusPill : ReactElement =
    Html.span [
        prop.className "in-focus-pill"
        prop.text "✦ Focus"
    ]

// ── Movies filmstrip (§ 4 Movies filmstrip) ──

type FilmstripItem = {
    /// Stable React key — prefer a real identifier (e.g. slug) over `Title`
    /// so items with the same title don't collide.
    Key: string
    PosterRef: string option
    Title: string
    Meta: string
    /// Anchor href for the tile, when the tile navigates somewhere. Paired
    /// with `OnNavigate` (SPA-style client nav); when both are `None` the
    /// tile renders as a plain, non-interactive `div`.
    Href: string option
    OnNavigate: (unit -> unit) option
    /// Fully-rendered, self-positioned (`absolute top-1.5 left-1.5 ...`)
    /// InFocus badge, or `None`. Rendered as-is by the caller, matching the
    /// `JellyfinButton` slot below, so this module doesn't need `Icons`.
    InFocusBadge: ReactElement option
    /// Fully-rendered, self-positioned (`absolute bottom-2 right-2 ...`)
    /// Jellyfin play button, or `None` when the item isn't on Jellyfin.
    /// Rendered as-is by the caller so this module doesn't need to know
    /// about icons/URLs (`nextEpisodeHeroCard` precedent).
    JellyfinButton: ReactElement option
}

/// Filmstrip movie row — black sprocket-holed well with a row of
/// equal-width posters (196px tall, 3a proportions), captions (title +
/// meta, e.g. runtime/"rec. by") beneath the strip. Overflow = fill +
/// scroll hybrid: posters grow to fill the available width (`flex-grow`)
/// but never shrink below their 3a proportion (`flex-shrink-0`); once the
/// row's natural content width exceeds the section width, the whole thing
/// -- sprockets, posters, and captions together, sized via a shared
/// `w-max` block -- becomes one horizontally-scrollable piece (the single
/// `overflow-x-auto` ancestor wrapping everything), never a static frame
/// around an inner scroller. Interactive bits (`Href`/`OnNavigate`,
/// `InFocusBadge`, `JellyfinButton`) are caller-supplied per the
/// `nextEpisodeHeroCard` precedent, so this module stays decoupled from
/// `Feliz.Router` / `Icons` / URL helpers.
let filmstripRow (items: FilmstripItem list) : ReactElement =
    let tileWrapperClass = "flex-[1_0_130px] group"
    let posterBoxClass = "relative w-full h-[196px] rounded-[var(--radius-poster)] bg-base-300 overflow-hidden transition-transform duration-300 group-hover:scale-105"
    let posterTile (item: FilmstripItem) =
        Html.div [
            prop.className posterBoxClass
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
                match item.InFocusBadge with
                | Some badge -> badge
                | None -> ()
                match item.JellyfinButton with
                | Some button -> button
                | None -> ()
                // Light shine on hover — same `.poster-shine` overlay the game
                // poster cards use; the `.group:hover .poster-shine` rule in
                // index.css fades it in via the tile's Tailwind `group`.
                Html.div [ prop.className posterShine ]
            ]
        ]
    Html.div [
        prop.className "overflow-x-auto"
        prop.children [
            Html.div [
                prop.className "flex flex-col w-max min-w-full"
                prop.children [
                    Html.div [
                        prop.className "filmstrip"
                        prop.children [
                            Html.div [ prop.className "filmstrip-sprocket mb-[7px]" ]
                            Html.div [
                                prop.className "flex gap-2.5 px-4"
                                prop.children [
                                    for item in items do
                                        match item.Href, item.OnNavigate with
                                        | None, None ->
                                            Html.div [
                                                prop.key item.Key
                                                prop.className tileWrapperClass
                                                prop.children [ posterTile item ]
                                            ]
                                        | href, onNavigate ->
                                            Html.a [
                                                prop.key item.Key
                                                prop.href (href |> Option.defaultValue "#")
                                                prop.className (tileWrapperClass + " cursor-pointer")
                                                prop.onClick (fun e ->
                                                    e.preventDefault()
                                                    onNavigate |> Option.iter (fun f -> f ()))
                                                prop.children [ posterTile item ]
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
                                    prop.key (item.Key + "-caption")
                                    prop.className "flex-[1_0_130px] flex flex-col gap-0.5 min-w-0"
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

// ── Next episode hero card (Dashboard "Next episode" — cinematic backdrop cards) ──

/// One watched-with friend, reduced to the two fields the card renders
/// (image ref + name) — kept primitive so this module stays decoupled from
/// the `FriendRef` shared type, matching the `FilmstripItem` precedent above.
type NextEpisodeHeroFriend = {
    ImageRef: string option
    Name: string
    /// Link target for the friend's detail page — a real href for link
    /// semantics (hover, middle-click, copy-link).
    Href: string
    /// SPA navigation to the friend's page, invoked on click after the
    /// component cancels the default navigation and the card click-through.
    OnClick: unit -> unit
}

type NextEpisodeHeroCardProps = {
    SeriesName: string
    /// "SxxExx: title" — `None` when there is no next-up episode to label.
    EpisodeLabel: string option
    BackdropRef: string option
    /// Fallback background when `BackdropRef` is `None`.
    PosterRef: string option
    InFocus: bool
    ProgressFilled: int
    ProgressTotal: int
    WatchedWith: NextEpisodeHeroFriend list
    /// Fully-rendered, self-positioned (`absolute top-3 right-3 ...`) Jellyfin
    /// play button, or `None` when the episode isn't on Jellyfin. Rendered as-is
    /// by the caller so this module doesn't need to know about icons/URLs.
    JellyfinButton: ReactElement option
}

/// Cinematic "Next episode" hero card — the repeated, real-data variant of the
/// styleguide's single-specimen `heroCard`. Backdrop fills the canvas, a bottom
/// scrim overlay carries the series name, episode label, segmented progress, and
/// watched-with friends (image + name, each linking to the friend's page), and
/// the caller-supplied Jellyfin button (if any) sits top-right.
let nextEpisodeHeroCard (props: NextEpisodeHeroCardProps) : ReactElement =
    let backgroundRef = props.BackdropRef |> Option.orElse props.PosterRef
    Html.div [
        prop.className (velvetCardHero + " relative w-full aspect-video overflow-hidden transition-transform duration-300 group-hover:scale-105")
        prop.children [
            match backgroundRef with
            | Some ref ->
                Html.img [
                    prop.src $"/images/{ref}"
                    prop.alt props.SeriesName
                    prop.className "absolute inset-0 w-full h-full object-cover"
                ]
            | None ->
                Html.div [ prop.className "absolute inset-0 bg-gradient-to-br from-primary/20 to-base-300" ]

            // Bottom scrim so the overlay text stays legible over the backdrop.
            Html.div [ prop.className "absolute inset-0 bg-gradient-to-t from-black/90 via-black/35 to-transparent" ]

            // Light shine on hover — same `.poster-shine` overlay the game
            // poster cards use; the `.group:hover .poster-shine` rule in
            // index.css fades it in via the card wrapper's Tailwind `group`.
            Html.div [ prop.className posterShine ]

            if props.InFocus then
                Html.div [
                    prop.className "absolute top-3 left-3 z-10"
                    prop.children [ statusBadge InFocus ]
                ]

            Html.div [
                prop.className "absolute bottom-0 left-0 right-0 p-3 sm:p-4 flex flex-col gap-1.5 z-[1]"
                prop.children [
                    Html.h3 [
                        prop.className "text-lg sm:text-xl font-display text-white/95 truncate"
                        prop.text props.SeriesName
                    ]
                    match props.EpisodeLabel with
                    | Some label ->
                        Html.p [
                            prop.className "text-xs font-mono text-white/70 truncate"
                            prop.text label
                        ]
                    | None -> ()
                    progressSegmented props.ProgressFilled props.ProgressTotal
                    if not props.WatchedWith.IsEmpty then
                        Html.div [
                            prop.className "flex items-center gap-1.5 flex-wrap"
                            prop.children [
                                for friend in props.WatchedWith do
                                    Html.a [
                                        prop.key friend.Name
                                        prop.href friend.Href
                                        prop.onClick (fun e ->
                                            e.preventDefault()
                                            e.stopPropagation()
                                            friend.OnClick())
                                        prop.className "inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-white/10 text-white/80 hover:bg-white/20 hover:text-white transition-colors cursor-pointer"
                                        prop.children [
                                            match friend.ImageRef with
                                            | Some img ->
                                                Html.img [
                                                    prop.src $"/images/{img}"
                                                    prop.alt friend.Name
                                                    prop.className "w-3.5 h-3.5 rounded-full object-cover"
                                                ]
                                            | None -> ()
                                            Html.span [ prop.text friend.Name ]
                                        ]
                                    ]
                            ]
                        ]
                ]
            ]

            match props.JellyfinButton with
            | Some button -> button
            | None -> ()
        ]
    ]
