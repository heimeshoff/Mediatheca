module Mediatheca.Client.Pages.EventBrowser.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.EventBrowser.Types

let defaultPageSize = 25

let init () : Model * Cmd<Msg> =
    { Events = []
      Streams = []
      EventTypes = []
      BoundedContexts = []
      Search = ""
      StreamFilter = ""
      EventTypeFilter = ""
      BoundedContextFilter = ""
      TimestampFrom = ""
      TimestampTo = ""
      PageSize = defaultPageSize
      CurrentBefore = None
      CursorStack = []
      HasMore = false
      TotalMatches = 0
      IsLoading = true
      ExpandedEvent = None },
    Cmd.batch [ Cmd.ofMsg Load_filter_options; Cmd.ofMsg (Load_page (None, [])) ]

/// A date-only boundary (from an <input type="date">, "yyyy-MM-dd") widened to
/// the ISO-8601 instant format events.timestamp is stored in (DateTimeOffset's
/// round-trip "o" format), so the server's plain string comparison lines up.
let private startOfDay (date: string) = date + "T00:00:00.0000000+00:00"
let private endOfDay (date: string) = date + "T23:59:59.9999999+00:00"

let private buildQuery (model: Model) (before: int64 option) : EventPageQuery =
    {
        Filter = {
            Search = if model.Search.Trim() = "" then None else Some (model.Search.Trim())
            StreamFilter = if model.StreamFilter = "" then None else Some model.StreamFilter
            EventTypeFilter = if model.EventTypeFilter = "" then None else Some model.EventTypeFilter
            BoundedContext = if model.BoundedContextFilter = "" then None else Some model.BoundedContextFilter
            TimestampFrom = if model.TimestampFrom = "" then None else Some (startOfDay model.TimestampFrom)
            TimestampTo = if model.TimestampTo = "" then None else Some (endOfDay model.TimestampTo)
        }
        Before = before
        PageSize = model.PageSize
    }

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_filter_options ->
        model,
        Cmd.OfAsync.either
            (fun () -> async {
                let! streams = api.getEventStreams ()
                let! eventTypes = api.getEventTypes ()
                let! boundedContexts = api.getBoundedContexts ()
                return streams, eventTypes, boundedContexts
            })
            ()
            (fun (streams, eventTypes, boundedContexts) -> Filter_options_loaded (streams, eventTypes, boundedContexts))
            (fun _ -> Filter_options_loaded ([], [], []))

    | Filter_options_loaded (streams, eventTypes, boundedContexts) ->
        { model with Streams = streams; EventTypes = eventTypes; BoundedContexts = boundedContexts }, Cmd.none

    | Load_page (before, cursorStack) ->
        let query = buildQuery model before
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getEventPage query (fun page -> Page_loaded (before, cursorStack, page))

    | Page_loaded (before, cursorStack, page) ->
        { model with
            Events = page.Events
            HasMore = page.HasMore
            TotalMatches = page.TotalMatches
            CurrentBefore = before
            CursorStack = cursorStack
            IsLoading = false },
        Cmd.none

    | Search_changed value ->
        { model with Search = value }, Cmd.ofMsg (Load_page (None, []))

    | Stream_filter_changed value ->
        { model with StreamFilter = value }, Cmd.ofMsg (Load_page (None, []))

    | Event_type_filter_changed value ->
        { model with EventTypeFilter = value }, Cmd.ofMsg (Load_page (None, []))

    | Bounded_context_filter_changed value ->
        { model with BoundedContextFilter = value }, Cmd.ofMsg (Load_page (None, []))

    | Timestamp_from_changed value ->
        { model with TimestampFrom = value }, Cmd.ofMsg (Load_page (None, []))

    | Timestamp_to_changed value ->
        { model with TimestampTo = value }, Cmd.ofMsg (Load_page (None, []))

    | Page_size_changed size ->
        { model with PageSize = size }, Cmd.ofMsg (Load_page (None, []))

    | Next_page ->
        match model.HasMore, List.tryLast model.Events with
        | true, Some lastEvent ->
            let newBefore = Some lastEvent.GlobalPosition
            let newStack = model.CurrentBefore :: model.CursorStack
            model, Cmd.ofMsg (Load_page (newBefore, newStack))
        | _ -> model, Cmd.none

    | Prev_page ->
        match model.CursorStack with
        | prevBefore :: rest -> model, Cmd.ofMsg (Load_page (prevBefore, rest))
        | [] -> model, Cmd.none

    | Toggle_event_detail pos ->
        let expanded =
            match model.ExpandedEvent with
            | Some p when p = pos -> None
            | _ -> Some pos
        { model with ExpandedEvent = expanded }, Cmd.none
