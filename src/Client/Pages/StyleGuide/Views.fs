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
                        prop.className (DesignSystem.glassSubtle + " p-5 rounded-xl border border-base-content/5")
                        prop.children [
                            Html.h4 [
                                prop.className (DesignSystem.subtitle + " text-primary mb-2")
                                prop.text "Layer 1: CSS Custom Properties"
                            ]
                            Html.p [
                                prop.className DesignSystem.secondaryText
                                prop.text "Raw design tokens defined in index.css under :root. Glass opacities, spacing scale, border radii, animation durations, shadows, and typography tracking. These are the primitive values."
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-base-content/50 bg-base-300/30 p-2 rounded"
                                prop.text "--glass-blur-standard: 24px;"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className (DesignSystem.glassSubtle + " p-5 rounded-xl border border-base-content/5")
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
                                prop.text "DesignSystem.glassCard"
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Usage Example"

            codeBlock """// In a component:
Html.div [
    prop.className DesignSystem.glassCard
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

            decision "Two font families define the visual identity. Oswald (font-display) for all headings -- always uppercase with 0.05em tracking for a cinematic, poster-like feel. Inter (font-sans) for body text, labels, and buttons -- clean and highly legible at all sizes."

            subheading "Type Scale"

            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    specimen "Page title -- large gradient heading" "DesignSystem.pageTitle" (
                        Html.h1 [
                            prop.className DesignSystem.pageTitle
                            prop.text "Page Title"
                        ]
                    )
                    specimen "Section header -- h2 with accent bar convention" "DesignSystem.sectionHeader" (
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
                    specimen "Subtitle -- secondary heading / label" "DesignSystem.subtitle" (
                        Html.span [
                            prop.className DesignSystem.subtitle
                            prop.text "Subtitle Text"
                        ]
                    )
                    specimen "Body text -- default readable text" "DesignSystem.bodyText" (
                        Html.p [
                            prop.className DesignSystem.bodyText
                            prop.text "Body text used for paragraphs and general content. Inter at base size with full opacity."
                        ]
                    )
                    specimen "Secondary text -- descriptions, metadata (70% opacity)" "DesignSystem.secondaryText" (
                        Html.p [
                            prop.className DesignSystem.secondaryText
                            prop.text "Secondary text for descriptions, metadata, and supporting information."
                        ]
                    )
                    specimen "Muted text -- timestamps, labels (50% opacity)" "DesignSystem.mutedText" (
                        Html.p [
                            prop.className DesignSystem.mutedText
                            prop.text "Muted text for timestamps, labels, and tertiary information."
                        ]
                    )
                    specimen "Faint text -- placeholders, hints (40% opacity)" "DesignSystem.faintText" (
                        Html.p [
                            prop.className DesignSystem.faintText
                            prop.text "Faint text for placeholders, hints, and the lowest-priority information."
                        ]
                    )
                ]
            ]

            subheading "Font Families"

            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
                prop.children [
                    Html.div [
                        prop.className "p-5 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [
                                prop.className "text-3xl font-display uppercase tracking-wider mb-2"
                                prop.text "Oswald"
                            ]
                            Html.p [
                                prop.className "font-display uppercase tracking-wider text-base-content/70 text-sm"
                                prop.text "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                            ]
                            Html.p [
                                prop.className "font-display uppercase tracking-wider text-base-content/70 text-sm"
                                prop.text "0123456789"
                            ]
                            Html.code [
                                prop.className "block mt-3 text-xs font-mono text-primary/70"
                                prop.text "font-display / uppercase / tracking-wider"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "p-5 rounded-lg bg-base-200/30 border border-base-content/5"
                        prop.children [
                            Html.p [
                                prop.className "text-3xl font-sans mb-2"
                                prop.text "Inter"
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
                                prop.text "font-sans / normal case / body text, labels, buttons"
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Font Pairing"
                "Oswald (headings, always uppercase + tracking 0.05em) and Inter (body, labels, buttons). The contrast between a condensed display font and a clean sans-serif creates visual hierarchy without extra decoration."
                "Using a single font for everything (too monotone, no hierarchy). Serif fonts (too formal for a media/entertainment app)."
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

let private colorsSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Colors"

            decision "The \"dim\" dark theme was chosen because dark backgrounds let movie posters and backdrops visually pop. All colors use the oklch color space for perceptually uniform, vibrant results."

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
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "cyan-green / CTAs" ]
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
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "orange / social" ]
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
                            Html.span [ prop.className "text-xs text-base-content/40"; prop.text "magenta / attention" ]
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

            subheading "Text Hierarchy"

            Html.div [
                prop.className "flex flex-col gap-3 p-5 rounded-lg bg-base-200/30 border border-base-content/5 max-w-2xl"
                prop.children [
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-base-content"; prop.text "Primary text -- full opacity" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-base-content (100%)" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-base-content/70"; prop.text "Secondary text -- 70% opacity" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-base-content/70" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-base-content/50"; prop.text "Muted text -- 50% opacity" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-base-content/50" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex items-center justify-between"
                        prop.children [
                            Html.span [ prop.className "text-base text-base-content/40"; prop.text "Faint text -- 40% opacity" ]
                            Html.code [ prop.className "text-xs font-mono text-primary/70"; prop.text "text-base-content/40" ]
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Dark Theme"
                "\"dim\" dark theme -- dark backgrounds let movie posters and backdrops stand out. oklch color space for precise, perceptually uniform colors."
                "Light themes (washed-out poster images, poor contrast for media-heavy UI). sRGB hex values (inconsistent perceived brightness across hues)."

            decisionBox
                "Semantic Color Mapping"
                "primary=cyan-green for CTAs and navigation highlights. secondary=orange for social features (friends, recommendations). accent=magenta for attention-grabbing elements."
                "Monochromatic palette (too austere). Red as primary (too aggressive for a personal library app)."
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

// ── Section: Glassmorphism ──

/// A gradient background that makes glass blur visible
let private glassBackground (children: ReactElement list) =
    Html.div [
        prop.className "relative rounded-xl overflow-hidden p-6"
        prop.style [
            style.backgroundImage "linear-gradient(135deg, oklch(50% 0.15 200), oklch(40% 0.12 280), oklch(35% 0.1 330))"
        ]
        prop.children children
    ]

let private glassmorphismSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Glassmorphism"

            decision "Every overlay in Mediatheca uses glassmorphism -- semi-transparent backgrounds with backdrop blur. This creates depth and context, letting the underlying content remain visible while focusing attention on the overlay."

            subheading "Glass Levels"

            // glassCard
            glassBackground [
                Html.div [
                    prop.className (DesignSystem.glassCard + " p-5")
                    prop.children [
                        Html.h3 [
                            prop.className DesignSystem.cardTitle
                            prop.text "Glass Card"
                        ]
                        Html.p [
                            prop.className (DesignSystem.secondaryText + " mt-2")
                            prop.text "Standard glassmorphism panel. Used for sidebar cards, detail panels, and general content containers. 55% opacity with 24px blur."
                        ]
                    ]
                ]
                Html.div [
                    prop.className "mt-2"
                    prop.children [
                        Html.code [
                            prop.className "text-xs font-mono text-white/60 bg-black/30 px-2 py-1 rounded"
                            prop.text "DesignSystem.glassCard"
                        ]
                    ]
                ]
            ]

            // glassOverlay
            glassBackground [
                Html.div [
                    prop.className (DesignSystem.glassOverlay + " p-5")
                    prop.children [
                        Html.h3 [
                            prop.className DesignSystem.cardTitle
                            prop.text "Glass Overlay"
                        ]
                        Html.p [
                            prop.className (DesignSystem.secondaryText + " mt-2")
                            prop.text "Heavy glassmorphism for modals and important overlays. 70% opacity with extra-large blur. More opaque to keep modal content readable."
                        ]
                    ]
                ]
                Html.div [
                    prop.className "mt-2"
                    prop.children [
                        Html.code [
                            prop.className "text-xs font-mono text-white/60 bg-black/30 px-2 py-1 rounded"
                            prop.text "DesignSystem.glassOverlay"
                        ]
                    ]
                ]
            ]

            // glassSubtle
            glassBackground [
                Html.div [
                    prop.className (DesignSystem.glassSubtle + " p-5 rounded-xl")
                    prop.children [
                        Html.h3 [
                            prop.className DesignSystem.cardTitle
                            prop.text "Glass Subtle"
                        ]
                        Html.p [
                            prop.className (DesignSystem.secondaryText + " mt-2")
                            prop.text "Subtle glassmorphism for content block cards and inline panels. 50% opacity with a soft blur. Minimal visual weight."
                        ]
                    ]
                ]
                Html.div [
                    prop.className "mt-2"
                    prop.children [
                        Html.code [
                            prop.className "text-xs font-mono text-white/60 bg-black/30 px-2 py-1 rounded"
                            prop.text "DesignSystem.glassSubtle"
                        ]
                    ]
                ]
            ]

            // glassDropdown
            glassBackground [
                Html.div [
                    prop.className (DesignSystem.glassDropdown + " p-3 w-56")
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
                            prop.text "DesignSystem.glassDropdown"
                        ]
                    ]
                ]
            ]

            subheading "Decisions"

            decisionBox
                "Universal Glassmorphism"
                "Every overlay uses glassmorphism -- never fully opaque backgrounds on floating elements. Opacity range: 0.55-0.70 depending on importance (lighter = more see-through = less important)."
                "Opaque overlays (lose spatial context, feel disconnected from the content beneath)."

            Html.div [
                prop.className "bg-warning/10 border-l-4 border-warning/40 p-4 rounded-r-lg max-w-3xl"
                prop.children [
                    Html.p [
                        prop.className (DesignSystem.subtitle + " text-warning/90 mb-2")
                        prop.text "Gotcha: Nested backdrop-filter"
                    ]
                    Html.p [
                        prop.className DesignSystem.secondaryText
                        prop.text "If a parent has backdrop-filter (e.g. backdrop-blur-sm), any child's backdrop-filter will only blur the parent's content, not the page behind it. Fix: render glassmorphic dropdowns/popovers as siblings to the blurred parent, not children. Wrap both in a plain position: relative container without backdrop-filter."
                    ]
                ]
            ]
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
                prop.text "Fixed-position modal dialog with glassmorphism overlay. Cannot be rendered inline (it covers the entire viewport). Accepts a title, close handler, content, and optional footer."
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
                        prop.text "Uses DesignSystem.modalContainer (fixed inset-0 z-50) + DesignSystem.modalPanel (glassOverlay + animate-fade-in). Backdrop click closes the modal."
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
        ]
    ]

// ── Section: Content Blocks ──

[<ReactComponent>]
let private contentBlocksDemo () =
    let blocks, setBlocks = React.useState<ContentBlockDto list>([
        { BlockId = "demo-1"; BlockType = "text"; Content = "This is a text note. Hover to see the drag handle on the left."; ImageRef = None; Url = None; Caption = None; Position = 0 }
        { BlockId = "demo-2"; BlockType = "quote"; Content = "The only way to do great work is to love what you do."; ImageRef = None; Url = None; Caption = None; Position = 1 }
        { BlockId = "demo-3"; BlockType = "callout"; Content = "Click the drag handle to open the context menu. Use \"Turn into\" to change block types."; ImageRef = None; Url = None; Caption = None; Position = 2 }
        { BlockId = "demo-4"; BlockType = "code"; Content = "let hello = printfn \"Hello from Fable!\""; ImageRef = None; Url = None; Caption = None; Position = 3 }
        { BlockId = "demo-5"; BlockType = "text"; Content = "Check out [Fable Documentation](https://fable.io/docs/) for more info on the compiler."; ImageRef = None; Url = None; Caption = None; Position = 4 }
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

    ContentBlockEditor.view blocks onAdd onUpdate onRemove onChangeType onReorder

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
                            prop.className (DesignSystem.glassSubtle + " p-5 rounded-xl border border-base-content/5")
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

// ── Section: Entry List ──

type private EntryListLayout = Gallery | List

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

let private layoutToggle (active: EntryListLayout) (onSwitch: EntryListLayout -> unit) =
    Html.div [
        prop.className "flex items-center gap-1 bg-base-200/50 rounded-lg p-1"
        prop.children [
            Html.button [
                prop.className (
                    "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-all duration-200 "
                    + (if active = Gallery then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch Gallery)
                prop.children [
                    Html.span [ prop.className "w-4 h-4"; prop.children [ Icons.viewGrid () ] ]
                    Html.span [ prop.text "Gallery" ]
                ]
            ]
            Html.button [
                prop.className (
                    "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-all duration-200 "
                    + (if active = List then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch List)
                prop.children [
                    Html.span [ prop.className "w-4 h-4"; prop.children [ Icons.viewList () ] ]
                    Html.span [ prop.text "List" ]
                ]
            ]
        ]
    ]

let private galleryView (entries: MockEntry list) =
    Html.div [
        prop.className (DesignSystem.movieGrid + " " + DesignSystem.staggerGrid)
        prop.children [
            for entry in entries do
                PosterCard.view entry.Slug entry.Name entry.Year entry.PosterRef None
        ]
    ]

let private listRow (entry: MockEntry) =
    Html.div [
        prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 hover:bg-base-200/80 transition-colors group"
        prop.children [
            Html.div [
                prop.className "flex-none"
                prop.children [ PosterCard.thumbnail entry.PosterRef entry.Name ]
            ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text entry.Name
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2 mt-0.5"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/50"
                                prop.text (string entry.Year)
                            ]
                            Html.span [
                                prop.className "text-base-content/20"
                                prop.text "·"
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/40"
                                prop.text (entry.Genres |> String.concat ", ")
                            ]
                        ]
                    ]
                ]
            ]
            match entry.Rating with
            | Some r ->
                Html.div [
                    prop.className "flex-none text-xs font-medium text-warning/80 bg-warning/10 px-2 py-0.5 rounded"
                    prop.text (sprintf "%.1f" r)
                ]
            | None -> ()
        ]
    ]

let private listView (entries: MockEntry list) =
    Html.div [
        prop.className ("bg-base-200/50 rounded-xl p-2 flex flex-col gap-1 " + DesignSystem.animateFadeIn)
        prop.children [
            for entry in entries do
                listRow entry
        ]
    ]

[<ReactComponent>]
let private entryListDemo () =
    let layout, setLayout = React.useState Gallery

    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.secondaryText
                        prop.text $"{mockEntries.Length} entries"
                    ]
                    layoutToggle layout setLayout
                ]
            ]
            match layout with
            | Gallery -> galleryView mockEntries
            | List -> listView mockEntries
        ]
    ]

let private entryListSection () =
    Html.div [
        prop.className "flex flex-col gap-6"
        prop.children [
            sectionTitle "Entry List"

            decision "A Notion-style database view for media entries. Supports switchable layouts: Gallery shows poster cards in a responsive grid, List shows detailed rows with thumbnail, metadata, and ratings. The layout toggle persists per-component instance."

            subheading "Live Demo"

            Html.div [
                prop.className "mt-2"
                prop.children [ entryListDemo () ]
            ]

            subheading "Layout Toggle"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Segmented control with icon + label. Active state uses primary tint with subtle shadow. Wraps in a base-200 container to group the options."
            ]

            Html.div [
                prop.className "flex flex-col gap-3 mt-4"
                prop.children [
                    Html.div [
                        prop.className "flex gap-4 items-center"
                        prop.children [
                            layoutToggle Gallery (fun _ -> ())
                            Html.code [
                                prop.className "text-xs font-mono text-primary/70"
                                prop.text "Gallery active"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex gap-4 items-center"
                        prop.children [
                            layoutToggle List (fun _ -> ())
                            Html.code [
                                prop.className "text-xs font-mono text-primary/70"
                                prop.text "List active"
                            ]
                        ]
                    ]
                ]
            ]

            subheading "Gallery Layout"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Responsive poster grid using PosterCard.view. Same layout as the Movies page -- 2 columns on mobile scaling to 6 on desktop. Includes stagger animation on load."
            ]

            codeBlock """Html.div [
    prop.className (DesignSystem.movieGrid + " " + DesignSystem.staggerGrid)
    prop.children [
        for entry in entries do
            PosterCard.view entry.Slug entry.Name entry.Year entry.PosterRef None
    ]
]"""

            subheading "List Layout"

            Html.p [
                prop.className DesignSystem.secondaryText
                prop.text "Detailed row layout with thumbnail, title, year, genres, and optional rating badge. Same pattern as CatalogDetail and FriendDetail pages. Rows have hover highlight and group-hover effects."
            ]

            codeBlock """Html.div [
    prop.className "bg-base-200/50 rounded-xl p-2 flex flex-col gap-1"
    prop.children [
        for entry in entries do
            Html.div [
                prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 group"
                prop.children [
                    PosterCard.thumbnail entry.PosterRef entry.Name
                    // title + year + genres (flex-1 min-w-0)
                    // optional rating badge (flex-none)
                ]
            ]
    ]
]"""

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
                "Segmented control (icon + label) in a contained pill group. Visually distinct from filter pills which are standalone. The toggle is local React state, not part of the Elmish model, since it's a view preference not application state."
                "Dropdown select (hidden options, extra click). Tab bar (conflicts with page-level navigation). Icon-only toggle (poor discoverability)."

            decisionBox
                "Gallery as Default"
                "Gallery (poster grid) is the default layout because posters are the strongest visual identifier for movies. The dark theme makes posters pop, and the grid gives a quick visual scan of the collection."
                "List as default (too text-heavy for a media app). Table layout (too dense, better for data apps than media libraries)."
        ]
    ]

// ── Section Nav ──

let private sectionNav (activeSection: Section) (dispatch: Msg -> unit) =
    let sections = [
        Overview, "Overview"
        Typography, "Typography"
        Colors, "Colors"
        Spacing, "Spacing"
        Glassmorphism, "Glassmorphism"
        Animations, "Animations"
        Components, "Components"
        ContentBlocks, "Content Blocks"
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
    | Glassmorphism -> glassmorphismSection ()
    | Animations -> animationsSection ()
    | Components -> componentsSection ()
    | ContentBlocks -> contentBlocksSection ()
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
