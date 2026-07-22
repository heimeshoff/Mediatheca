module Mediatheca.Client.Pages.StreamDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.StreamDetail.Types

let init (streamId: string) : Model * Cmd<Msg> =
    { StreamId = streamId
      Detail = None
      IsLoading = true
      Error = None
      ExpandedEntry = None
      Composer = ComposerState.empty },
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

    // ── Compensating-event composer (administration-xjmda, ADR-0032) ──

    | Open_composer ->
        { model with Composer = { ComposerState.empty with IsOpen = true; TypesLoading = true } },
        Cmd.OfAsync.either
            adminApi.getCompensatingEventTypes model.StreamId
            Composer_types_loaded
            (fun ex -> Composer_types_failed ex.Message)

    | Close_composer ->
        { model with Composer = ComposerState.empty }, Cmd.none

    | Composer_types_loaded types ->
        { model with Composer = { model.Composer with Types = types; TypesLoading = false } }, Cmd.none

    | Composer_types_failed err ->
        { model with Composer = { model.Composer with TypesLoading = false; TypesError = Some err } }, Cmd.none

    | Select_event_type eventType ->
        { model with
            Composer = { model.Composer with
                            SelectedType = Some eventType
                            Payload = ""
                            TemplateFromOtherStream = false
                            TemplateLoading = true
                            Preview = None
                            PreviewError = None } },
        Cmd.OfAsync.either
            (adminApi.getCompensatingEventTemplate model.StreamId)
            eventType
            Composer_template_loaded
            (fun ex -> Composer_template_failed ex.Message)

    | Composer_template_loaded template ->
        let payload, fromOther =
            match template with
            | Some t -> t.Data, t.FromOtherStream
            | None -> "", false
        { model with Composer = { model.Composer with TemplateLoading = false; Payload = payload; TemplateFromOtherStream = fromOther } }, Cmd.none

    | Composer_template_failed err ->
        { model with Composer = { model.Composer with TemplateLoading = false; TypesError = Some err } }, Cmd.none

    | Payload_edited text ->
        { model with Composer = { model.Composer with Payload = text; Preview = None; PreviewError = None } }, Cmd.none

    | Request_preview ->
        match model.Composer.SelectedType with
        | None -> model, Cmd.none
        | Some eventType ->
            { model with Composer = { model.Composer with PreviewLoading = true; PreviewError = None; Preview = None } },
            Cmd.OfAsync.either
                (adminApi.previewCompensatingEvent model.StreamId eventType)
                model.Composer.Payload
                Preview_result
                (fun ex -> Preview_result (Error ex.Message))

    | Preview_result (Ok preview) ->
        { model with Composer = { model.Composer with PreviewLoading = false; Preview = Some preview } }, Cmd.none

    | Preview_result (Error err) ->
        { model with Composer = { model.Composer with PreviewLoading = false; PreviewError = Some err } }, Cmd.none

    | Close_preview ->
        { model with Composer = { model.Composer with Preview = None } }, Cmd.none

    | Confirm_append ->
        match model.Composer.SelectedType, model.Composer.Preview with
        | Some eventType, Some preview ->
            { model with Composer = { model.Composer with AppendInFlight = true; AppendError = None } },
            Cmd.OfAsync.either
                (adminApi.appendCompensatingEvent model.StreamId eventType model.Composer.Payload)
                preview.ExpectedPosition
                Append_result
                (fun ex -> Append_result (Error ex.Message))
        | _ -> model, Cmd.none

    | Append_result (Ok ()) ->
        { model with Composer = ComposerState.empty }, Cmd.ofMsg Load

    | Append_result (Error err) ->
        { model with Composer = { model.Composer with AppendInFlight = false; AppendError = Some err } }, Cmd.none
