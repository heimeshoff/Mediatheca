module Mediatheca.Client.Components.Sidebar

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client
open Mediatheca.Client.Router

type NavItem = {
    Label: string
    Page: Page
    IsActive: Page -> bool
    Icon: unit -> ReactElement
    Href: string
}

// Top group: primary destinations. Bottom group: pinned to the foot of the
// rail via `mt-auto` (styleguide.md § 4 Sidebar nav — design-system-t4b9k,
// active-tab treatment reverted to dir 3a's burgundy fill by design-system-grtw7).
let private topNavItems = [
    { Label = "Dashboard"; Page = Dashboard; IsActive = (fun p -> p = Dashboard); Icon = Icons.dashboard; Href = Router.format "" }
    { Label = "Movies"; Page = Movie_list; IsActive = Route.isMoviesSection; Icon = Icons.movie; Href = Router.format "movies" }
    { Label = "TV Series"; Page = Series_list; IsActive = Route.isSeriesSection; Icon = Icons.tv; Href = Router.format "series" }
    { Label = "Games"; Page = Game_list; IsActive = Route.isGamesSection; Icon = Icons.gamepad; Href = Router.format "games" }
    { Label = "Catalogs"; Page = Catalog_list; IsActive = Route.isCatalogsSection; Icon = Icons.catalog; Href = Router.format "catalogs" }
    { Label = "Friends"; Page = Friend_list; IsActive = Route.isFriendsSection; Icon = Icons.friends; Href = Router.format "friends" }
]

// The former Admin/Settings split (two buttons for what read as one
// destination) is gone (administration-k3vmt): the whole admin console
// dissolved into inline sections on Settings, so this group is down to one
// item. Also fixes BottomNav's mobile dock, which never carried an Admin
// item — Settings now reaches the entire console there too.
let private bottomNavItems = [
    { Label = "Settings"; Page = Settings; IsActive = Route.isSettingsSection; Icon = Icons.settings; Href = Router.format "settings" }
]

// Collapsed/expanded is a viewport preference, not an event-sourced
// observation of the user's engagement (ADR-0043's test) — plain
// `localStorage`, read synchronously on mount so a collapsed rail never
// flashes expanded before settling (design-system-n8zqr).
[<Literal>]
let private CollapsedStorageKey = "mediatheca.sidebarCollapsed"

let private readStoredCollapsed () =
    Browser.Dom.window.localStorage.getItem(CollapsedStorageKey) = "true"

let private writeStoredCollapsed (collapsed: bool) =
    Browser.Dom.window.localStorage.setItem(CollapsedStorageKey, (if collapsed then "true" else "false"))

/// The hovered nav item's tooltip target, measured synchronously at hover
/// time off the trigger's own `getBoundingClientRect()` — the trigger is
/// already mounted when the mouse event fires, so no ref/effect indirection
/// is needed (contrast `GameDetail`'s `HeroRating`, whose trigger mounts
/// conditionally).
type private TooltipTarget = {
    Label: string
    Top: float
    Left: float
}

let private navItem
    (currentPage: Page)
    (collapsed: bool)
    (getRailRight: unit -> float)
    (setTooltip: TooltipTarget option -> unit)
    (item: NavItem)
    =
    let isActive = item.IsActive currentPage
    Html.li [
        prop.key item.Label
        prop.children [
            Html.a [
                prop.className (
                    DesignSystem.navItemClass isActive
                    + (if collapsed then " justify-center" else "")
                )
                prop.href item.Href
                // Always set (not just collapsed) — the accessible name
                // matches the visible label either way, so this is a no-op
                // for the expanded state and the load-bearing name source
                // once the label span stops rendering while collapsed.
                prop.ariaLabel item.Label
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate item.Href
                )
                // Tooltip only fires collapsed — expanded, the label is
                // already on screen (task "What" § Tooltip). Positioned off
                // the rail's own right edge (not the trigger's — the item
                // has its own inset padding), vertically centered on the
                // hovered icon.
                prop.onMouseEnter (fun e ->
                    if collapsed then
                        let el = e.currentTarget :?> Browser.Types.HTMLElement
                        let rect = el.getBoundingClientRect ()
                        setTooltip (
                            Some {
                                Label = item.Label
                                Top = rect.top + rect.height / 2.0
                                Left = getRailRight () + 8.0
                            }
                        ))
                prop.onMouseLeave (fun _ -> if collapsed then setTooltip None)
                prop.children [
                    Html.span [
                        prop.className (if isActive then DesignSystem.navItemActiveIconClass else DesignSystem.navItemIconClass)
                        prop.children [ item.Icon() ]
                    ]
                    if not collapsed then
                        Html.span [ prop.text item.Label ]
                ]
            ]
        ]
    ]

