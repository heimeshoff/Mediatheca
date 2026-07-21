module Mediatheca.Client.Pages.StreamDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.StreamDetail.Types

let init (streamId: string) : Model * Cmd<Msg> =
    { StreamId = streamId
      Detail = None
      IsLoading = true
      Error = None
      ExpandedEntry = None },
    Cmd.ofMsg Load

let update (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        { model with IsLoading = true; Error = None },
        Cmd.OfAsync.either
            adminApi.getStreamDetail model.StreamId
            Detail_loaded
            (fun ex -> Load_failed ex.Message)

    | Detail_loaded detail ->
        { model with Detail = Some detail; IsLoading = false }, Cmd.none

    | Load_failed err ->
        { model with Error = Some err; IsLoading = false }, Cmd.none

    | Toggle_raw globalPosition ->
        let expanded =
            if model.ExpandedEntry = Some globalPosition then None
            else Some globalPosition
        { model with ExpandedEntry = expanded }, Cmd.none
