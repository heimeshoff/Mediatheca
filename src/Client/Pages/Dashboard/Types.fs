module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type JellyfinSyncStatus =
    | Idle
    | Syncing
    | Synced of JellyfinImportResult
    | SyncFailed

type Model = {
    Placeholder: string
    Stats: DashboardStats option
    RecentMovies: MovieListItem list
    RecentSeries: RecentSeriesItem list
    RecentActivity: RecentActivityItem list
    IsLoading: bool
    JellyfinSyncStatus: JellyfinSyncStatus
}

type Msg =
    | NoOp
    | Stats_loaded of DashboardStats
    | Movies_loaded of MovieListItem list
    | Series_loaded of RecentSeriesItem list
    | Activity_loaded of RecentActivityItem list
    | Jellyfin_sync_completed of Result<JellyfinImportResult, string>
