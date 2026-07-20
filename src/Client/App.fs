module Mediatheca.Client.App

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Fable.Remoting.Client
open Mediatheca.Shared
open Mediatheca.Client.State
open Mediatheca.Client.Views

// Side-effect imports for fonts and CSS — Velvet Lobby type foundation:
// Instrument Serif (display/italic voice), Instrument Sans (body/UI),
// Spline Sans Mono (data: dates, durations, counts, ids).
importSideEffects "@fontsource/instrument-serif/400.css"
importSideEffects "@fontsource/instrument-serif/400-italic.css"
importSideEffects "@fontsource/instrument-sans/400.css"
importSideEffects "@fontsource/instrument-sans/500.css"
importSideEffects "@fontsource/instrument-sans/600.css"
importSideEffects "@fontsource/instrument-sans/700.css"
importSideEffects "@fontsource/spline-sans-mono/400.css"
importSideEffects "@fontsource/spline-sans-mono/500.css"
importSideEffects "./index.css"

// API proxies — IMediathecaApi (domain BCs) and IAdminApi (administration
// console; ADR-0004 allows multiple Fable.Remoting APIs) are separate
// contracts served under distinct routes.
let api: IMediathecaApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IMediathecaApi>

let adminApi: IAdminApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder AdminRoute.builder
    |> Remoting.buildProxy<IAdminApi>

// Entry point
Program.mkProgram (init api adminApi) (update api adminApi) view
|> Program.withReactSynchronous "feliz-app"
|> Program.run
