module Mediatheca.Client.Pages.Settings.State

open Elmish
open Mediatheca.Client.Pages.Settings.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Application Settings" }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
