module Mediatheca.Client.Pages.Friends.Views

open Feliz
open Mediatheca.Client.Pages.Friends.Types
open Mediatheca.Client.Components

let view (model: Model) (_dispatch: Msg -> unit) =
    PageContainer.view "Friends" [
        Html.p [
            prop.className "text-base-content/70"
            prop.text model.Placeholder
        ]
        Html.p [
            prop.className "mt-4 text-base-content/50"
            prop.text "Manage your friends and shared media here."
        ]
    ]
