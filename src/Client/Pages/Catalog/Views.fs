module Mediatheca.Client.Pages.Catalog.Views

open Feliz
open Mediatheca.Client.Pages.Catalog.Types
open Mediatheca.Client.Components

let view (model: Model) (_dispatch: Msg -> unit) =
    PageContainer.view "Catalog" [
        Html.p [
            prop.className "text-base-content/70"
            prop.text model.Placeholder
        ]
        Html.p [
            prop.className "mt-4 text-base-content/50"
            prop.text "Browse and search the full media catalog here."
        ]
    ]
