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

let private navItem (currentPage: Page) (item: NavItem) =
    let isActive = item.IsActive currentPage
    Html.li [
        prop.key item.Label
        prop.children [
            Html.a [
                prop.className (DesignSystem.navItemClass isActive)
                prop.href item.Href
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate item.Href
                )
                prop.children [
                    Html.span [
                        prop.className (if isActive then DesignSystem.navItemActiveIconClass else DesignSystem.navItemIconClass)
                        prop.children [ item.Icon() ]
                    ]
                    Html.span [ prop.text item.Label ]
                ]
            ]
        ]
    ]

let view (currentPage: Page) =
    Html.aside [
        // sticky + h-screen (not min-h-screen) pins the rail to the viewport rather than
        // stretching to document height (design-system-vk7rd) — this is what lets
        // navGroupBottom's mt-auto resolve against the viewport instead of the page foot.
        prop.className "hidden lg:flex flex-col w-64 lg:sticky lg:top-0 lg:h-screen bg-base-200 border-r border-base-300/50"
        prop.children [
            // Logo header with subtle bottom border — Velvet Lobby wordmark (brief 3a):
            // "Media" in Instrument Serif ink + italic gold "theca", plus the
            // dir 3a tagline underneath.
            Html.div [
                prop.className "flex flex-col px-6 py-6 border-b border-base-300/30"
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
                        prop.children [ for item in topNavItems do navItem currentPage item ]
                    ]
                    Html.ul [
                        prop.className DesignSystem.navGroupBottom
                        prop.children [ for item in bottomNavItems do navItem currentPage item ]
                    ]
                ]
            ]
        ]
    ]
