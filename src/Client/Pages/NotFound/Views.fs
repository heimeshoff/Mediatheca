module Mediatheca.Client.Pages.NotFound.Views

open Feliz
open Feliz.Router
open Mediatheca.Client.Components

let view () =
    PageContainer.view "Page Not Found" [
        Html.p [
            prop.className "text-base-content/70"
            prop.text "The page you're looking for doesn't exist."
        ]
        Html.a [
            prop.className "link link-primary mt-4 inline-block"
            prop.href (Router.format "")
            prop.onClick (fun e ->
                e.preventDefault()
                Router.navigate ""
            )
            prop.text "Go to Dashboard"
        ]
    ]
