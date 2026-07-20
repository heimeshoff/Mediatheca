module Mediatheca.Client.Pages.EventBrowser.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.EventBrowser.Types

let defaultPageSize = 25

/// Follow-mode poll interval (administration-mtf1f). A client-side constant,
/// not configurable — polling is fine for a single-user app (see the task's
/// Notes); the SSE pattern is reserved for rebuild progress.
let pollIntervalMs = 2000

/// Cap on a single tail poll response, so a burst of writes between polls
/// can't return an unbounded batch.
let private tailLimit = 200

/// Cap on the rows kept in the model while following, so a long-running
/// follow session doesn't grow the list without bound (the older rows fall
/// off the bottom; the point of Follow is watching the newest activity, not
/// building a full history — pagination stays available for that).
let private maxFollowedRows = 200

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
      ExpandedEvent = None
      Following = false
      FollowEpoch = 0
      TailPosition = None
      NewlyArrived = Set.empty },
    Cmd.batch [ Cmd.ofMsg Load_filter_options; Cmd.ofMsg (Load_page (None, [])) ]

/// A date-only boundary (from an <input type="date">, "yyyy-MM-dd") widened to
/// the ISO-8601 instant format events.timestamp is stored in (DateTimeOffset's
/// round-trip "o" format), so the server's plain string comparison lines up.
let private startOfDay (date: string) = date + "T00:00:00.0000000+00:00"
let private endOfDay (date: string) = date + "T23:59:59.9999999+00:00"

/// The active filter set, shared as-is between the paged explorer query and
/// the live-tail query (administration-mtf1f) — see EventFilter's doc comment.
let private buildFilter (model: Model) : EventFilter =
    {
        Search = if model.Search.Trim() = "" then None else Some (model.Search.Trim())
        StreamFilter = if model.StreamFilter = "" then None else Some model.StreamFilter
        EventTypeFilter = if model.EventTypeFilter = "" then None else Some model.EventTypeFilter
        BoundedContext = if model.BoundedContextFilter = "" then None else Some model.BoundedContextFilter
        TimestampFrom = if model.TimestampFrom = "" then None else Some (startOfDay model.TimestampFrom)
        TimestampTo = if model.TimestampTo = "" then None else Some (endOfDay model.TimestampTo)
    }

let private buildQuery (model: Model) (before: int64 option) : EventPageQuery =
    {
        Filter = buildFilter model
        Before = before
        PageSize = model.PageSize
    }

/// Stop following: bump the epoch (invalidating any scheduled poll or
/// in-flight response) and turn Following off. Used by the explicit toggle,
/// by pagination away from the live edge, and — via `Admin.State.stopFollowing`
/// — by root `State.Url_changed` when navigating off the Admin page entirely
/// (administration-mtf1f iteration 2). Not `private`: the navigation-away case
/// has to reach in from outside this module, since Elmish has no
/// `componentWillUnmount` to hook and `AdminModel` is otherwise left untouched
/// by every other page's branch in `Url_changed`.
let stopFollowing (model: Model) : Model =
    { model with Following = false; FollowEpoch = model.FollowEpoch + 1 }

/// Reschedule the next tail poll after `pollIntervalMs`, tagged with the
/// epoch current when it was scheduled. Built on Async.Sleep + Cmd.OfAsync
/// (the same primitives every other async Cmd in this module uses) rather
/// than a raw `setTimeout`/`clearInterval` — the epoch tag, not a disposal
/// handle, is what makes a stale timer inert (see FollowEpoch's doc comment).
let private delayedPoll (epoch: int) : Cmd<Msg> =
    Cmd.OfAsync.perform
        (fun () -> async {
            do! Async.Sleep pollIntervalMs
            return epoch
        })
        ()
        Poll_tail

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
        // TailPosition only tracks the newest ("first page") edge — an older
        // page's max position would make no sense as a live-tail cursor, and
        // Follow is unavailable off the first page anyway (see Next_page).
        let tailPosition =
            match before with
            | None ->
                match page.Events with
                | [] -> Some 0L
                | events -> events |> List.map (fun e -> e.GlobalPosition) |> List.max |> Some
            | Some _ -> model.TailPosition
        { model with
            Events = page.Events
            HasMore = page.HasMore
            TotalMatches = page.TotalMatches
            CurrentBefore = before
            CursorStack = cursorStack
            IsLoading = false
            TailPosition = tailPosition
            NewlyArrived = Set.empty },
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
        // Leaving the newest page — prepending live rows onto a page of
        // history the user is actively reading would be a UX trap, so
        // pagination away from the live edge always stops Follow.
        let model = stopFollowing model
        match model.HasMore, List.tryLast model.Events with
        | true, Some lastEvent ->
            let newBefore = Some lastEvent.GlobalPosition
            let newStack = model.CurrentBefore :: model.CursorStack
            model, Cmd.ofMsg (Load_page (newBefore, newStack))
        | _ -> model, Cmd.none

    | Prev_page ->
        let model = stopFollowing model
        match model.CursorStack with
        | prevBefore :: rest -> model, Cmd.ofMsg (Load_page (prevBefore, rest))
        | [] -> model, Cmd.none

    | Toggle_follow ->
        if model.Following then
            stopFollowing model, Cmd.none
        else
            let newEpoch = model.FollowEpoch + 1
            { model with Following = true; FollowEpoch = newEpoch },
            Cmd.ofMsg (Poll_tail newEpoch)

    | Poll_tail epoch ->
        if epoch <> model.FollowEpoch then
            // Stale: Follow was toggled off (or back on) since this poll was
            // scheduled. Drop it — do not fetch, do not reschedule.
            model, Cmd.none
        else
            let after = model.TailPosition |> Option.defaultValue 0L
            let query: EventTailQuery = { Filter = buildFilter model; After = after; Limit = tailLimit }
            model,
            Cmd.OfAsync.either
                api.getEventsAfter
                query
                (fun events -> Tail_loaded (epoch, events))
                (fun _ -> Tail_loaded (epoch, []))

    | Tail_loaded (epoch, events) ->
        if epoch <> model.FollowEpoch then
            // Stale response for an epoch that's no longer current — apply
            // nothing and, crucially, do not reschedule the next poll. This
            // is the guard that actually stops the loop; without it a
            // response arriving just after the toggle went off would
            // reschedule one more tick regardless.
            model, Cmd.none
        else
            let model =
                match events with
                | [] -> model
                | _ ->
                    let existing = model.Events |> List.map (fun e -> e.GlobalPosition) |> Set.ofList
                    let newest = events |> List.map (fun e -> e.GlobalPosition) |> List.max
                    // events arrive ascending; render newest-first, same as the page.
                    let toPrepend =
                        events
                        |> List.filter (fun e -> not (Set.contains e.GlobalPosition existing))
                        |> List.sortByDescending (fun e -> e.GlobalPosition)
                    { model with
                        Events = (toPrepend @ model.Events) |> List.truncate maxFollowedRows
                        TailPosition = Some newest
                        NewlyArrived = toPrepend |> List.map (fun e -> e.GlobalPosition) |> Set.ofList
                        TotalMatches = model.TotalMatches + List.length toPrepend }
            model, delayedPoll epoch

    | Toggle_event_detail pos ->
        let expanded =
            match model.ExpandedEvent with
            | Some p when p = pos -> None
            | _ -> Some pos
        { model with ExpandedEvent = expanded }, Cmd.none
