module Mediatheca.Client.Pages.Friends.State

open Elmish
open Mediatheca.Client.Pages.Friends.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Friends & Sharing" }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp -> model, Cmd.none
