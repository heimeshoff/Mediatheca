module Mediatheca.Client.App

open Fable.Core.JsInterop
open Elmish
open Feliz
open Feliz.DaisyUI
open Fable.Remoting.Client
open Mediatheca.Shared

// Side-effect import for CSS
importSideEffects "./index.css"

// API proxy
let api: IMediathecaApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IMediathecaApi>

// Model
type Model = {
    Message: string
    Loading: bool
}

type Msg =
    | CheckHealth
    | HealthChecked of string
    | HealthCheckFailed of exn

// Init
let init () : Model * Cmd<Msg> =
    { Message = ""; Loading = true },
    Cmd.OfAsync.either api.healthCheck () HealthChecked HealthCheckFailed

// Update
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | CheckHealth ->
        { model with Loading = true },
        Cmd.OfAsync.either api.healthCheck () HealthChecked HealthCheckFailed
    | HealthChecked message ->
        { model with Message = message; Loading = false }, Cmd.none
    | HealthCheckFailed _ ->
        { model with Message = "Failed to connect to server"; Loading = false }, Cmd.none

// View
let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "min-h-screen flex items-center justify-center bg-base-200"
        prop.children [
            Daisy.card [
                prop.className "bg-base-100 shadow-xl p-8"
                prop.children [
                    Html.h1 [
                        prop.className "text-3xl font-bold text-primary mb-4"
                        prop.text "Mediatheca"
                    ]
                    if model.Loading then
                        Daisy.loading [
                            loading.spinner
                            loading.lg
                        ]
                    else
                        Html.p [
                            prop.className "text-lg"
                            prop.text model.Message
                        ]
                    Daisy.button.button [
                        button.primary
                        prop.className "mt-4"
                        prop.text "Check Health"
                        prop.onClick (fun _ -> dispatch CheckHealth)
                    ]
                ]
            ]
        ]
    ]

// Entry point
open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "feliz-app"
|> Program.run
