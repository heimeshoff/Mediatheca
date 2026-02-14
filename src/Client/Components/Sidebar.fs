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

let private navItems = [
    { Label = "Dashboard"; Page = Dashboard; IsActive = (fun p -> p = Dashboard); Icon = Icons.dashboard; Href = Router.format "" }
    { Label = "Movies"; Page = Movie_list; IsActive = Route.isMoviesSection; Icon = Icons.movie; Href = Router.format "movies" }
    { Label = "TV Series"; Page = Series_list; IsActive = Route.isSeriesSection; Icon = Icons.tv; Href = Router.format "series" }
    { Label = "Catalogs"; Page = Catalog_list; IsActive = Route.isCatalogsSection; Icon = Icons.catalog; Href = Router.format "catalogs" }
    { Label = "Friends"; Page = Friend_list; IsActive = Route.isFriendsSection; Icon = Icons.friends; Href = Router.format "friends" }
    { Label = "Events"; Page = Event_browser; IsActive = (fun p -> p = Event_browser); Icon = Icons.events; Href = Router.format "events" }
    { Label = "Settings"; Page = Settings; IsActive = (fun p -> p = Settings); Icon = Icons.settings; Href = Router.format "settings" }
]

let view (currentPage: Page) =
    Html.aside [
        prop.className "hidden lg:flex flex-col w-64 min-h-screen bg-base-200/80 backdrop-blur-sm border-r border-base-300/50"
        prop.children [
            // Logo header with subtle bottom border
            Html.div [
                prop.className "flex items-center gap-3 px-6 py-6 border-b border-base-300/30"
                prop.children [
                    Html.span [
                        prop.className "text-primary drop-shadow-[0_0_8px_oklch(86.133%_0.141_139.549_/_0.4)]"
                        prop.children [ Icons.mediatheca () ]
                    ]
                    Html.span [
                        prop.className "text-xl font-bold font-display uppercase tracking-wider text-gradient-primary"
                        prop.text "Mediatheca"
                    ]
                ]
            ]
            // Navigation
            Html.nav [
                prop.className "flex-1 px-3 py-4"
                prop.children [
                    Html.ul [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            for item in navItems do
                                let isActive = item.IsActive currentPage
                                Html.li [
                                    Html.a [
                                        prop.className (DesignSystem.navItemClass isActive)
                                        prop.href item.Href
                                        prop.onClick (fun e ->
                                            e.preventDefault()
                                            Router.navigate item.Href
                                        )
                                        prop.children [
                                            item.Icon()
                                            Html.span [ prop.text item.Label ]
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]
