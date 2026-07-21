module Mediatheca.Client.Pages.AdminImages.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminImages.Types

let init () : Model * Cmd<Msg> =
    { Stats = None
      IsLoadingStats = true
      OrphanScan = None
      IsLoadingOrphans = true
      Selected = Set.empty
      PendingIntent = None
      IsPurging = false
      LastPurgeResult = None },
    Cmd.ofMsg Load

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        { model with IsLoadingStats = true; IsLoadingOrphans = true },
        Cmd.batch [
            Cmd.OfAsync.perform api.getImageCacheStats () Stats_loaded
            Cmd.OfAsync.perform api.listOrphanedImages () Orphans_loaded
        ]

    | Stats_loaded stats ->
        { model with Stats = Some stats; IsLoadingStats = false }, Cmd.none

    | Orphans_loaded scan ->
        // A fresh scan may have dropped paths that were selected under a
        // previous (now stale) scan — keep only selections still present.
        let selected =
            match scan with
            | OrphanScanReady (orphans, _) ->
                let currentPaths = orphans |> List.map (fun o -> o.RelativePath) |> Set.ofList
                Set.intersect model.Selected currentPaths
            | OrphanScanBlocked _ -> Set.empty
        { model with OrphanScan = Some scan; IsLoadingOrphans = false; Selected = selected }, Cmd.none

    | Toggle_selected path ->
        let selected =
            if Set.contains path model.Selected then Set.remove path model.Selected
            else Set.add path model.Selected
        { model with Selected = selected }, Cmd.none

    | Select_all ->
        match model.OrphanScan with
        | Some (OrphanScanReady (orphans, _)) ->
            { model with Selected = orphans |> List.map (fun o -> o.RelativePath) |> Set.ofList }, Cmd.none
        | _ -> model, Cmd.none

    | Select_none ->
        { model with Selected = Set.empty }, Cmd.none

    | Purge_selected_clicked ->
        { model with PendingIntent = Some PurgeSelectedIntent }, Cmd.none

    | Purge_all_clicked ->
        { model with PendingIntent = Some PurgeAllIntent }, Cmd.none

    | Cancel_purge ->
        { model with PendingIntent = None }, Cmd.none

    | Confirm_purge ->
        match model.PendingIntent with
        | None -> model, Cmd.none
        | Some intent ->
            let selection =
                match intent with
                | PurgeAllIntent -> PurgeAll
                | PurgeSelectedIntent -> PurgeSpecific (Set.toList model.Selected)
            { model with PendingIntent = None; IsPurging = true },
            Cmd.OfAsync.perform api.purgeOrphanedImages selection Purge_completed

    | Purge_completed result ->
        { model with IsPurging = false; LastPurgeResult = Some result; Selected = Set.empty },
        Cmd.ofMsg Load
