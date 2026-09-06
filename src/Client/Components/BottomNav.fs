module Mediatheca.Client.Components.BottomNav

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Router

type DockItem = {
    Label: string
    Page: Page
    IsActive: Page -> bool
    Icon: unit -> ReactElement
    Href: string
}

// design-system-fryq7: mirrors Sidebar.fs's topNavItems membership change —
// Movies/Series/Games items removed, Dashboard stays active on their detail
// pages too (Route.isDashboardSection).
let private dockItems = [
    { Label = "Dashboard"; Page = Dashboard; IsActive = Route.isDashboardSection; Icon = Icons.dashboard; Href = Router.format "" }
    { Label = "Catalogs"; Page = Catalog_list; IsActive = Route.isCatalogsSection; Icon = Icons.catalog; Href = Router.format "catalogs" }
    { Label = "Friends"; Page = Friend_list; IsActive = Route.isFriendsSection; Icon = Icons.friends; Href = Router.format "friends" }
    { Label = "Settings"; Page = Settings; IsActive = (fun p -> p = Settings); Icon = Icons.settings; Href = Router.format "settings" }
]

let view (currentPage: Page) =
    Daisy.dock [
        prop.className "lg:hidden"
        prop.children [
            for item in dockItems do
                Html.a [
                    prop.className (if item.IsActive currentPage then "dock-active" else "")
                    prop.href item.Href
                    prop.onClick (fun e ->
                        e.preventDefault()
                        Router.navigate item.Href
                    )
                    prop.children [
                        item.Icon()
                        Daisy.dockLabel [ prop.text item.Label ]
                    ]
                ]
        ]
    ]
