module Mediatheca.Client.Pages.StyleGuide.Views

open Feliz
open Mediatheca.Client
open Mediatheca.Client.Pages.StyleGuide.Types
open Mediatheca.Client.Components
open Mediatheca.Shared

// ── Shared helpers ──

let private sectionTitle (title: string) =
    Html.h2 [
        prop.className (DesignSystem.sectionHeader + " mb-6")
        prop.text title
    ]

/// A "specimen" showing the rendered element and its reference
let private specimen (label: string) (reference: string) (element: ReactElement) =
    Html.div [
        prop.className "flex flex-col gap-2 p-4 rounded-lg bg-base-200/30 border border-base-content/5"
        prop.children [
            element
            Html.div [
                prop.className "flex items-center gap-2 mt-2"
                prop.children [
                    Html.code [
                        prop.className "text-xs bg-base-300/50 px-2 py-1 rounded font-mono text-primary/80"
                        prop.text reference
                    ]
                    Html.span [
                        prop.className DesignSystem.faintText
                        prop.text label
                    ]
                ]
            ]
        ]
    ]

/// Annotation paragraph for design decisions
let private decision (text: string) =
    Html.p [
        prop.className (DesignSystem.secondaryText + " max-w-3xl leading-relaxed")
        prop.text text
    ]

/// Small label for sub-sections
let private subheading (text: string) =
    Html.h3 [
        prop.className (DesignSystem.cardTitle + " mt-8 mb-4")
        prop.text text
    ]

/// Code block for showing usage examples
let private codeBlock (code: string) =
    Html.pre [
        prop.className "bg-base-300/40 border border-base-content/5 rounded-lg p-4 text-sm font-mono text-base-content/80 overflow-x-auto"
        prop.children [
            Html.code [
                prop.text code
            ]
        ]
    ]

/// Decision callout box
let private decisionBox (title: string) (accepted: string) (rejected: string) =
    Html.div [
        prop.className "bg-base-200/20 border-l-4 border-primary/40 p-4 rounded-r-lg max-w-3xl"
        prop.children [
            Html.p [
                prop.className (DesignSystem.subtitle + " text-primary/90 mb-2")
                prop.text title
            ]
            Html.p [
                prop.className (DesignSystem.secondaryText + " mb-2")
                prop.text ("Chosen: " + accepted)
            ]
            Html.p [
                prop.className (DesignSystem.mutedText + " italic")
                prop.text ("Rejected: " + rejected)
            ]
        ]
    ]

// ── Section: Overview ──

let private overviewSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Overview"
            Html.p [
                prop.className "text-base-content/70 max-w-2xl leading-relaxed"
                prop.text "The Mediatheca design system. This page serves as the single source of truth for all design tokens, component definitions, and visual conventions used throughout the application."
            ]

            subheading "Two-Layer Architecture"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4 max-w-4xl"
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.velvetCard + " p-5 rounded-xl border border-base-content/5")
                        prop.children [
                            Html.h4 [
                                prop.className (DesignSystem.subtitle + " text-primary mb-2")
                                prop.text "Layer 1: CSS Custom Properties"
                            ]
                            Html.p [
                                prop.className DesignSystem.secondaryText
                                prop.text "Raw design tokens defined in index.css under :root. Paper-overlay fill/shadow, spacing scale, border radii, animation durations, shadows, and typography tracking. These are the primitive values."
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-base-content/50 bg-base-300/30 p-2 rounded"
                                prop.text "--shadow-paper: 0 10px 28px -8px oklch(0 0 0 / 0.55) ...;"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className (DesignSystem.velvetCard + " p-5 rounded-xl border border-base-content/5")
                        prop.children [
                            Html.h4 [
                                prop.className (DesignSystem.subtitle + " text-primary mb-2")
                                prop.text "Layer 2: F# DesignSystem Module"
                            ]
                            Html.p [
                                prop.className DesignSystem.secondaryText
                                prop.text "Typed compositions of Tailwind/DaisyUI classes in DesignSystem.fs. Components reference these instead of hardcoding class strings, enabling consistent refactoring and discoverability."
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-base-content/50 bg-base-300/30 p-2 rounded"
                                prop.text "DesignSystem.velvetCard"
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Usage Example"

            codeBlock """// In a component:
Html.div [
    prop.className DesignSystem.velvetCard
    prop.children [
        Html.h2 [
            prop.className DesignSystem.sectionHeader
            prop.text "My Section"
        ]
        Html.p [
            prop.className DesignSystem.bodyText
            prop.text "Content here..."
        ]
    ]
]"""
        ]
    ]

// ── Section: Typography ──

let private typographySection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Typography"

            decision "Three families, each with one job -- the \"Velvet Lobby\" editorial programme. Instrument Serif (font-display) for display & titles, mixed case; its italic cut is the signature \"voice\" for section headers and the theca wordmark. Instrument Sans (font-sans) for body, labels, and UI. Spline Sans Mono (font-mono) is the new \"data\" typeface -- dates, durations, counts, ids."

            subheading "The Italic Voice"

            Html.div [
                prop.className "flex flex-col gap-2 p-5 rounded-lg bg-base-200/30 border border-base-content/5 max-w-xl"
                prop.children [
                    Html.h2 [
                        prop.className DesignSystem.sectionHeader
                        prop.text "Next up"
                    ]
                    Html.p [
                        prop.className DesignSystem.eyebrow
                        prop.text "Continue watching"
                    ]
                    Html.p [
                        prop.className (DesignSystem.secondaryText + " mt-2")
                        prop.text "Section headers (\"Next up\", \"In focus\", \"Recently played\") and the \"theca\" half of the wordmark always render in Instrument Serif italic -- the one recurring editorial flourish. Everything else stays upright."
                    ]
                    Html.code [
                        prop.className "text-xs font-mono text-primary/70 mt-1"
                        prop.text "DesignSystem.sectionHeader -- font-display italic"
                    ]
                ]
            ]

            subheading "Type Scale"

            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    specimen "Page title -- hero / entity name" "DesignSystem.pageTitle" (
                        Html.h1 [
                            prop.className DesignSystem.pageTitle
                            prop.text "Page Title"
                        ]
                    )
                    specimen "Section header -- the italic serif voice" "DesignSystem.sectionHeader" (
                        Html.h2 [
                            prop.className DesignSystem.sectionHeader
                            prop.text "Section Header"
                        ]
                    )
                    specimen "Card title -- h3 for card headings" "DesignSystem.cardTitle" (
                        Html.h3 [
                            prop.className DesignSystem.cardTitle
                            prop.text "Card Title"
                        ]
                    )
                    specimen "Eyebrow -- category label above a section (alias: subtitle)" "DesignSystem.eyebrow" (
                        Html.span [
                            prop.className DesignSystem.eyebrow
                            prop.text "Eyebrow Label"
                        ]
                    )
                    specimen "Body text -- default readable text (secondary ink)" "DesignSystem.bodyText" (
                        Html.p [
                            prop.className DesignSystem.bodyText
                            prop.text "Body text used for paragraphs and general content. Instrument Sans at full-strength ink."
                        ]
                    )
                    specimen "Secondary text -- descriptions, metadata (ink-secondary step)" "DesignSystem.secondaryText" (
                        Html.p [
                            prop.className DesignSystem.secondaryText
                            prop.text "Secondary text for descriptions, metadata, and supporting information."
                        ]
                    )
                    specimen "Muted text -- timestamps, labels (ink-muted step, alias: metaText)" "DesignSystem.mutedText" (
                        Html.p [
                            prop.className DesignSystem.mutedText
                            prop.text "Muted text for timestamps, labels, and tertiary information."
                        ]
                    )
                    specimen "Faint text -- placeholders, hints (ink-faint step)" "DesignSystem.faintText" (
                        Html.p [
                            prop.className DesignSystem.faintText
                            prop.text "Faint text for placeholders, hints, and the lowest-priority information."
                        ]
                    )
                    specimen "Data text -- dates, durations, counts, ids (new mono role)" "DesignSystem.dataText" (
                        Html.p [
                            prop.className DesignSystem.dataText
                            prop.text "2026-07-02 · 44 MIN · #A1B2C3"
                        ]
                    )
                ]
            ]

            subheading "3c List-Page Chrome"

            decision "Dense list-page tiers from direction 3c (Movies Grid) -- additions alongside the editorial scale above, not renames. The poster-grid caption is a deliberately sans voice (distinct from cardTitle's serif), since a dense grid of small captions reads better upright than in the card voice."

            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    specimen "List-page header -- serif title baseline-paired with a mono count" "DesignSystem.listPageHeaderPattern" (
                        DesignSystem.listPageHeaderPattern "Movies" "148 titles · 12 in focus"
                    )
                    specimen "Filter pills -- active (gold fill) / inactive (line border)" "DesignSystem.filterPill" (
                        Html.div [
                            prop.className "flex items-center gap-2"
                            prop.children [
                                DesignSystem.filterPill "All" true ignore
                                DesignSystem.filterPill "In focus" false ignore
                                DesignSystem.filterPill "Completed" false ignore
                            ]
                        ]
                    )
                    specimen "Grid caption pair -- dense poster-grid / filmstrip caption (sans, not cardTitle)" "DesignSystem.gridCaptionPair" (
                        Html.div [
                            prop.className "w-24"
                            prop.children [ DesignSystem.gridCaptionPair "Blade Runner" "1982 · rec. by Sam" ]
                        ]
                    )
                ]
            ]

            subheading "Font Families"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-3 gap-4"
                prop.children [
                    Html.div [
                        prop.className "p-5 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [
                                prop.className "text-3xl font-display italic mb-2"
                                prop.text "Instrument Serif"
                            ]
                            Html.p [
                                prop.className "font-display text-base-content/70 text-sm"
                                prop.text "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz"
                            ]
                            Html.p [
                                prop.className "font-display text-base-content/70 text-sm"
                                prop.text "0123456789"
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-primary/70"
                                prop.text "font-display -- display, titles; italic = section voice"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "p-5 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [
                                prop.className "text-3xl font-sans mb-2"
                                prop.text "Instrument Sans"
                            ]
                            Html.p [
                                prop.className "font-sans text-base-content/70 text-sm"
                                prop.text "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                            ]
                            Html.p [
                                prop.className "font-sans text-base-content/70 text-sm"
                                prop.text "0123456789"
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-primary/70"
                                prop.text "font-sans -- body, labels, UI (weights 400-700)"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "p-5 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [
                                prop.className "text-3xl font-mono mb-2"
                                prop.text "Spline Mono"
                            ]
                            Html.p [
                                prop.className "font-mono text-base-content/70 text-sm"
                                prop.text "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                            ]
                            Html.p [
                                prop.className "font-mono text-base-content/70 text-sm"
                                prop.text "0123456789"
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-primary/70"
                                prop.text "font-mono -- data: dates, durations, counts, ids"
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Font Pairing"
                "Instrument Serif (display, mixed case, italic = section voice) + Instrument Sans (body, labels, UI) + Spline Sans Mono (data). The contrast between an editorial serif, a clean sans body, and a tabular mono for data creates hierarchy without shouting -- the library reads like a cinema listing, not a dashboard."
                "A single font for everything (too monotone, no hierarchy). Condensed all-caps display headings, the previous Oswald treatment (too utilitarian for the velvet direction)."
        ]
    ]

// ── Section: Colors ──

let private colorSwatch (name: string) (bgClass: string) (textClass: string) =
    Html.div [
        prop.className "flex flex-col items-center gap-2"
        prop.children [
            Html.div [
                prop.className (bgClass + " w-20 h-20 rounded-xl border border-base-content/10 shadow-sm")
            ]
            Html.span [
                prop.className ("text-xs font-mono " + textClass)
                prop.text name
            ]
        ]
    ]

/// A palette swatch labeled with its literal oklch value (Velvet Lobby primitives).
let private oklchSwatch (name: string) (oklchLabel: string) (bgClass: string) =
    Html.div [
        prop.className "flex flex-col items-center gap-2"
        prop.children [
            Html.div [
                prop.className (bgClass + " w-20 h-20 rounded-xl border border-base-content/10 shadow-sm")
            ]
            Html.span [
                prop.className "text-xs font-sans text-base-content/70"
                prop.text name
            ]
            Html.span [
                prop.className "text-[10px] font-mono text-ink-faint"
                prop.text oklchLabel
            ]
        ]
    ]

let private colorsSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Colors"

            decision "\"Velvet Lobby\": velvet-black surfaces, ivory-serif ink, and a single gold accent used like foil. All colors use the oklch color space for perceptually uniform, vibrant results against the dark base -- and to let posters/backdrops carry the vivid hues."

            subheading "Velvet Lobby Primitives"

            Html.div [
                prop.className "flex flex-wrap gap-4"
                prop.children [
                    oklchSwatch "bg" "oklch(0.16 0.028 20)" "bg-base-200"
                    oklchSwatch "surface" "oklch(0.20 0.03 22)" "bg-base-100"
                    oklchSwatch "line" "oklch(0.32 0.04 28)" "bg-line"
                    oklchSwatch "gold" "oklch(0.80 0.12 82)" "bg-gold"
                    oklchSwatch "spotlight" "oklch(0.30 0.06 30)" "bg-spotlight"
                    oklchSwatch "ink" "oklch(0.93 0.012 60)" "bg-base-content"
                ]
            ]

            Html.p [
                prop.className (DesignSystem.faintText + " max-w-2xl")
                prop.text "bg/surface/ink/gold are carried by the \"dim\" DaisyUI theme's base-200/base-100/base-content/primary slots (replaced in place); line and spotlight are minted directly as --color-line / --color-spotlight since they have no DaisyUI-semantic equivalent."
            ]

            subheading "Base Colors"

            Html.div [
                prop.className "flex flex-wrap gap-4"
                prop.children [
                    colorSwatch "base-100" "bg-base-100" "text-base-content/60"
                    colorSwatch "base-200" "bg-base-200" "text-base-content/60"
                    colorSwatch "base-300" "bg-base-300" "text-base-content/60"
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [
                                prop.className "bg-base-content w-20 h-20 rounded-xl border border-base-content/10 shadow-sm"
                            ]
                            Html.span [
                                prop.className "text-xs font-mono text-base-content/60"
                                prop.text "base-content"
                            ]
                        ]
                    ]
                    colorSwatch "neutral" "bg-neutral" "text-base-content/60"
                ]
            ]

            subheading "Semantic Colors"

            Html.div [
                prop.className "flex flex-wrap gap-4"
                prop.children [
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [
                                prop.className "bg-primary w-20 h-20 rounded-xl shadow-sm flex items-center justify-center"
                                prop.children [
                                    Html.span [
                                        prop.className "text-primary-content text-xs font-bold"
                                        prop.text "Aa"
                                    ]
                                ]
                            ]
                            Html.span [ prop.className "text-xs font-mono text-primary"; prop.text "primary" ]
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "gold / the accent, foil" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [
                                prop.className "bg-secondary w-20 h-20 rounded-xl shadow-sm flex items-center justify-center"
                                prop.children [
                                    Html.span [
                                        prop.className "text-secondary-content text-xs font-bold"
                                        prop.text "Aa"
                                    ]
                                ]
                            ]
                            Html.span [ prop.className "text-xs font-mono text-secondary"; prop.text "secondary" ]
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "dull gold / low-emphasis" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [
                                prop.className "bg-accent w-20 h-20 rounded-xl shadow-sm flex items-center justify-center"
                                prop.children [
                                    Html.span [
                                        prop.className "text-accent-content text-xs font-bold"
                                        prop.text "Aa"
                                    ]
                                ]
                            ]
                            Html.span [ prop.className "text-xs font-mono text-accent"; prop.text "accent" ]
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "bright gold / foil-sweep end" ]
                        ]
                    ]
                ]
            ]

            subheading "Status Colors"

            Html.div [
                prop.className "flex flex-wrap gap-4"
                prop.children [
                    colorSwatch "info" "bg-info" "text-info"
                    colorSwatch "success" "bg-success" "text-success"
                    colorSwatch "warning" "bg-warning" "text-warning"
                    colorSwatch "error" "bg-error" "text-error"
                ]
            ]

            Html.p [
                prop.className (DesignSystem.faintText + " max-w-2xl")
                prop.text "Lifecycle status colors (info=on hold, success=completed, warning, error=abandoned) are functional signals, not brand accents -- gold stays the only accent."
            ]

            subheading "Text Hierarchy"

            Html.div [
                prop.className "flex flex-col gap-3 p-5 rounded-lg bg-base-200/30 border border-base-content/5 max-w-2xl"
                prop.children [
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-base-content"; prop.text "Primary -- ink" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-base-content -- oklch(0.93 0.012 60)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-ink-secondary"; prop.text "Secondary -- ink-secondary" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-ink-secondary -- oklch(0.74 0.015 45)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-ink-muted"; prop.text "Muted -- ink-muted" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-ink-muted -- oklch(0.62 0.02 40)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-ink-faint"; prop.text "Faint -- ink-faint" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-ink-faint -- oklch(0.52 0.04 45)" ]
                        ]
                    ]
                ]
            ]

            Html.p [
                prop.className (DesignSystem.faintText + " max-w-2xl")
                prop.text "These four steps are literal oklch lightness values on the warm-neutral hue, not alpha applied to ink -- the only legal text-hierarchy levels (styleguide.md § 1.2)."
            ]

            subheading "Decisions"

            decisionBox
                "Dark Theme"
                "\"dim\" theme, replaced in place with the Velvet Lobby palette -- dark, warm burgundy-black backgrounds let movie posters and backdrops stand out. oklch color space for precise, perceptually uniform colors."
                "Light themes (washed-out poster images, poor contrast for media-heavy UI). sRGB hex values (inconsistent perceived brightness across hues)."

            decisionBox
                "Semantic Color Mapping"
                "primary=gold, the single brand accent, spent sparingly like foil (CTAs, active state, ratings, \"In focus\"). secondary/accent are duller/brighter gold variants for lower/higher emphasis within the same hue family -- never a second brand hue. Lifecycle status colors (info/success/warning/error) stay functional signals only."
                "A second brand accent hue (breaks the \"gold is the only accent\" discipline). Monochromatic palette (too austere). Red as primary (too aggressive for a personal library app)."
        ]
    ]

