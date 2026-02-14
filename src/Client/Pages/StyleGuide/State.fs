module Mediatheca.Client.Pages.StyleGuide.State

open Elmish
open Mediatheca.Client.Pages.StyleGuide.Types

let init () : Model * Cmd<Msg> =
    { ActiveSection = Overview }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Set_section section ->
        { model with ActiveSection = section }, Cmd.none
