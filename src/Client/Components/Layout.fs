module Mediatheca.Client.Components.Layout

open Feliz
open Mediatheca.Client.Router

let view (currentPage: Page) (content: ReactElement) =
    Html.div [
        prop.className "flex min-h-screen bg-base-300"
        prop.children [
            Sidebar.view currentPage
            Html.main [
                // min-w-0 overrides the flex item's default automatic min-width (auto),
                // which otherwise lets a wide horizontally-scrolling descendant (e.g. a
                // poster row with overflow-x-auto) force this whole column wider than the
                // viewport instead of clipping/scrolling internally.
                prop.className "flex-1 min-w-0 pb-20 lg:pb-0"
                prop.children [ content ]
            ]
            BottomNav.view currentPage
        ]
    ]
