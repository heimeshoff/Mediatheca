module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type Model = {
    Placeholder: string
    Stats: DashboardStats option
    RecentMovies: MovieListItem list
    RecentActivity: RecentActivityItem list
    IsLoading: bool
}

type Msg =
    | NoOp
    | Stats_loaded of DashboardStats
    | Movies_loaded of MovieListItem list
    | Activity_loaded of RecentActivityItem list
