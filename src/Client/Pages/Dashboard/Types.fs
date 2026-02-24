module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type DashboardTab =
    | All
    | MoviesTab
    | SeriesTab
    | GamesTab

type AchievementsState =
    | AchievementsNotLoaded
    | AchievementsLoading
    | AchievementsReady of SteamAchievement list
    | AchievementsError of string

type Model = {
    ActiveTab: DashboardTab
    AllTabData: DashboardAllTab option
    MoviesTabData: DashboardMoviesTab option
    SeriesTabData: DashboardSeriesTab option
    GamesTabData: DashboardGamesTab option
    Achievements: AchievementsState
    IsLoading: bool
}

type Msg =
    | SwitchTab of DashboardTab
    | AllTabLoaded of DashboardAllTab
    | MoviesTabLoaded of DashboardMoviesTab
    | SeriesTabLoaded of DashboardSeriesTab
    | GamesTabLoaded of DashboardGamesTab
    | TabLoadError of string
    | AchievementsLoaded of Result<SteamAchievement list, string>
