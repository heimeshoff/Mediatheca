module Mediatheca.Client.App

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Fable.Remoting.Client
open Mediatheca.Shared
open Mediatheca.Client.State
open Mediatheca.Client.Views

// Side-effect imports for fonts and CSS
importSideEffects "@fontsource/inter/400.css"
importSideEffects "@fontsource/inter/500.css"
importSideEffects "@fontsource/inter/600.css"
importSideEffects "@fontsource/inter/700.css"
importSideEffects "@fontsource/oswald/400.css"
importSideEffects "@fontsource/oswald/500.css"
importSideEffects "@fontsource/oswald/600.css"
importSideEffects "@fontsource/oswald/700.css"
importSideEffects "./index.css"

// API proxy
let api: IMediathecaApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IMediathecaApi>

// Entry point
Program.mkProgram (init api) (update api) view
|> Program.withReactSynchronous "feliz-app"
|> Program.run
