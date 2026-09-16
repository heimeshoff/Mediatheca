module Mediatheca.Client.Pages.AdminHealth.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminHealth.Types

let init () : Model * Cmd<Msg> =
    { Stats = None
      IsLoading = true
      CatalogBackfillRunning = false
      CatalogBackfillReport = None },
    Cmd.ofMsg Load

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getHealthStats () Stats_loaded

    | Stats_loaded stats ->
        { model with Stats = Some stats; IsLoading = false }, Cmd.none

    | Run_catalog_media_type_backfill_clicked ->
        { model with CatalogBackfillRunning = true },
        Cmd.OfAsync.perform api.backfillCatalogEntryMediaTypes () Catalog_media_type_backfill_completed

    | Catalog_media_type_backfill_completed report ->
        { model with CatalogBackfillReport = Some report; CatalogBackfillRunning = false }, Cmd.none
