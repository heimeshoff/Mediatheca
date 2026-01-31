module Mediatheca.Client.Pages.Settings.Views

open Feliz
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Components

let view (model: Model) (_dispatch: Msg -> unit) =
    PageContainer.view "Settings" [
        Html.p [
            prop.className "text-base-content/70"
            prop.text model.Placeholder
        ]
        Html.p [
            prop.className "mt-4 text-base-content/50"
            prop.text "Configure your Mediatheca preferences here."
        ]
    ]