// ── Section: Spacing ──

let private spacingSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Spacing"

            decision "Mobile-first responsive padding. The standard gap of 0.75rem (gap-3) balances information density with readability on both phone and desktop screens."

            subheading "Spacing Scale"

            Html.div [
                prop.className "flex flex-col gap-4 max-w-2xl"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-4"
                        prop.children [
                            Html.div [ prop.className "bg-primary/30 rounded" ; prop.style [ style.width 8; style.height 32 ] ]
                            Html.div [
                                prop.className "flex flex-col"
                                prop.children [
                                    Html.code [ prop.className "text-xs font-mono text-primary/80"; prop.text "gap-2 / 0.5rem" ]
                                    Html.span [ prop.className DesignSystem.faintText; prop.text "Compact -- tight lists, inline groups" ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-4"
                        prop.children [
                            Html.div [ prop.className "bg-primary/40 rounded"; prop.style [ style.width 12; style.height 32 ] ]
                            Html.div [
                                prop.className "flex flex-col"
                                prop.children [
                                    Html.code [ prop.className "text-xs font-mono text-primary/80"; prop.text "gap-3 / 0.75rem" ]
                                    Html.span [ prop.className DesignSystem.faintText; prop.text "Standard -- grids, card lists, default spacing" ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-4"
                        prop.children [
                            Html.div [ prop.className "bg-primary/50 rounded"; prop.style [ style.width 16; style.height 32 ] ]
                            Html.div [
                                prop.className "flex flex-col"
                                prop.children [
                                    Html.code [ prop.className "text-xs font-mono text-primary/80"; prop.text "gap-4 / 1rem" ]
                                    Html.span [ prop.className DesignSystem.faintText; prop.text "Loose -- section breaks, prominent spacing" ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Page Padding"

            Html.div [
                prop.className "flex flex-col gap-3 max-w-2xl"
                prop.children [
                    specimen "Responsive page padding" "DesignSystem.pagePadding" (
                        Html.div [
                            prop.className "flex gap-4"
                            prop.children [
                                Html.div [
                                    prop.className "border border-dashed border-primary/30 rounded-lg p-4"
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.mutedText; prop.text "p-4 (mobile)" ]
                                    ]
                                ]
                                Html.div [
                                    prop.className "border border-dashed border-primary/30 rounded-lg p-6"
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.mutedText; prop.text "lg:p-6 (desktop)" ]
                                    ]
                                ]
                            ]
                        ]
                    )
                    specimen "Page container (padding + max-width + centering)" "DesignSystem.pageContainer" (
                        Html.code [
                            prop.className "text-xs font-mono text-base-content/60"
                            prop.text "\"p-4 lg:p-6 max-w-7xl mx-auto\""
                        ]
                    )
                ]
            ]

            subheading "Border Radius"

            Html.div [
                prop.className "flex flex-wrap gap-6 items-end"
                prop.children [
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [ prop.className "w-20 h-20 bg-base-300/50 border border-base-content/10 rounded-xl" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "rounded-xl" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Cards" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [ prop.className "w-20 h-12 bg-base-300/50 border border-base-content/10 rounded-lg" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "rounded-lg" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Buttons" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [ prop.className "w-14 h-14 bg-base-300/50 border border-base-content/10 rounded-full" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "rounded-full" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Avatars" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2"
                        prop.children [
                            Html.div [ prop.className "w-14 h-20 bg-base-300/50 border border-base-content/10 rounded-md" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "rounded-md" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Posters" ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Section: Paper Overlay ──

/// A gradient backdrop that makes the paper overlay's elevation shadow read
/// clearly against a busy background.
let private overlayBackdrop (children: ReactElement list) =
    Html.div [
        prop.className "relative rounded-xl overflow-hidden p-6"
        prop.style [
            style.backgroundImage "linear-gradient(135deg, oklch(0.30 0.03 26), oklch(0.20 0.03 22), oklch(0.16 0.028 20))"
        ]
        prop.children children
    ]

let private paperOverlaySection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Paper Overlay"

            decision "Every floating surface in Mediatheca -- dropdowns, popovers, modals -- is solid paper lifted off the page: an opaque fill (`--color-paper`), a subtle line ring, and a true elevation shadow (`--shadow-paper`). No translucency, no `backdrop-filter` (ADR-0016 supersedes ADR-0006's mandatory glassmorphism). Paper overlay is a distinct vocabulary from `velvetCard` (page/card chrome, flush with the page, ring-only elevation) -- overlays read as lifted above the page, chrome does not."

            subheading "Paper Overlay"

            // paperOverlay
            overlayBackdrop [
                Html.div [
                    prop.className (DesignSystem.paperOverlay + " p-5")
                    prop.children [
                        Html.h3 [
                            prop.className DesignSystem.cardTitle
                            prop.text "Paper Overlay"
                        ]
                        Html.p [
                            prop.className (DesignSystem.secondaryText + " mt-2")
                            prop.text "Opaque fill, line ring, elevation shadow. Used for modals and floating panels -- fully legible over any backdrop, no blur required."
                        ]
                    ]
                ]
                Html.div [
                    prop.className "mt-2"
                    prop.children [
                        Html.code [
                            prop.className "text-xs font-mono text-white/60 bg-black/30 px-2 py-1 rounded"
                            prop.text "DesignSystem.paperOverlay"
                        ]
                    ]
                ]
            ]

            // paperDropdown
            overlayBackdrop [
                Html.div [
                    prop.className (DesignSystem.paperDropdown + " p-3 w-56")
                    prop.children [
                        Html.div [
                            prop.className "rating-dropdown-item"
                            prop.children [
                                Html.span [ prop.className "text-sm"; prop.text "Dropdown item 1" ]
                            ]
                        ]
                        Html.div [
                            prop.className "rating-dropdown-item"
                            prop.children [
                                Html.span [ prop.className "text-sm"; prop.text "Dropdown item 2" ]
                            ]
                        ]
                        Html.div [
                            prop.className "rating-dropdown-item rating-dropdown-item-active"
                            prop.children [
                                Html.span [ prop.className "text-sm"; prop.text "Active item" ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "mt-2"
                    prop.children [
                        Html.code [
                            prop.className "text-xs font-mono text-white/60 bg-black/30 px-2 py-1 rounded"
                            prop.text "DesignSystem.paperDropdown"
                        ]
                    ]
                ]
            ]

            subheading "Tooltip"

            decision "The system's first tooltip (design-system-n8zqr) -- same paper-overlay material as the dropdown above (opaque fill, line ring, elevation shadow), sized down to a compact label pill. Structural placement (fixed-position coordinates measured off the hovered trigger's own rect) is the caller's job; `DesignSystem.tooltip` owns only the material + typography. First consumer: the collapsed sidebar rail reveals an icon-only item's label on hover -- see Components/Sidebar.fs."

            Html.div [
                prop.className "relative h-16 rounded-lg bg-base-300/40 flex items-center pl-6"
                prop.children [
                    Html.div [
                        prop.className "w-9 h-9 rounded-lg bg-base-200 flex items-center justify-center text-ink-muted"
                        prop.children [ Icons.dashboard () ]
                    ]
                    Html.div [
                        prop.className (DesignSystem.tooltip + " ml-3")
                        prop.text "Dashboard"
                    ]
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.tooltip -- position: fixed + top/left set inline by the caller"
            ]

            subheading "Decisions"

            decisionBox
                "Paper, not glass"
                "Every overlay is solid paper -- opaque fill + elevation shadow + line ring -- never translucent, never blurred. Distinct from velvetCard (page chrome, flush with the page)."
                "Glassmorphism (semi-transparent background + backdrop-filter blur) -- retired 2026-07-03; ADR-0016 supersedes ADR-0006."
        ]
    ]

// ── Section: Animations ──

let private animationsSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Animations"

            decision "Animations are subtle and fast (0.15s-0.4s). They provide feedback and spatial continuity without slowing the user down. Stagger delays of 40ms per item give a premium cascading feel."

            subheading "Entrance Animations"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-3 gap-4"
                prop.children [
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className (DesignSystem.animateFadeIn + " p-6 rounded-xl bg-primary/10 border border-primary/20 text-center")
                                prop.children [
                                    Html.span [ prop.className DesignSystem.bodyText; prop.text "Fade In" ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70 text-center"; prop.text "DesignSystem.animateFadeIn" ]
                            Html.span [ prop.className (DesignSystem.faintText + " text-center"); prop.text "0.3s ease-out / opacity only" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className (DesignSystem.animateFadeInUp + " p-6 rounded-xl bg-secondary/10 border border-secondary/20 text-center")
                                prop.children [
                                    Html.span [ prop.className DesignSystem.bodyText; prop.text "Fade In Up" ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70 text-center"; prop.text "DesignSystem.animateFadeInUp" ]
                            Html.span [ prop.className (DesignSystem.faintText + " text-center"); prop.text "0.4s ease-out / opacity + translateY(12px)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className (DesignSystem.animateScaleIn + " p-6 rounded-xl bg-accent/10 border border-accent/20 text-center")
                                prop.children [
                                    Html.span [ prop.className DesignSystem.bodyText; prop.text "Scale In" ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70 text-center"; prop.text "DesignSystem.animateScaleIn" ]
                            Html.span [ prop.className (DesignSystem.faintText + " text-center"); prop.text "0.3s ease-out / opacity + scale(0.95)" ]
                        ]
                    ]
                ]
            ]

            subheading "Stagger Grid"

            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.staggerGrid + " grid grid-cols-4 sm:grid-cols-6 gap-3")
                        prop.children [
                            for i in 1..12 do
                                Html.div [
                                    prop.className "h-16 rounded-lg bg-primary/15 border border-primary/20 flex items-center justify-center"
                                    prop.children [
                                        Html.span [ prop.className "text-xs text-primary/60 font-mono"; prop.text (string i) ]
                                    ]
                                ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.staggerGrid" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Children cascade in with 40ms delay per item" ]
                        ]
                    ]
                ]
            ]

            subheading "Hover Effects"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-6"
                prop.children [
                    // Card hover
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className (DesignSystem.cardHover + " p-6 bg-base-200/50 border border-base-content/5 cursor-pointer")
                                prop.children [
                                    Html.h4 [ prop.className DesignSystem.cardTitle; prop.text "Card Hover" ]
                                    Html.p [ prop.className (DesignSystem.secondaryText + " mt-2"); prop.text "Hover over this card to see it lift with translateY(-4px) and enhanced shadow." ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.cardHover" ]
                        ]
                    ]
                    // Poster card hover
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className "flex justify-center"
                                prop.children [
                                    Html.div [
                                        prop.className (DesignSystem.posterCard + " w-32 cursor-pointer")
                                        prop.children [
                                            Html.div [
                                                prop.className DesignSystem.posterImageContainer
                                                prop.children [
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                                                        prop.children [ Icons.movie () ]
                                                    ]
                                                    Html.div [ prop.className DesignSystem.posterShine ]
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.div [
                                prop.className "text-center"
                                prop.children [
                                    Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.posterCard" ]
                                    Html.p [ prop.className (DesignSystem.faintText + " mt-1"); prop.text "Hover: scale(1.05) + translateY(-4px) + shine overlay" ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Section: Components ──

let private componentsSection () =
    // Mock data for component examples
    let mockFriendAlice: FriendRef = { Slug = "alice"; Name = "Alice"; ImageRef = None }
    let mockFriendBob: FriendRef = { Slug = "bob"; Name = "Bob"; ImageRef = None }
    let mockFriendCarla: FriendRef = { Slug = "carla"; Name = "Carla"; ImageRef = None }

    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Components"

            decision "Reusable components live in src/Client/Components/. Each exports view functions that accept typed parameters. Components use DesignSystem references internally."

            // ── PosterCard ──
            subheading "PosterCard"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Grid-display poster with 2:3 aspect ratio, hover shine effect, and info overlay. Used on the Movies grid page. Renders as a link to the movie detail."
            ]

            Html.div [
                prop.className "grid grid-cols-2 sm:grid-cols-4 gap-4 mt-4 max-w-2xl"
                prop.children [
                    // Without poster -- shows gradient placeholder
                    Html.div [
                        prop.className "flex flex-col gap-2"
                        prop.children [
                            PosterCard.view "blade-runner-2049-2017" "Blade Runner 2049" 2017 None None
                            Html.span [ prop.className DesignSystem.faintText; prop.text "No poster (placeholder)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-2"
                        prop.children [
                            PosterCard.view "the-matrix-1999" "The Matrix" 1999 None None
                            Html.span [ prop.className DesignSystem.faintText; prop.text "No poster (placeholder)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-2"
                        prop.children [
                            PosterCard.view "alien-1979" "Alien" 1979 None None
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Hover to see effects" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-2"
                        prop.children [
                            PosterCard.view "dune-part-two-2024" "Dune: Part Two" 2024 None None
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Hover to see effects" ]
                        ]
                    ]
                ]
            ]

            Html.div [
                prop.className "mt-3"
                prop.children [
                    Html.code [
                        prop.className "text-xs font-mono text-primary/70 bg-base-300/30 px-2 py-1 rounded"
                        prop.text "PosterCard.view slug name year posterRef ratingBadge"
                    ]
                ]
            ]

            // ── PosterCard.thumbnail ──
            subheading "PosterCard Thumbnail"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Small poster thumbnail for list/row layouts (Dashboard, FriendDetail, CatalogDetail)."
            ]

            Html.div [
                prop.className "flex gap-4 mt-4 items-center"
                prop.children [
                    PosterCard.thumbnail None "Example Movie"
                    PosterCard.thumbnail None "Another Movie"
                    Html.code [
                        prop.className "text-xs font-mono text-primary/70"
                        prop.text "PosterCard.thumbnail posterRef alt"
                    ]
                ]
            ]

            // ── ModalPanel ──
            subheading "ModalPanel"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Fixed-position modal dialog with a paper overlay. Cannot be rendered inline (it covers the entire viewport). Accepts a title, close handler, content, and optional footer."
            ]

            Html.div [
                prop.className "p-5 rounded-xl bg-base-200/30 border border-base-content/5 max-w-2xl"
                prop.children [
                    Html.p [ prop.className DesignSystem.mutedText; prop.text "API signatures:" ]
                    Html.div [
                        prop.className "flex flex-col gap-2 mt-3"
                        prop.children [
                            Html.code [
                                prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-2 rounded block"
                                prop.text "ModalPanel.view title onClose content"
                            ]
                            Html.code [
                                prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-2 rounded block"
                                prop.text "ModalPanel.viewWithFooter title onClose content footer"
                            ]
                            Html.code [
                                prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-2 rounded block"
                                prop.text "ModalPanel.viewCustom title onClose headerExtra content footer"
                            ]
                        ]
                    ]
                    Html.p [
                        prop.className (DesignSystem.faintText + " mt-3")
                        prop.text "Uses DesignSystem.modalContainer (fixed inset-0 z-50) + DesignSystem.modalPanel (paperOverlay + animate-fade-in). Backdrop click closes the modal."
                    ]
                ]
            ]

            // ── FriendPill ──
            subheading "FriendPill"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Badge-style pill for displaying friend references. Three variants: clickable, with remove button, and inline text link."
            ]

            Html.div [
                prop.className "flex flex-col gap-4 mt-4"
                prop.children [
                    Html.div [
                        prop.className "flex flex-wrap items-center gap-3"
                        prop.children [
                            FriendPill.view mockFriendAlice
                            FriendPill.view mockFriendBob
                            FriendPill.view mockFriendCarla
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "FriendPill.view friend" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Clickable badge, navigates to friend detail" ]
                        ]
                    ]

                    Html.div [
                        prop.className "flex flex-wrap items-center gap-3"
                        prop.children [
                            FriendPill.viewWithRemove mockFriendAlice (fun _ -> ())
                            FriendPill.viewWithRemove mockFriendBob (fun _ -> ())
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "FriendPill.viewWithRemove friend onRemove" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "With X button for removal" ]
                        ]
                    ]

                    Html.div [
                        prop.className "flex items-center gap-3"
                        prop.children [
                            Html.span [ prop.className DesignSystem.secondaryText; prop.text "Recommended by" ]
                            FriendPill.viewInline mockFriendAlice
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "FriendPill.viewInline friend" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Inline text link (no badge)" ]
                        ]
                    ]
                ]
            ]

            // ── Icons ──
            subheading "Icons"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Heroicons-based SVG icons. Standard size is 24x24 (w-6 h-6). Some small variants at 16x16 (w-4 h-4) for inline use."
            ]

            Html.div [
                prop.className "grid grid-cols-3 sm:grid-cols-4 md:grid-cols-6 gap-4 mt-4"
                prop.children [
                    // Standard icons (w-6 h-6)
                    for (icon, name) in [
                        Icons.dashboard, "dashboard"
                        Icons.movie, "movie"
                        Icons.friends, "friends"
                        Icons.catalog, "catalog"
                        Icons.events, "events"
                        Icons.settings, "settings"
                        Icons.trash, "trash"
                        Icons.questionCircle, "questionCircle"
                        Icons.thumbsDown, "thumbsDown"
                        Icons.minusCircle, "minusCircle"
                        Icons.handOkay, "handOkay"
                        Icons.thumbsUp, "thumbsUp"
                        Icons.trophy, "trophy"
                    ] do
                        Html.div [
                            prop.className "flex flex-col items-center gap-2 p-3 rounded-lg bg-base-200/20 border border-base-content/5"
                            prop.children [
                                Html.div [
                                    prop.className "text-base-content/70"
                                    prop.children [ icon () ]
                                ]
                                Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text name ]
                            ]
                        ]
                ]
            ]

            Html.div [
                prop.className "mt-3"
                prop.children [
                    Html.p [ prop.className DesignSystem.faintText; prop.text "Small icons (w-4 h-4): recommendedBy, play" ]
                    Html.div [
                        prop.className "flex gap-4 mt-2 items-center"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center gap-2 text-base-content/70"
                                prop.children [
                                    Icons.recommendedBy ()
                                    Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text "recommendedBy" ]
                                ]
                            ]
                            Html.div [
                                prop.className "flex items-center gap-2 text-base-content/70"
                                prop.children [
                                    Icons.play ()
                                    Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text "play" ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            // Special icon
            Html.div [
                prop.className "flex items-center gap-3 mt-3"
                prop.children [
                    Html.div [
                        prop.className "text-primary"
                        prop.children [ Icons.mediatheca () ]
                    ]
                    Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text "Icons.mediatheca (w-8 h-8, brand icon)" ]
                ]
            ]

            // ── Pill Buttons ──
            subheading "Pill Buttons"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Filter/tag toggle buttons with active and inactive states. Used in navigation tabs, filter bars, and tag selections."
            ]

            Html.div [
                prop.className "flex flex-wrap gap-3 mt-4"
                prop.children [
                    Html.button [
                        prop.className (DesignSystem.pill true)
                        prop.text "Active"
                    ]
                    Html.button [
                        prop.className (DesignSystem.pill false)
                        prop.text "Inactive"
                    ]
                    Html.button [
                        prop.className (DesignSystem.pill false)
                        prop.text "Another"
                    ]
                    Html.button [
                        prop.className (DesignSystem.pill true)
                        prop.text "Selected"
                    ]
                ]
            ]

            Html.div [
                prop.className "flex flex-col gap-2 mt-3"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.pill true" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Active state -- primary tint with border" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.pill false" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Inactive state -- transparent, hover reveals" ]
                        ]
                    ]
                ]
            ]

            // ── ActionMenu ──
            subheading "ActionMenu"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Trigger-and-dropdown action menu (kebab menus, hero action buttons). The dropdown is a paper overlay (rating-dropdown, per the Paper Overlay section). The dropdown renders as a SIBLING of the trigger button -- never a child. Click a trigger below to open its menu."

            ]

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-3 gap-6 mt-4 max-w-4xl"
                prop.children [
                    // view -- kebab menu
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-center p-6 rounded-lg bg-base-200/30 border border-base-content/5 min-h-[120px]"
                                prop.children [
                                    ActionMenu.view [
                                        { Label = "Edit"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false }
                                        { Label = "Share"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false }
                                        { Label = "Delete"; Icon = Some Icons.trash; OnClick = (fun () -> ()); IsDestructive = true }
                                    ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "ActionMenu.view items" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Kebab menu -- small ghost trigger, plain item list" ]
                        ]
                    ]

                    // heroView -- larger trigger
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-center p-6 rounded-lg bg-base-200/30 border border-base-content/5 min-h-[120px]"
                                prop.children [
                                    ActionMenu.heroView [
                                        { Label = "Change backdrop"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false }
                                        { Label = "Edit details"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false }
                                        { Label = "Remove from library"; Icon = Some Icons.trash; OnClick = (fun () -> ()); IsDestructive = true }
                                    ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "ActionMenu.heroView items" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Hero-positioned -- larger trigger for detail headers" ]
                        ]
                    ]

                    // heroViewSections -- labelled sections
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-center p-6 rounded-lg bg-base-200/30 border border-base-content/5 min-h-[120px]"
                                prop.children [
                                    ActionMenu.heroViewSections [
                                        { Label = Some "Manage"
                                          Items = [
                                            { Label = "Edit details"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false }
                                            { Label = "Change backdrop"; Icon = None; OnClick = (fun () -> ()); IsDestructive = false } ] }
                                        { Label = Some "Danger zone"
                                          Items = [
                                            { Label = "Remove from library"; Icon = Some Icons.trash; OnClick = (fun () -> ()); IsDestructive = true } ] }
                                    ]
                                ]
                            ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "ActionMenu.heroViewSections sections" ]
                            Html.span [ prop.className DesignSystem.faintText; prop.text "Labelled, divider-separated sections" ]
                        ]
                    ]
                ]
            ]

            Html.div [
                prop.className "mt-3 flex flex-col gap-2 max-w-3xl"
                prop.children [
                    Html.code [
                        prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-2 rounded block"
                        prop.text "ActionMenuItem = { Label: string; Icon: (unit -> ReactElement) option; OnClick: unit -> unit; IsDestructive: bool }"
                    ]
                    Html.code [
                        prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-2 rounded block"
                        prop.text "ActionMenuSection = { Label: string option; Items: ActionMenuItem list }"
                    ]
                    Html.p [
                        prop.className DesignSystem.faintText
                        prop.text "Source: Components/ActionMenu.fs (view :60, heroView :147, heroViewSections :208). Destructive items render in error color; click-outside and the backdrop overlay close the menu."
                    ]
                ]
            ]
        ]
    ]

// ── Section: Velvet Lobby patterns (design-system-h3q8n) ──
// The re-skinned recurring patterns from the design brief's 3a/3b/3c/3d
// boards: typed Feliz compositions in DesignSystem.fs, specimens here.

[<ReactComponent>]
let private velvetLobbyPatternsSection () =
    let rating, setRating = React.useState 3
    let heroRating, setHeroRating = React.useState 4

    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Velvet Lobby Patterns"

            decision "The recurring component patterns from the Velvet Lobby re-skin (design brief turn 3, options 3a dashboard / 3b game detail / 3c movies grid), re-expressed as typed Feliz compositions in DesignSystem.fs (not inline in pages) so every BC's frontend conforms to the same shapes. Tokens: styleguide.md § 1.3-1.6 (spacing/radii/shadows/animation, incl. the gold-leaf sweep). Surfaces: § 3.1 velvet card, paper overlay (ADR-0016) for floating controls."

            // ── Velvet card & paper overlay ──
            subheading "Surfaces — Velvet Card & Paper Overlay"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4 mt-4 max-w-3xl"
                prop.children [
                    specimen "Solid, non-overlay page/card surface (§ 3.1). surface background + line-ring elevation, no blur." "DesignSystem.velvetCard"
                        (Html.div [
                            prop.className (DesignSystem.velvetCard + " p-4")
                            prop.children [ Html.p [ prop.className DesignSystem.bodyText; prop.text "Velvet card" ] ]
                        ])
                    specimen "Paper overlay pill for small controls floating directly over artwork (ADR-0016) — the same opaque-fill/elevation material as dropdowns and modals, just pill-shaped." "DesignSystem.paperOverlay"
                        (Html.div [
                            prop.className "relative h-20 rounded-lg bg-gradient-to-br from-primary/30 to-base-300 flex items-center justify-center"
                            prop.children [
                                Html.div [
                                    prop.className (DesignSystem.paperOverlay + " rounded-full px-3 py-1.5 text-xs font-sans text-base-content")
                                    prop.text "Change artwork"
                                ]
                            ]
                        ])
                ]
            ]

            // ── Sidebar nav (dir 3a burgundy active tab) ──
            subheading "Sidebar Nav"

            decision "The desktop rail (§ 4 Sidebar nav — dir 3a, design-system-grtw7): a wordmark + tagline header, a top group of primary destinations, and a bottom group (Events/Settings, one step smaller) pinned via mt-auto. The active item is a burgundy fill (`--color-nav-active-fill`) with a gold icon — the brief's own dir-3a treatment, reverted from the ADR-0013 ivory placard + concave corner-notch (superseding ADR-0014); the gold inset-left bar dir-3a/ADR-0014 also carried was retracted (design-system-m2wvc) — the fill and icon alone read as active, without a hard vertical rule down every item's left edge."

            Html.div [
                prop.className "mt-4 max-w-[220px] rounded-lg overflow-hidden bg-base-200/80 border border-base-300/50 p-3"
                prop.children [
                    Html.ul [
                        prop.className DesignSystem.navGroupTop
                        prop.children [
                            Html.li [
                                Html.div [
                                    prop.className (DesignSystem.navItem + " " + DesignSystem.navItemActive)
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.navItemActiveIconClass; prop.children [ Icons.dashboard () ] ]
                                        Html.span [ prop.text "Dashboard" ]
                                    ]
                                ]
                            ]
                            Html.li [
                                Html.div [
                                    prop.className (DesignSystem.navItem + " " + DesignSystem.navItemInactive)
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.navItemIconClass; prop.children [ Icons.movie () ] ]
                                        Html.span [ prop.text "Movies" ]
                                    ]
                                ]
                            ]
                            Html.li [
                                Html.div [
                                    prop.className (DesignSystem.navItem + " " + DesignSystem.navItemInactive)
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.navItemIconClass; prop.children [ Icons.tv () ] ]
                                        Html.span [ prop.text "TV Series" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Html.ul [
                        prop.className DesignSystem.navGroupBottom
                        prop.children [
                            Html.li [
                                Html.div [
                                    prop.className (DesignSystem.navItem + " " + DesignSystem.navItemInactive)
                                    prop.children [
                                        Html.span [ prop.className DesignSystem.navItemIconClass; prop.children [ Icons.settings () ] ]
                                        Html.span [ prop.text "Settings" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.navItemClass isActive, navItemActiveIconClass, navGroupTop, navGroupBottom -- see Components/Sidebar.fs"
            ]

            // ── Underline tabs ──
            subheading "Underline Tabs"

            decision "Direction 3a's header-tab strip (design-system-k9p3v): a text tab with a gold underline under the active tab, no filled-pill / bordered-button chrome -- reuses the sidebar's gold token (`--color-gold`), no new colour. Promoted as a shared pattern the same way the sidebar's dir-3a treatment was (design-system-grtw7 / ADR-0014); the Dashboard header re-points onto it in intelligence-dq8rk."

            Html.div [
                prop.className "mt-4 flex items-center gap-6 border-b border-base-content/5"
                prop.children [
                    Html.button [
                        prop.type' "button"
                        prop.className (DesignSystem.underlineTabClass true)
                        prop.text "All"
                    ]
                    Html.button [
                        prop.type' "button"
                        prop.className (DesignSystem.underlineTabClass false)
                        prop.text "Movies"
                    ]
                    Html.button [
                        prop.type' "button"
                        prop.className (DesignSystem.underlineTabClass false)
                        prop.text "Series"
                    ]
                    Html.button [
                        prop.type' "button"
                        prop.className (DesignSystem.underlineTabClass false)
                        prop.text "Games"
                    ]
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.underlineTabClass isActive -- caller renders its own Html.button list + click wiring"
            ]

            // ── Status badges ──
            subheading "Status Badges"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "The game lifecycle mapped to the palette. \"In focus\" is the only variant that animates (gold-leaf sweep) — the sweep is reserved for this state only."
            ]

            Html.div [
                prop.className "flex flex-wrap gap-3 mt-4"
                prop.children [
                    DesignSystem.statusBadge DesignSystem.Backlog
                    DesignSystem.statusBadge DesignSystem.InFocus
                    DesignSystem.statusBadge DesignSystem.Retired
                    DesignSystem.statusBadge DesignSystem.Abandoned
                    DesignSystem.statusBadge DesignSystem.Dismissed
                ]
            ]

            Html.div [
                prop.className "mt-3 flex flex-col gap-1"
                prop.children [
                    Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.statusBadge DesignSystem.InFocus" ]
                    Html.p [
                        prop.className DesignSystem.faintText
                        prop.text "DesignSystem.LifecycleStatus unifies 1:1 with Shared.GameStatus (Backlog/InFocus/Retired/Abandoned/Dismissed) as of games-status-vocabulary-reconcile -- Playing never existed as a status (InFocus covers it) and OnHold was removed. LifecycleStatus stays the pattern's own type; a BC's status enum maps onto it case-for-case."
                    ]
                ]
            ]

            // ── Progress meters ──
            subheading "Progress Meters"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Segmented (film-frame, one bar per episode of a single season) is flag-driven — watched.[i] paints segment i, so a gap mid-season renders as a gap, not a prefix. The episode row has three states on one axis: brown (unwatched), half-gold (the next-up episode, exactly midway between the two ends), gold (watched). The season rail sits above it: one line per season, gold when the season has at least one watched episode, brown when untouched — and half-gold for the season the next-up episode lives in, the coarse-grained echo of the episode row's frontier marker. A fully-watched season otherwise reads the same as a partially-watched one. Off that gold axis entirely: a finished series paints both rows green (the Retired-badge green), because a show with nothing left has no frontier to point at. Continuous (gold-gradient fill) is for time/percent quantities, unrelated to either."
            ]

            Html.div [
                prop.className "flex flex-col gap-4 mt-4 max-w-md"
                prop.children [
                    Html.div [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            DesignSystem.progressEpisodes [ true; true; true; false; false; true; true; false; false; false ] (Some 7) false
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.progressEpisodes [ true; true; true; false; false; true; true; false; false; false ] (Some 7) false — episode 8 is next up: half-gold, one step short of watched" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            DesignSystem.progressSeasons [ true; true; false ] (Some 1) false
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.progressSeasons [ true; true; false ] (Some 1) false — season 2 is the one being watched: half-gold, same voice as the episode row's frontier" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            DesignSystem.seriesSeasonEpisodeProgress {
                                SeasonsTouched = [ true; true; false; true ]
                                ActiveSeasonIndex = Some 3
                                CurrentSeasonWatched = [ true; true; true; false; false; true; true; false; false; false ]
                                CurrentSeasonNextUpIndex = Some 7
                                IsComplete = false
                            }
                            Html.code [
                                prop.className "text-xs font-mono text-primary/70"
                                prop.text "DesignSystem.seriesSeasonEpisodeProgress { SeasonsTouched; ActiveSeasonIndex; CurrentSeasonWatched; CurrentSeasonNextUpIndex; IsComplete } — season rail above episode row; the hole at episodes 4-5 stays visible (history, not a queue), while episode 8 — the frontier — and its season are half-gold"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            DesignSystem.seriesSeasonEpisodeProgress {
                                SeasonsTouched = [ true; true; true ]
                                ActiveSeasonIndex = None
                                CurrentSeasonWatched = [ true; true; true; true; true; true ]
                                CurrentSeasonNextUpIndex = None
                                IsComplete = true
                            }
                            Html.code [
                                prop.className "text-xs font-mono text-primary/70"
                                prop.text "…with IsComplete = true — a finished series lingering on the dashboard: every season line and episode segment green, nothing half-lit"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            DesignSystem.progressContinuous 0.53
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "DesignSystem.progressContinuous 0.53" ]
                        ]
                    ]
                ]
            ]

            // ── Star rating ──
            subheading "Star Rating"

            Html.div [
                prop.className "flex items-center gap-4 mt-4"
                prop.children [
                    DesignSystem.starRating rating setRating
                    Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text (sprintf "value = %d" rating) ]
                ]
            ]
            Html.p [
                prop.className (DesignSystem.faintText + " mt-2")
                prop.text "DesignSystem.starRating value onChange -- tap a star to set, tap the current value again to clear."
            ]

            // ── Section header pattern ──
            subheading "Section Header"

            Html.div [
                prop.className "mt-4 max-w-2xl"
                prop.children [
                    DesignSystem.sectionHeaderPattern "In focus" (Some "Continuing") (Some ("All 12 →", fun () -> ()))
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.sectionHeaderPattern title eyebrow link"
            ]

            // ── List row ──
            subheading "List Row"

            Html.div [
                prop.className "flex flex-col mt-4 max-w-md rounded-lg bg-base-200/20 px-3"
                prop.children [
                    DesignSystem.listRow (PosterCard.thumbnail None "Recently played") "Marvel's Spider-Man 2" "yesterday · 2.4h"
                    DesignSystem.listRow (PosterCard.thumbnail None "Recently played") "Baldur's Gate 3" "3 days ago · 1.1h"
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.listRow thumb title meta"
            ]

            // ── In-focus poster frame ──
            subheading "In-Focus Poster Frame"

            Html.div [
                prop.className "flex gap-4 mt-4 max-w-xs"
                prop.children [
                    DesignSystem.inFocusFrame (PosterCard.view "dune-part-two-2024" "Dune: Part Two" 2024 None None)
                    PosterCard.view "the-matrix-1999" "The Matrix" 1999 None None
                ]
            ]
            Html.p [
                prop.className (DesignSystem.faintText + " mt-2")
                prop.text "DesignSystem.inFocusFrame child -- wraps any poster/card element with the gold-frame In-focus treatment, the visual sibling of the In-focus badge (left has the frame, right does not)."
            ]

            // ── In-focus pill (compact on-poster badge) ──
            subheading "In-Focus Pill (compact on-poster badge)"

            Html.div [
                prop.className "flex gap-4 mt-4 max-w-xs"
                prop.children [
                    Html.div [
                        prop.className "relative"
                        prop.children [
                            DesignSystem.inFocusFrame (PosterCard.view "dune-part-two-2024" "Dune: Part Two" 2024 None None)
                            DesignSystem.inFocusPill
                        ]
                    ]
                    PosterCard.view "the-matrix-1999" "The Matrix" 1999 None None
                ]
            ]
            Html.p [
                prop.className (DesignSystem.faintText + " mt-2")
                prop.text "DesignSystem.inFocusPill composed with inFocusFrame -- the intended poster-grid pairing. The pill is a solid gold fill (no gold-sweep); the frame behind it is the one animated element -- one moving element per poster."
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.inFocusPill -- render as a sibling of inFocusFrame inside a position-relative wrapper"
            ]

            // ── Movies filmstrip ──
            subheading "Movies Filmstrip"

            Html.div [
                prop.className "mt-4 max-w-[1200px]"
                prop.children [
                    DesignSystem.filmstripRow [
                        { Key = "alien"; PosterRef = None; Title = "Alien"; Meta = "1h57 · rec. by Mara"; Href = None; OnNavigate = None; InFocusBadge = None; JellyfinButton = None }
                        { Key = "blade-runner"; PosterRef = None; Title = "Blade Runner"; Meta = "1h57"; Href = None; OnNavigate = None; InFocusBadge = None; JellyfinButton = None }
                        { Key = "dune-part-two"; PosterRef = None; Title = "Dune: Part Two"; Meta = "2h46 · rec. by Sam"; Href = None; OnNavigate = None; InFocusBadge = None; JellyfinButton = None }
                        { Key = "arrival"; PosterRef = None; Title = "Arrival"; Meta = "1h56 · rec. by Alex"; Href = None; OnNavigate = None; InFocusBadge = None; JellyfinButton = None }
                        { Key = "sicario"; PosterRef = None; Title = "Sicario"; Meta = "2h1m"; Href = None; OnNavigate = None; InFocusBadge = None; JellyfinButton = None }
                    ]
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.filmstripRow [ { Key; PosterRef; Title; Meta; Href; OnNavigate; InFocusBadge; JellyfinButton } ]"
            ]

            // ── Secondary media card ──
            subheading "Secondary Media Card"

            Html.div [
                prop.className "flex gap-4 mt-4"
                prop.children [
                    DesignSystem.secondaryMediaCard {
                        Title = "Loki"
                        NextLabel = "Next: S2 E6 · 44 min"
                        Progress = {
                            SeasonsTouched = [ true; true ]
                            // Season 2 is where the next-up episode lives —
                            // half-gold, not the flat gold of a season that is
                            // merely touched.
                            ActiveSeasonIndex = Some 1
                            // series-ww1rb: a real mid-season hole (episodes 3-4
                            // skipped between two watched runs), not a prefix —
                            // proof the primitive renders gaps, not just counts.
                            CurrentSeasonWatched = [ true; true; false; false; true; false ]
                            // The frontier — the first unwatched episode *past* the
                            // furthest watched one. The 3-4 hole behind it stays
                            // brown: history, not a queue.
                            CurrentSeasonNextUpIndex = Some 5
                            IsComplete = false
                        }
                    }
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.secondaryMediaCard { Title; NextLabel; Progress }"
            ]

            // ── Cinematic hero card ──
            subheading "Cinematic Hero Card"

            Html.div [
                prop.className "mt-4 max-w-xl"
                prop.children [
                    DesignSystem.heroCard {
                        Title = "Severance"
                        InFocus = true
                        WatchedWith = [ "M"; "A" ]
                        Progress = {
                            SeasonsTouched = [ true; true ]
                            ActiveSeasonIndex = Some 1
                            CurrentSeasonWatched = [ true; true; true; false; false; true; true; false; false ]
                            CurrentSeasonNextUpIndex = Some 7
                            IsComplete = false
                        }
                        Rating = heroRating
                        OnRatingChange = setHeroRating
                        OnWatchClick = fun () -> ()
                    }
                ]
            ]
            Html.code [
                prop.className "text-xs font-mono text-primary/70 mt-2 block"
                prop.text "DesignSystem.heroCard { Title; InFocus; WatchedWith; Progress; Rating; OnRatingChange; OnWatchClick }"
            ]

            // ── Motion ──
            subheading "Motion"

            decision "Design-system owns the motion VOCABULARY, not its application. Three primitives are encoded once: the gold-leaf sweep (goldLeafSweep, reserved for In-focus surfaces only -- see Status Badges and the Cinematic Hero Card above), the leave-transition (leaveTransition / leaveTransitionLeaving, 400ms ease-out fade + collapse, for items leaving a queue), and the cross-fade (crossFade, 200ms, for e.g. dashboard tab-panel swaps). BCs decide WHERE the leave-transition and cross-fade fire -- that wiring is out of scope here. The spotlight gradient is static and never animated -- a rule, not a helper."

            Html.div [
                prop.className "flex flex-col gap-2 mt-3 max-w-2xl"
                prop.children [
                    Html.code [ prop.className "text-xs font-mono text-primary/70 block"; prop.text "DesignSystem.goldLeafSweep    (\"gold-sweep\", ~3.2s linear infinite)" ]
                    Html.code [ prop.className "text-xs font-mono text-primary/70 block"; prop.text "DesignSystem.leaveTransition / .leaveTransitionLeaving (400ms ease-out)" ]
                    Html.code [ prop.className "text-xs font-mono text-primary/70 block"; prop.text "DesignSystem.crossFade         (200ms)" ]
                ]
            ]
        ]
    ]

// ── Section: Content Blocks ──

[<ReactComponent>]
let private contentBlocksDemo () =
    let blocks, setBlocks = React.useState<ContentBlockDto list>([
        { BlockId = "demo-1"; BlockType = "text"; Content = "This is a text note. Hover to see the drag handle on the left."; ImageRef = None; Url = None; Caption = None; Position = 0; RowGroup = None; RowPosition = None }
        { BlockId = "demo-2"; BlockType = "quote"; Content = "The only way to do great work is to love what you do."; ImageRef = None; Url = None; Caption = None; Position = 1; RowGroup = None; RowPosition = None }
        { BlockId = "demo-3"; BlockType = "callout"; Content = "Click the drag handle to open the context menu. Use \"Turn into\" to change block types."; ImageRef = None; Url = None; Caption = None; Position = 2; RowGroup = None; RowPosition = None }
        { BlockId = "demo-4"; BlockType = "code"; Content = "let hello = printfn \"Hello from Fable!\""; ImageRef = None; Url = None; Caption = None; Position = 3; RowGroup = None; RowPosition = None }
        { BlockId = "demo-5"; BlockType = "text"; Content = "Check out [Fable Documentation](https://fable.io/docs/) for more info on the compiler."; ImageRef = None; Url = None; Caption = None; Position = 4; RowGroup = None; RowPosition = None }
    ])
    let nextId, setNextId = React.useState(6)

    let onAdd (req: AddContentBlockRequest) =
        let newBlock : ContentBlockDto = {
            BlockId = $"demo-{nextId}"
            BlockType = req.BlockType
            Content = req.Content
            ImageRef = req.ImageRef
            Url = req.Url
            Caption = req.Caption
            Position = blocks.Length
            RowGroup = None
            RowPosition = None
        }
        setBlocks (blocks @ [newBlock])
        setNextId (nextId + 1)

    let onUpdate (blockId: string) (req: UpdateContentBlockRequest) =
        setBlocks (blocks |> List.map (fun b ->
            if b.BlockId = blockId then
                { b with Content = req.Content; Url = req.Url; ImageRef = req.ImageRef; Caption = req.Caption }
            else b))

    let onRemove (blockId: string) =
        setBlocks (blocks |> List.filter (fun b -> b.BlockId <> blockId))

    let onChangeType (blockId: string) (newType: string) =
        setBlocks (blocks |> List.map (fun b ->
            if b.BlockId = blockId then { b with BlockType = newType }
            else b))

    let onReorder (blockIds: string list) =
        setBlocks (
            blockIds
            |> List.mapi (fun i bid ->
                blocks |> List.tryFind (fun b -> b.BlockId = bid)
                |> Option.map (fun b -> { b with Position = i }))
            |> List.choose id)

    ContentBlockEditor.view blocks onAdd onUpdate onRemove onChangeType onReorder None None None

let private contentBlocksSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Content Blocks"

            decision "The content block system lets users attach rich notes to movies. All blocks are text blocks that can contain inline links via markdown-style [text](url) syntax. Blocks are event-sourced and ordered by position. Smart paste: select text and paste a URL to create an inline link."

            subheading "Live Demo"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Try adding, editing, and removing blocks below. Hover over a block to see the drag handle on the left. Click the handle to open the context menu (edit, delete, change type). Drag to reorder."
            ]

            Html.div [
                prop.className "max-w-2xl mt-4"
                prop.children [
                    contentBlocksDemo ()
                ]
            ]

            subheading "Block Types"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 max-w-4xl"
                prop.children [
                    for (typeName, label, desc) in [
                        "text", "Text Block", "Free-form text notes with optional inline links via [text](url) markdown syntax. Default block type."
                        "quote", "Quote Block", "Styled with a left border and italic text. Use for citations, memorable quotes, or highlighted passages."
                        "callout", "Callout Block", "Info-styled block with an icon and tinted background. Use for tips, warnings, or important notes."
                        "code", "Code Block", "Monospace font with a subtle background. Use for code snippets, technical references, or formatted data."
                        "image", "Image Block", "Image attachments with optional caption. Uses ImageRef for storage reference. (Planned -- not yet in editor.)"
                    ] do
                        Html.div [
                            prop.className (DesignSystem.velvetCard + " p-5 rounded-xl border border-base-content/5")
                            prop.children [
                                Html.h4 [
                                    prop.className (DesignSystem.subtitle + " text-primary mb-2")
                                    prop.text label
                                ]
                                Html.p [
                                    prop.className DesignSystem.secondaryText
                                    prop.text desc
                                ]
                                Html.code [
                                    prop.className "block mt-3 text-xs font-mono text-base-content/50 bg-base-300/30 p-2 rounded"
                                    prop.text $"BlockType = \"{typeName}\""
                                ]
                            ]
                        ]
                ]
            ]

            subheading "API"

            Html.div [
                prop.className "flex flex-col gap-3 max-w-3xl"
                prop.children [
                    Html.code [
                        prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-3 rounded block"
                        prop.text "ContentBlockEditor.view blocks onAdd onUpdate onRemove onChangeType onReorder"
                    ]
                    Html.div [
                        prop.className "p-4 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [ prop.className DesignSystem.mutedText; prop.text "Parameters:" ]
                            Html.ul [
                                prop.className "mt-2 space-y-1"
                                prop.children [
                                    for (name, desc) in [
                                        "blocks", "ContentBlockDto list, sorted by Position"
                                        "onAdd", "AddContentBlockRequest -> unit"
                                        "onUpdate", "string -> UpdateContentBlockRequest -> unit"
                                        "onRemove", "string -> unit"
                                        "onChangeType", "string -> string -> unit (blockId, newType)"
                                        "onReorder", "string list -> unit (ordered blockIds)"
                                    ] do
                                        Html.li [
                                            prop.className "text-sm text-base-content/70"
                                            prop.children [
                                                Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text name ]
                                                Html.span [ prop.text $" -- {desc}" ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Interaction Patterns"

            Html.div [
                prop.className "flex flex-col gap-3 max-w-3xl"
                prop.children [
                    Html.div [
                        prop.className "p-4 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            for (keys, desc) in [
                                "Enter", "Save the current block"
                                "Escape", "Cancel editing"
                                "Select text + Paste URL", "Create an inline [text](url) link in the content"
                                "Hover block", "Reveal drag handle on the left"
                                "Click drag handle", "Open context menu (Edit, Delete, Turn into...)"
                                "Drag handle", "Drag to reorder blocks"
                            ] do
                                Html.div [
                                    prop.className "flex items-center gap-3 py-1"
                                    prop.children [
                                        Html.kbd [
                                            prop.className "px-2 py-0.5 text-xs font-mono bg-base-300/50 rounded border border-base-content/10 text-base-content/70 min-w-[4rem] text-center"
                                            prop.text keys
                                        ]
                                        Html.span [
                                            prop.className "text-sm text-base-content/70"
                                            prop.text desc
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Inline Editing"
                "Edit-in-place with Enter/Escape keyboard shortcuts. Blocks transform into input fields on edit, keeping the user in context. No modal dialogs for simple text edits."
                "Separate edit modal (too heavy for quick notes). Markdown editor (overkill for short text notes)."

            decisionBox
                "Smart Paste"
                "Pasting a URL when text is selected wraps it as a markdown link [text](url) inline. This mirrors how rich text editors work and keeps links as part of the text flow rather than separate block types."
                "Separate link block type (adds complexity, breaks text flow). Always creating plain text from paste (loses structured links)."

            decisionBox
                "No-Card Styling"
                "Content blocks render as plain text on the background -- no cards, no glass effects. Blocks are secondary content that should feel like natural text, not UI elements. New blocks appear via a subtle \"new block\" placeholder."
                "Glass cards (too visually heavy, makes notes feel like separate components). Fully styled cards (compete with primary movie metadata)."
        ]
    ]

// ── Section: Content Zone ──

// Functional updaters (setState(fun prev -> ...)) ensure each callback sees
// the latest state, even when ContentBlockEditor fires multiple callbacks
// (e.g. onReorder then onUngroup) in the same React event.

[<ReactComponent>]
let private contentZoneDemo () =
    let pairGroupId = "demo-row-group-1"
    let blocks, setBlocks = React.useState<ContentBlockDto list>([
        { BlockId = "zone-1"; BlockType = "text"; Content = "This standalone text block can be dragged to reorder, or dropped onto another block's left/right half to form a two-column row."; ImageRef = None; Url = None; Caption = None; Position = 0; RowGroup = None; RowPosition = None }
        { BlockId = "zone-2"; BlockType = "text"; Content = "Left column -- this block is already paired in a RowPair. Drag within the pair to swap sides, or drag to a gap to extract."; ImageRef = None; Url = None; Caption = None; Position = 1; RowGroup = Some pairGroupId; RowPosition = Some 0 }
        { BlockId = "zone-3"; BlockType = "text"; Content = "Right column -- the other half of the pre-existing RowPair."; ImageRef = None; Url = None; Caption = None; Position = 2; RowGroup = Some pairGroupId; RowPosition = Some 1 }
        { BlockId = "zone-4"; BlockType = "quote"; Content = "Two-column layouts let you place related content side by side -- like a quote next to commentary."; ImageRef = None; Url = None; Caption = None; Position = 3; RowGroup = None; RowPosition = None }
        { BlockId = "zone-5"; BlockType = "callout"; Content = "Try dragging this callout to the left or right half of the quote above to create a new pair!"; ImageRef = None; Url = None; Caption = None; Position = 4; RowGroup = None; RowPosition = None }
    ])
    let nextId, setNextId = React.useState(6)

    let onAdd (req: AddContentBlockRequest) =
        let newBlock : ContentBlockDto = {
            BlockId = $"zone-{nextId}"
            BlockType = req.BlockType
            Content = req.Content
            ImageRef = req.ImageRef
            Url = req.Url
            Caption = req.Caption
            Position = blocks.Length
            RowGroup = None
            RowPosition = None
        }
        setBlocks (blocks @ [newBlock])
        setNextId (nextId + 1)

    let onUpdate (blockId: string) (req: UpdateContentBlockRequest) =
        setBlocks (blocks |> List.map (fun b ->
            if b.BlockId = blockId then
                { b with Content = req.Content; Url = req.Url; ImageRef = req.ImageRef; Caption = req.Caption }
            else b))

    let onRemove (blockId: string) =
        setBlocks (blocks |> List.filter (fun b -> b.BlockId <> blockId))

    let onChangeType (blockId: string) (newType: string) =
        setBlocks (blocks |> List.map (fun b ->
            if b.BlockId = blockId then { b with BlockType = newType }
            else b))

    let onReorder (blockIds: string list) =
        setBlocks (
            blockIds
            |> List.mapi (fun i bid ->
                blocks |> List.tryFind (fun b -> b.BlockId = bid)
                |> Option.map (fun b -> { b with Position = i }))
            |> List.choose id)

    let onGroup (leftId: string) (rightId: string) =
        let groupId = System.Guid.NewGuid().ToString()
        setBlocks (blocks |> List.map (fun b ->
            if b.BlockId = leftId then { b with RowGroup = Some groupId; RowPosition = Some 0 }
            elif b.BlockId = rightId then { b with RowGroup = Some groupId; RowPosition = Some 1 }
            else b))

    let onUngroup (blockId: string) =
        let block = blocks |> List.tryFind (fun b -> b.BlockId = blockId)
        match block |> Option.bind (fun b -> b.RowGroup) with
        | Some rg ->
            setBlocks (blocks |> List.map (fun b ->
                if b.RowGroup = Some rg then { b with RowGroup = None; RowPosition = None }
                else b))
        | None -> ()

    ContentBlockEditor.view
        blocks
        onAdd
        onUpdate
        onRemove
        onChangeType
        onReorder
        None
        (Some onGroup)
        (Some onUngroup)

let private contentZoneSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Content Zone"

            decision "The Content Zone is a Notion-like drag-and-drop layout system for content blocks. Beyond simple reordering, blocks can be grouped into two-column RowPairs by dragging onto the left or right half of another block. RowPair members can be swapped or extracted back to full-width by dragging to a gap indicator."

            subheading "Live Demo"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "This is a fully interactive demo with pre-existing paired and standalone blocks. Try these interactions:"
            ]

            Html.ul [
                prop.className "mt-2 space-y-1 list-disc list-inside max-w-2xl"
                prop.children [
                    Html.li [
                        prop.className "text-sm text-base-content/70"
                        prop.text "Drag blocks between positions via the green full-width indicator lines"
                    ]
                    Html.li [
                        prop.className "text-sm text-base-content/70"
                        prop.text "Drag a block to the left or right half of another to create a two-column row"
                    ]
                    Html.li [
                        prop.className "text-sm text-base-content/70"
                        prop.text "Drag a RowPair member to a gap to extract it as full-width"
                    ]
                    Html.li [
                        prop.className "text-sm text-base-content/70"
                        prop.text "Drag within a RowPair to swap left/right positions"
                    ]
                ]
            ]

            Html.div [
                prop.className "max-w-2xl mt-4"
                prop.children [
                    contentZoneDemo ()
                ]
            ]

            subheading "Interaction Patterns"

            Html.div [
                prop.className "flex flex-col gap-3 max-w-3xl"
                prop.children [
                    Html.div [
                        prop.className "p-4 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            for (keys, desc) in [
                                "Drag to gap", "Reorder: move a block to a new position (green full-width line)"
                                "Drag to left half", "Group: create a RowPair with the dragged block on the left"
                                "Drag to right half", "Group: create a RowPair with the dragged block on the right"
                                "Drag pair member to gap", "Ungroup: extract block from pair, both become full-width"
                                "Drag within pair", "Swap: exchange left/right positions in the RowPair"
                            ] do
                                Html.div [
                                    prop.className "flex items-center gap-3 py-1"
                                    prop.children [
                                        Html.kbd [
                                            prop.className "px-2 py-0.5 text-xs font-mono bg-base-300/50 rounded border border-base-content/10 text-base-content/70 min-w-[10rem] text-center"
                                            prop.text keys
                                        ]
                                        Html.span [
                                            prop.className "text-sm text-base-content/70"
                                            prop.text desc
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]

            subheading "API"

            Html.div [
                prop.className "flex flex-col gap-3 max-w-3xl"
                prop.children [
                    Html.code [
                        prop.className "text-xs font-mono text-base-content/60 bg-base-300/30 p-3 rounded block"
                        prop.text "ContentBlockEditor.view blocks onAdd onUpdate onRemove onChangeType onReorder onUploadScreenshot onGroupBlocks onUngroupBlock"
                    ]
                    Html.div [
                        prop.className "p-4 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [ prop.className DesignSystem.mutedText; prop.text "Parameters:" ]
                            Html.ul [
                                prop.className "mt-2 space-y-1"
                                prop.children [
                                    for (name, desc) in [
                                        "blocks", "ContentBlockDto list -- sorted by Position"
                                        "onAdd", "AddContentBlockRequest -> unit"
                                        "onUpdate", "string -> UpdateContentBlockRequest -> unit"
                                        "onRemove", "string -> unit"
                                        "onChangeType", "string -> string -> unit (blockId, newType)"
                                        "onReorder", "string list -> unit (ordered blockIds)"
                                        "onUploadScreenshot", "(byte[] -> string -> string option -> unit) option"
                                        "onGroupBlocks", "(string -> string -> unit) option -- (leftId, rightId)"
                                        "onUngroupBlock", "(string -> unit) option -- blockId"
                                    ] do
                                        Html.li [
                                            prop.className "text-sm text-base-content/70"
                                            prop.children [
                                                Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text name ]
                                                Html.span [ prop.text $" -- {desc}" ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Gap-Based Reordering"
                "Green full-width indicator lines appear between blocks during drag, clearly showing where the block will land. This provides unambiguous drop targets and works naturally with both single blocks and RowPairs."
                "Swap-on-hover (confusing with adjacent blocks). Drag handle only (no visual feedback for drop position). Sortable.js (external dependency, harder to integrate with RowPair grouping)."

            decisionBox
                "Left/Right Drop Zones for Grouping"
                "Dragging onto the left or right half of a block creates a two-column RowPair. The drop zone (left vs right) determines which side the dragged block occupies. This mirrors Notion's column creation and is discoverable through visual feedback."
                "Explicit 'group' button (extra UI, less fluid). Context menu grouping (requires selecting two blocks separately, slower workflow)."
        ]
    ]

// ── Section: Entry List ──

type private MockEntry = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Genres: string list
    Rating: float option
}

let private mockEntries = [
    { Slug = "blade-runner-2049-2017"; Name = "Blade Runner 2049"; Year = 2017; PosterRef = None; Genres = ["Sci-Fi"; "Drama"]; Rating = Some 8.0 }
    { Slug = "the-matrix-1999"; Name = "The Matrix"; Year = 1999; PosterRef = None; Genres = ["Sci-Fi"; "Action"]; Rating = Some 8.7 }
    { Slug = "alien-1979"; Name = "Alien"; Year = 1979; PosterRef = None; Genres = ["Horror"; "Sci-Fi"]; Rating = Some 8.5 }
    { Slug = "dune-part-two-2024"; Name = "Dune: Part Two"; Year = 2024; PosterRef = None; Genres = ["Sci-Fi"; "Adventure"]; Rating = Some 8.3 }
    { Slug = "parasite-2019"; Name = "Parasite"; Year = 2019; PosterRef = None; Genres = ["Thriller"; "Drama"]; Rating = Some 8.5 }
    { Slug = "interstellar-2014"; Name = "Interstellar"; Year = 2014; PosterRef = None; Genres = ["Sci-Fi"; "Drama"]; Rating = Some 8.7 }
    { Slug = "the-godfather-1972"; Name = "The Godfather"; Year = 1972; PosterRef = None; Genres = ["Crime"; "Drama"]; Rating = Some 9.2 }
    { Slug = "spirited-away-2001"; Name = "Spirited Away"; Year = 2001; PosterRef = None; Genres = ["Animation"; "Fantasy"]; Rating = Some 8.6 }
]

let private mockEntryItems : EntryList.EntryItem list =
    mockEntries |> List.map (fun e ->
        { Slug = e.Slug
          Name = e.Name
          Year = e.Year
          PosterRef = e.PosterRef
          Rating = e.Rating
          RoutePrefix = "movies" })

let private mockBySlug =
    mockEntries |> List.map (fun e -> e.Slug, e) |> Map.ofList

let private mockListRow (item: EntryList.EntryItem) =
    let entry = mockBySlug |> Map.tryFind item.Slug
    Html.div [
        prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 hover:bg-base-200/80 transition-colors group"
        prop.children [
            Html.div [
                prop.className "flex-none"
                prop.children [ PosterCard.thumbnail item.PosterRef item.Name ]
            ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2 mt-0.5"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/50"
                                prop.text (string item.Year)
                            ]
                            match entry with
                            | Some e when not (List.isEmpty e.Genres) ->
                                Html.span [
                                    prop.className "text-base-content/20"
                                    prop.text "·"
                                ]
                                Html.span [
                                    prop.className "text-xs text-base-content/40"
                                    prop.text (e.Genres |> String.concat ", ")
                                ]
                            | _ -> ()
                        ]
                    ]
                ]
            ]
            match item.Rating with
            | Some r ->
                Html.div [
                    prop.className "flex-none text-xs font-medium text-warning/80 bg-warning/10 px-2 py-0.5 rounded"
                    prop.text (sprintf "%.1f" r)
                ]
            | None -> ()
        ]
    ]

let private entryListSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Entry List"

            decision "A Notion-style database view for media entries. Supports switchable layouts: Gallery shows poster cards in a responsive grid, List shows detailed rows with thumbnail, metadata, and ratings. The layout toggle persists per-component instance. Reusable via Components/EntryList.fs."

            subheading "Live Demo"

            Html.div [
                prop.className "mt-2"
                prop.children [
                    EntryList.view {
                        Items = mockEntryItems
                        RenderListRow = mockListRow
                        ShowWatchOrder = false
                        InitialSettings = None
                        OnSettingsChanged = None
                    }
                ]
            ]

            subheading "Usage"

            codeBlock """EntryList.view {
    Items = items         // EntryItem list
    RenderListRow = fun item ->
        // custom list-mode row per page
        Html.div [ ... ]
}"""

            subheading "EntryItem"

            Html.div [
                prop.className "p-4 rounded-lg bg-base-200/30 border border-base-content/5 max-w-2xl"
                prop.children [
                    Html.p [ prop.className DesignSystem.mutedText; prop.text "Fields:" ]
                    Html.ul [
                        prop.className "mt-2 space-y-1"
                        prop.children [
                            for (name, desc) in [
                                "Slug", "string -- unique identifier, used for PosterCard link"
                                "Name", "string -- display title"
                                "Year", "int -- release year"
                                "PosterRef", "string option -- image reference"
                                "Rating", "float option -- used by sort-by-rating"
                            ] do
                                Html.li [
                                    prop.className "text-sm text-base-content/70"
                                    prop.children [
                                        Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text name ]
                                        Html.span [ prop.text $" -- {desc}" ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]

            subheading "Icons"

            Html.div [
                prop.className "flex gap-6 mt-2"
                prop.children [
                    Html.div [
                        prop.className "flex flex-col items-center gap-2 p-3 rounded-lg bg-base-200/20 border border-base-content/5"
                        prop.children [
                            Html.div [ prop.className "text-base-content/70"; prop.children [ Icons.viewGrid () ] ]
                            Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text "viewGrid" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex flex-col items-center gap-2 p-3 rounded-lg bg-base-200/20 border border-base-content/5"
                        prop.children [
                            Html.div [ prop.className "text-base-content/70"; prop.children [ Icons.viewList () ] ]
                            Html.span [ prop.className "text-xs font-mono text-base-content/40"; prop.text "viewList" ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Layout Toggle Pattern"
                "Segmented control (icon-only) in a contained pill group. Visually distinct from filter pills which are standalone. The toggle is local React state, not part of the Elmish model, since it's a view preference not application state."
                "Dropdown select (hidden options, extra click). Tab bar (conflicts with page-level navigation). Icon-only toggle (poor discoverability)."

            decisionBox
                "Gallery as Default"
                "Gallery (poster grid) is the default layout because posters are the strongest visual identifier for movies. The dark theme makes posters pop, and the grid gives a quick visual scan of the collection."
                "List as default (too text-heavy for a media app). Table layout (too dense, better for data apps than media libraries)."

            decisionBox
                "Caller-provided List Row"
                "Gallery mode is uniform (PosterCard.view), but list mode delegates row rendering to the caller via RenderListRow. Each page can show its own metadata: notes in catalogs, dates in watched-together, genres+ratings in the style guide."
                "Fixed list row format (can't show page-specific data). Fully configurable via options record (over-engineering for 3 use cases)."
        ]
    ]

// ── Section Nav ──

let private sectionNav (activeSection: Section) (dispatch: Msg -> unit) =
    let sections = [
        Overview, "Overview"
        Typography, "Typography"
        Colors, "Colors"
        Spacing, "Spacing"
        PaperOverlay, "Paper Overlay"
        Animations, "Animations"
        Components, "Components"
        VelvetLobbyPatterns, "Velvet Lobby Patterns"
        ContentBlocks, "Content Blocks"
        ContentZone, "Content Zone"
        EntryList, "Entry List"
    ]
    Html.nav [
        prop.className "flex flex-wrap gap-2 mb-8"
        prop.children [
            for (section, label) in sections do
                Html.button [
                    prop.className (DesignSystem.pill (section = activeSection))
                    prop.text label
                    prop.onClick (fun _ -> dispatch (Set_section section))
                ]
        ]
    ]

// ── Section Content ──

let private sectionContent (section: Section) =
    match section with
    | Overview -> overviewSection ()
    | Typography -> typographySection ()
    | Colors -> colorsSection ()
    | Spacing -> spacingSection ()
    | PaperOverlay -> paperOverlaySection ()
    | Animations -> animationsSection ()
    | Components -> componentsSection ()
    | VelvetLobbyPatterns -> velvetLobbyPatternsSection ()
    | ContentBlocks -> contentBlocksSection ()
    | ContentZone -> contentZoneSection ()
    | EntryList -> entryListSection ()

// ── Page View ──

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "mb-8"
                prop.children [
                    Html.h1 [
                        prop.className (DesignSystem.pageTitle + " mb-2")
                        prop.text "Style Guide"
                    ]
                    Html.p [
                        prop.className "text-base-content/50"
                        prop.text "Design system reference & component workbench"
                    ]
                ]
            ]
            sectionNav model.ActiveSection dispatch
            sectionContent model.ActiveSection
        ]
    ]
