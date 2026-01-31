module Mediatheca.Client.Pages.Movies.State

open Elmish
open Mediatheca.Client.Pages.Movies.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Movies & Series" }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
