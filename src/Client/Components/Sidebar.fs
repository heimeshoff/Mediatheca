module Mediatheca.Client.Components.Sidebar

open Feliz
open Feliz.DaisyUI
open Feliz.Router
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
    { Label = "Friends"; Page = Friend_list; IsActive = Route.isFriendsSection; Icon = Icons.friends; Href = Router.format "friends" }
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
                                        prop.className (
                                            "nav-glow flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all duration-200 "
                                            + if isActive then
                                                "active bg-primary/10 text-primary"
                                              else
                                                "text-base-content/70 hover:text-base-content hover:bg-base-300/50"
                                        )
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
