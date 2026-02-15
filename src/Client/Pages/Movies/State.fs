module Mediatheca.Client.Pages.Movies.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Movies.Types

let init () : Model * Cmd<Msg> =
    { Movies = []
      SearchQuery = ""
      IsLoading = true },
    Cmd.ofMsg Load_movies

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_movies ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getMovies () Movies_loaded

    | Movies_loaded movies ->
        { model with Movies = movies; IsLoading = false }, Cmd.none

    | Search_changed query ->
        { model with SearchQuery = query }, Cmd.none

    | Open_tmdb_search ->
        model, Cmd.none
