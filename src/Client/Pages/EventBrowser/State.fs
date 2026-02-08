module Mediatheca.Client.Pages.EventBrowser.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.EventBrowser.Types

let init () : Model * Cmd<Msg> =
    { Events = []
      Streams = []
      EventTypes = []
      StreamFilter = ""
      EventTypeFilter = ""
      IsLoading = true
      ExpandedEvent = None },
    Cmd.ofMsg Load_events

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_events ->
        let query: EventQuery = {
            StreamFilter = if model.StreamFilter = "" then None else Some model.StreamFilter
            EventTypeFilter = if model.EventTypeFilter = "" then None else Some model.EventTypeFilter
            Limit = 100
            Offset = 0
        }
        { model with IsLoading = true },
        Cmd.batch [
            Cmd.OfAsync.perform api.getEvents query Events_loaded
            Cmd.OfAsync.perform api.getEventStreams () Streams_loaded
            Cmd.OfAsync.perform api.getEventTypes () Event_types_loaded
        ]

    | Events_loaded events ->
        { model with Events = events; IsLoading = false }, Cmd.none

    | Streams_loaded streams ->
        { model with Streams = streams }, Cmd.none

    | Event_types_loaded types ->
        { model with EventTypes = types }, Cmd.none

    | Stream_filter_changed filter ->
        { model with StreamFilter = filter }, Cmd.ofMsg Load_events

    | Event_type_filter_changed filter ->
        { model with EventTypeFilter = filter }, Cmd.ofMsg Load_events

    | Toggle_event_detail pos ->
        let expanded =
            match model.ExpandedEvent with
            | Some p when p = pos -> None
            | _ -> Some pos
        { model with ExpandedEvent = expanded }, Cmd.none
