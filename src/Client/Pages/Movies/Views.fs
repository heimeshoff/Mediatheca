module Mediatheca.Client.Pages.Movies.Views

open Feliz
open Mediatheca.Client.Pages.Movies.Types
open Mediatheca.Client.Components

let view (model: Model) (_dispatch: Msg -> unit) =
    PageContainer.view "Movies" [
        Html.p [
            prop.className "text-base-content/70"
            prop.text model.Placeholder
        ]
        Html.p [
            prop.className "mt-4 text-base-content/50"
            prop.text "Your movie collection will appear here."
        ]
    ]
