module Mediatheca.Client.Pages.Series.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Series.Types

let init () : Model * Cmd<Msg> =
    { Series = []
      SearchQuery = ""
      IsLoading = true },
    Cmd.ofMsg Load_series

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_series ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getSeries () Series_loaded

    | Series_loaded series ->
        { model with Series = series; IsLoading = false }, Cmd.none

    | Search_changed query ->
        { model with SearchQuery = query }, Cmd.none

    | Open_tmdb_search ->
        model, Cmd.none
