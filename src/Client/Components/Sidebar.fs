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
    { Label = "Movies"; Page = MovieList; IsActive = Route.isMoviesSection; Icon = Icons.movie; Href = Router.format "movies" }
    { Label = "Friends"; Page = FriendList; IsActive = Route.isFriendsSection; Icon = Icons.friends; Href = Router.format "friends" }
    { Label = "Settings"; Page = Settings; IsActive = (fun p -> p = Settings); Icon = Icons.settings; Href = Router.format "settings" }
]

let view (currentPage: Page) =
    Html.aside [
        prop.className "hidden lg:flex flex-col w-64 min-h-screen bg-base-200 border-r border-base-300"
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3 px-6 py-5"
                prop.children [
                    Html.span [ prop.className "text-primary"; prop.children [ Icons.mediatheca () ] ]
                    Html.span [
                        prop.className "text-xl font-bold font-display uppercase tracking-wider"
                        prop.text "Mediatheca"
                    ]
                ]
            ]
            Daisy.menu [
                prop.className "flex-1 px-2"
                prop.children [
                    for item in navItems do
                        Html.li [
                            Html.a [
                                prop.className (if item.IsActive currentPage then "active" else "")
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