let private toggleButton (collapsed: bool) (onToggle: unit -> unit) =
    Html.button [
        prop.type' "button"
        prop.className "flex-shrink-0 flex items-center justify-center w-6 h-6 rounded-md text-ink-muted hover:text-base-content hover:bg-base-300/50 transition-colors cursor-pointer"
        prop.ariaLabel (if collapsed then "Expand sidebar" else "Collapse sidebar")
        prop.onClick (fun _ -> onToggle ())
        prop.children [
            Html.span [
                prop.className ("transition-transform duration-200 " + (if collapsed then "rotate-180" else ""))
                prop.children [ Icons.chevronLeft () ]
            ]
        ]
    ]

[<ReactComponent>]
let view (currentPage: Page) =
    let collapsed, setCollapsed = React.useState (fun () -> readStoredCollapsed ())
    let tooltip, setTooltip = React.useState<TooltipTarget option> (None)
    let railRef = React.useElementRef ()

    let getRailRight () =
        match railRef.current with
        | Some el -> el.getBoundingClientRect().right
        | None -> 0.0

    let toggle () =
        let next = not collapsed
        writeStoredCollapsed next
        setCollapsed next
        // A stale tooltip pinned to a now-hidden/relaid-out trigger would be
        // wrong either way (expanding removes tooltips entirely).
        setTooltip None

    Html.aside [
        prop.ref railRef
        // sticky + h-screen (not min-h-screen) pins the rail to the viewport rather than
        // stretching to document height (design-system-vk7rd) — this is what lets
        // navGroupBottom's mt-auto resolve against the viewport instead of the page foot.
        // Width animates between the two states (design-system-n8zqr); `main` needs no
        // compensation since the rail stays in flow (Components/Layout.fs's flex row).
        prop.className (
            "hidden lg:flex flex-col lg:sticky lg:top-0 lg:h-screen bg-base-200 border-r border-base-300/50 transition-[width] duration-200 ease-out "
            + (if collapsed then "w-16 " else "w-64 ")
            + (if collapsed then DesignSystem.navRailCollapsed else "")
        )
        prop.children [
            // Logo header with subtle bottom border — Velvet Lobby wordmark (brief 3a):
            // "Media" in Instrument Serif ink + italic gold "theca", plus the
            // dir 3a tagline underneath. Collapsed: the wordmark reduces to the
            // mark alone ("Media*theca*" doesn't survive at 64px) and the
            // tagline is hidden; the collapse toggle lives here in both states.
            if collapsed then
                Html.div [
                    prop.className "flex flex-col items-center gap-3 px-2 py-6 border-b border-base-300/30"
                    prop.children [
                        Html.span [
                            prop.className "text-primary drop-shadow-[0_0_8px_oklch(0.80_0.12_82_/_0.4)]"
                            prop.children [ Icons.mediatheca () ]
                        ]
                        toggleButton collapsed toggle
                    ]
                ]
            else
                Html.div [
                    prop.className "flex flex-col px-6 py-6 border-b border-base-300/30"
                    prop.children [
                        Html.div [
                            prop.className "flex items-center justify-between gap-3"
                            prop.children [
                                Html.div [
                                    prop.className "flex items-center gap-3"
                                    prop.children [
                                        Html.span [
                                            prop.className "text-primary drop-shadow-[0_0_8px_oklch(0.80_0.12_82_/_0.4)]"
                                            prop.children [ Icons.mediatheca () ]
                                        ]
                                        Html.span [
                                            prop.className "font-display text-2xl leading-none text-base-content"
                                            prop.children [
                                                Html.text "Media"
                                                Html.span [ prop.className "italic text-primary"; prop.text "theca" ]
                                            ]
                                        ]
                                    ]
                                ]
                                toggleButton collapsed toggle
                            ]
                        ]
                        Html.div [
                            prop.className DesignSystem.navTagline
                            prop.text "Where entertainment lives"
                        ]
                    ]
                ]
            // Navigation — top group (primary destinations) + bottom group
            // (Events/Settings, pinned via mt-auto).
            Html.nav [
                // overflow-y-auto: on viewports too short to fit every item, the nav column
                // scrolls internally rather than clipping the bottom group off the end.
                prop.className "flex-1 flex flex-col px-3 py-4 overflow-y-auto"
                prop.children [
                    Html.ul [
                        prop.className DesignSystem.navGroupTop
                        prop.children [ for item in topNavItems do navItem currentPage collapsed getRailRight setTooltip item ]
                    ]
                    Html.ul [
                        prop.className DesignSystem.navGroupBottom
                        prop.children [ for item in bottomNavItems do navItem currentPage collapsed getRailRight setTooltip item ]
                    ]
                ]
            ]
            // Collapsed-only tooltip (paper overlay, ADR-0016) — `position:
            // fixed` off the rail's own right edge + the hovered trigger's
            // vertical center, so it escapes `nav`'s `overflow-y-auto`
            // clipping regardless of where in the (possibly
            // internally-scrolled) list the item sits.
            match tooltip with
            | Some t ->
                Html.div [
                    prop.className DesignSystem.tooltip
                    prop.style [
                        style.custom ("position", "fixed")
                        style.top (length.px t.Top)
                        style.left (length.px t.Left)
                        style.custom ("transform", "translateY(-50%)")
                    ]
                    prop.text t.Label
                ]
            | None -> ()
        ]
    ]
