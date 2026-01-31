module Mediatheca.Client.Pages.Dashboard.State

open Elmish
open Mediatheca.Client.Pages.Dashboard.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Welcome to Mediatheca" }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
