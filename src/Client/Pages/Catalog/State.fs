module Mediatheca.Client.Pages.Catalog.State

open Elmish
open Mediatheca.Client.Pages.Catalog.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Media Catalog" }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
