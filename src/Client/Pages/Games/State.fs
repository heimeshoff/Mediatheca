module Mediatheca.Client.Pages.Games.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Games.Types

let init () : Model * Cmd<Msg> =
    { Games = []
      UpcomingGames = []
      SearchQuery = ""
      StatusFilter = None
      FacetFilter = None
      IsLoading = true },
    Cmd.batch [ Cmd.ofMsg Load_games; Cmd.ofMsg Load_upcoming_games ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_games ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getGames () Games_loaded

    | Games_loaded games ->
        { model with Games = games; IsLoading = false }, Cmd.none

    | Load_upcoming_games ->
        model, Cmd.OfAsync.perform api.getUpcomingGames () Upcoming_games_loaded

    | Upcoming_games_loaded games ->
        { model with UpcomingGames = games }, Cmd.none

    | Search_changed query ->
        { model with SearchQuery = query }, Cmd.none

    | Status_filter_changed status ->
        { model with StatusFilter = status }, Cmd.none

    | Facet_filter_changed facet ->
        { model with FacetFilter = facet }, Cmd.none

    | Open_search_modal ->
        model, Cmd.none
