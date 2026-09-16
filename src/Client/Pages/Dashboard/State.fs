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
    | BooksTab ->
        Cmd.OfAsync.either
            api.getDashboardBooksTab ()
            BooksTabLoaded
            (fun ex -> TabLoadError ex.Message)

let private fetchAchievements (api: IMediathecaApi) : Cmd<Msg> =
    Cmd.OfAsync.either
        api.getSteamRecentAchievements ()
        AchievementsLoaded
        (fun ex -> AchievementsLoaded (Error ex.Message))

let private fetchExpandedItems (api: IMediathecaApi) (card: DashboardCard) : Cmd<Msg> =
    Cmd.OfAsync.either
        api.getDashboardCardItems (DashboardCard.query card)
        (fun items -> ExpandedItemsLoaded (card, Ok items))
        (fun ex -> ExpandedItemsLoaded (card, Error ex.Message))

/// DOM id of the expanded card's surface. `Views.fs`'s `growingTabArea` reads
/// this to scroll the grown card into view (instantly, from its
/// `useLayoutEffect`, ADR-0073 §1/§6) — the 50ms `setTimeout` guess that used
/// to live here is retired; the layout effect fires after React's own commit,
/// which is the actual thing the guess was standing in for.
let expandedCardElementId = "dashboard-expanded-card"

let init () : Model * Cmd<Msg> =
    { ActiveTab = All
      AllTabData = None
      MoviesTabData = None
      SeriesTabData = None
      GamesTabData = None
      BooksTabData = None
      Achievements = AchievementsNotLoaded
      IsLoading = true
      IsSyncing = false
      Expanded = None },
    Cmd.none

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SwitchTab tab ->
        let cmd = fetchTabData api tab
        { model with ActiveTab = tab; IsLoading = true; Expanded = None }, cmd

    | AllTabLoaded data ->
        { model with AllTabData = Some data; IsLoading = false }, Cmd.none

    | MoviesTabLoaded data ->
        { model with MoviesTabData = Some data; IsLoading = false }, Cmd.none

    | SeriesTabLoaded data ->
        { model with SeriesTabData = Some data; IsLoading = false }, Cmd.none

    | GamesTabLoaded data ->
        let achievementsCmd =
            match model.Achievements with
            | AchievementsNotLoaded -> fetchAchievements api
            | _ -> Cmd.none
        { model with GamesTabData = Some data; IsLoading = false; Achievements = if model.Achievements = AchievementsNotLoaded then AchievementsLoading else model.Achievements }, achievementsCmd

    | BooksTabLoaded data ->
        { model with BooksTabData = Some data; IsLoading = false }, Cmd.none

    | TabLoadError _ ->
        { model with IsLoading = false }, Cmd.none

    | AchievementsLoaded result ->
        match result with
        | Ok achievements ->
            { model with Achievements = AchievementsReady achievements }, Cmd.none
        | Error msg ->
            { model with Achievements = AchievementsError msg }, Cmd.none

    | TriggerPlaytimeSync ->
        let cmd =
            Cmd.OfAsync.either
                api.triggerPlaytimeSync ()
                PlaytimeSyncCompleted
                (fun ex -> PlaytimeSyncCompleted (Error ex.Message))
        { model with IsSyncing = true }, cmd

    | PlaytimeSyncCompleted _ ->
        let refreshCmds =
            Cmd.batch [
                fetchTabData api GamesTab
                fetchTabData api All
            ]
        { model with IsSyncing = false }, refreshCmds

    | Open_search_modal ->
        // Intercepted by root State.fs (Dashboard_msg branch), mirroring the
        // Games/Movies/Series Open_search_modal pattern — no-op here.
        model, Cmd.none

    | ExpandCard card ->
        { model with Expanded = Some { Card = card; Items = ExpandedLoading } },
        fetchExpandedItems api card

    | CollapseCard ->
        { model with Expanded = None }, Cmd.none

    | ExpandedItemsLoaded (card, result) ->
        match model.Expanded with
        | Some expanded when expanded.Card = card ->
            let items =
                match result with
                | Ok fetched -> ExpandedReady fetched
                | Error message -> ExpandedFailed message
            { model with Expanded = Some { expanded with Items = items } }, Cmd.none
        | _ ->
            // Stale reply: the card was collapsed, or another card was
            // expanded, while this fetch was in flight.
            model, Cmd.none
