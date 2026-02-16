module Mediatheca.Client.Pages.Dashboard.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Dashboard.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Welcome to Mediatheca"
      Stats = None
      RecentMovies = []
      RecentSeries = []
      RecentActivity = []
      IsLoading = true
      JellyfinSyncStatus = Syncing },
    Cmd.none

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp ->
        { model with IsLoading = false }, Cmd.none
    | Stats_loaded stats ->
        { model with Stats = Some stats; IsLoading = false }, Cmd.none
    | Movies_loaded movies ->
        let recent = movies |> List.truncate 4
        { model with RecentMovies = recent; IsLoading = false }, Cmd.none
    | Series_loaded series ->
        let recent = series |> List.truncate 4
        { model with RecentSeries = recent }, Cmd.none
    | Activity_loaded activity ->
        { model with RecentActivity = activity }, Cmd.none
    | Jellyfin_sync_completed result ->
        match result with
        | Ok importResult ->
            { model with JellyfinSyncStatus = Synced importResult }, Cmd.none
        | Error _ ->
            { model with JellyfinSyncStatus = SyncFailed }, Cmd.none
