module Mediatheca.Client.Components.Layout

open Feliz
open Mediatheca.Client.Router

let view (currentPage: Page) (content: ReactElement) =
    Html.div [
        prop.className "flex min-h-screen bg-base-300"
        prop.children [
            Sidebar.view currentPage
            Html.main [
                prop.className "flex-1 pb-20 lg:pb-0"
                prop.children [ content ]
            ]
            BottomNav.view currentPage
        ]
    ]
