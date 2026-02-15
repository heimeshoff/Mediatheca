module Mediatheca.Client.Pages.Games.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Games.Types

let init () : Model * Cmd<Msg> =
    { Games = []
      SearchQuery = ""
      StatusFilter = None
      IsLoading = true },
    Cmd.ofMsg Load_games

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_games ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getGames () Games_loaded

    | Games_loaded games ->
        { model with Games = games; IsLoading = false }, Cmd.none

    | Search_changed query ->
        { model with SearchQuery = query }, Cmd.none

    | Status_filter_changed status ->
        { model with StatusFilter = status }, Cmd.none

    | Open_search_modal ->
        model, Cmd.none
