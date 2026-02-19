module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type DashboardTab =
    | All
    | MoviesTab
    | SeriesTab
    | GamesTab

type Model = {
    ActiveTab: DashboardTab
    AllTabData: DashboardAllTab option
    MoviesTabData: DashboardMoviesTab option
    SeriesTabData: DashboardSeriesTab option
    GamesTabData: DashboardGamesTab option
    IsLoading: bool
}

type Msg =
    | SwitchTab of DashboardTab
    | AllTabLoaded of DashboardAllTab
    | MoviesTabLoaded of DashboardMoviesTab
    | SeriesTabLoaded of DashboardSeriesTab
    | GamesTabLoaded of DashboardGamesTab
    | TabLoadError of string
