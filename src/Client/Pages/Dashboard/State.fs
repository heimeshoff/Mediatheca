module Mediatheca.Client.Pages.Dashboard.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Dashboard.Types

let private fetchTabData (api: IMediathecaApi) (tab: DashboardTab) : Cmd<Msg> =
    match tab with
    | All ->
        Cmd.OfAsync.either
            api.getDashboardAllTab ()
            AllTabLoaded
            (fun ex -> TabLoadError ex.Message)
    | MoviesTab ->
        Cmd.OfAsync.either
            api.getDashboardMoviesTab ()
            MoviesTabLoaded
            (fun ex -> TabLoadError ex.Message)
    | SeriesTab ->
        Cmd.OfAsync.either
            api.getDashboardSeriesTab ()
            SeriesTabLoaded
            (fun ex -> TabLoadError ex.Message)
    | GamesTab ->
        Cmd.OfAsync.either
            api.getDashboardGamesTab ()
            GamesTabLoaded
            (fun ex -> TabLoadError ex.Message)

let init () : Model * Cmd<Msg> =
    { ActiveTab = All
      AllTabData = None
      MoviesTabData = None
      SeriesTabData = None
      GamesTabData = None
      IsLoading = true },
    Cmd.none

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SwitchTab tab ->
        let cmd = fetchTabData api tab
        { model with ActiveTab = tab; IsLoading = true }, cmd

    | AllTabLoaded data ->
        { model with AllTabData = Some data; IsLoading = false }, Cmd.none

    | MoviesTabLoaded data ->
        { model with MoviesTabData = Some data; IsLoading = false }, Cmd.none

    | SeriesTabLoaded data ->
        { model with SeriesTabData = Some data; IsLoading = false }, Cmd.none

    | GamesTabLoaded data ->
        { model with GamesTabData = Some data; IsLoading = false }, Cmd.none

    | TabLoadError _ ->
        { model with IsLoading = false }, Cmd.none
