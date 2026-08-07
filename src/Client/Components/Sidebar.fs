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

/// The wordmark *is* the collapse control (no chevron — design-system-n8zqr's
/// separate toggle button is gone): clicking the title collapses the rail,
/// clicking the lone mark expands it again. Collapsed, the mark borrows the
/// nav items' hover tooltip to name the otherwise-invisible affordance.
let private brandButton
    (collapsed: bool)
    (getRailRight: unit -> float)
    (setTooltip: TooltipTarget option -> unit)
    (onToggle: unit -> unit)
    (children: ReactElement list)
    =
    Html.button [
        prop.type' "button"
        prop.className (
            "flex items-center gap-3 rounded-lg cursor-pointer transition-opacity hover:opacity-80 "
            + (if collapsed then "justify-center" else "text-left")
        )
        prop.ariaLabel (if collapsed then "Expand sidebar" else "Collapse sidebar")
        prop.ariaExpanded (not collapsed)
        prop.onClick (fun _ -> onToggle ())
        prop.onMouseEnter (fun e ->
            if collapsed then
                let el = e.currentTarget :?> Browser.Types.HTMLElement
                let rect = el.getBoundingClientRect ()
                setTooltip (
                    Some {
                        Label = "Expand sidebar"
                        Top = rect.top + rect.height / 2.0
                        Left = getRailRight () + 8.0
                    }
                ))
        prop.onMouseLeave (fun _ -> if collapsed then setTooltip None)
        prop.children children
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
        // Collapsed, the fill drops away entirely — the icon strip sits straight on the
        // page rather than on its own `bg-base-200` panel — and fades with the width so
        // the two states cross over as one motion.
        prop.className (
            "hidden lg:flex flex-col lg:sticky lg:top-0 lg:h-screen border-r border-base-300/50 transition-[width,background-color] duration-200 ease-out "
            + (if collapsed then "w-16 bg-transparent " else "w-64 bg-base-200 ")
            + (if collapsed then DesignSystem.navRailCollapsed else "")
        )
        prop.children [
            // Logo header with subtle bottom border — Velvet Lobby wordmark (brief 3a):
            // "Media" in Instrument Serif ink + italic gold "theca", plus the
            // dir 3a tagline underneath. Collapsed: the wordmark reduces to the
            // mark alone ("Media*theca*" doesn't survive at 64px). The header
            // itself is the collapse control in both states — see `brandButton`.
            //
            // One header for both states, not a branch per state: the header's
            // height sets where the nav below it starts, so a collapsed variant
            // that simply dropped the tagline made every nav icon jump ~16px up
            // on toggle. The tagline goes `invisible` instead of unmounting —
            // its box still reserves the height, and `visibility: hidden` keeps
            // it out of the a11y tree (and out of Playwright's `toBeVisible`).
            // The 32px mark drives the brand row's height either way, so the
            // rest lines up on its own.
            Html.div [
                prop.className (
                    "flex flex-col py-6 border-b border-base-300/30 overflow-hidden "
                    + (if collapsed then "items-center px-2" else "px-6")
                )
                prop.children [
                    brandButton collapsed getRailRight setTooltip toggle [
                        Html.span [
                            prop.className "text-primary drop-shadow-[0_0_8px_oklch(0.80_0.12_82_/_0.4)]"
                            prop.children [ Icons.mediatheca () ]
                        ]
                        if not collapsed then
                            Html.span [
                                prop.className "font-display text-2xl leading-none text-base-content"
                                prop.children [
                                    Html.text "Media"
                                    Html.span [ prop.className "italic text-primary"; prop.text "theca" ]
                                ]
                            ]
                    ]
                    Html.div [
                        prop.className (DesignSystem.navTagline + (if collapsed then " invisible" else ""))
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
            //
            // Portalled to `document.body`: `position: sticky` makes the rail
            // its own stacking context, so the tooltip's z-index could only
            // ever compete *inside* the rail — page content painted later in
            // tree order (movie/series cards and their z-indexed overlays)
            // covered it. As the body's last child it outranks the page.
            match tooltip with
            | Some t ->
                ReactDOM.createPortal (
                    Html.div [
                        prop.className DesignSystem.tooltip
                        prop.style [
                            style.custom ("position", "fixed")
                            style.top (length.px t.Top)
                            style.left (length.px t.Left)
                            style.custom ("transform", "translateY(-50%)")
                        ]
                        prop.text t.Label
                    ],
                    Browser.Dom.document.body
                )
            | None -> ()
        ]
    ]
