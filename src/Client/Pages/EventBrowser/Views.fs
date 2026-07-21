module Mediatheca.Client.Pages.EventBrowser.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client.Pages.EventBrowser.Types
open Mediatheca.Client
open Mediatheca.Client.Components
open Mediatheca.Client.Router

let private eventRow (event: Mediatheca.Shared.EventDto) (isExpanded: bool) (isNewlyArrived: bool) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (
            "border-b border-base-300/30 last:border-0 "
            + if isNewlyArrived then DesignSystem.animateHighlight else ""
        )
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3 px-4 py-3 cursor-pointer hover:bg-base-300/30 transition-colors"
                prop.onClick (fun _ -> dispatch (Toggle_event_detail event.GlobalPosition))
                prop.children [
                    Html.span [
                        prop.className "text-xs text-base-content/30 font-mono w-10 text-right flex-none"
                        prop.text (string event.GlobalPosition)
                    ]
                    Html.span [
                        // Stream drill-in (administration-v4y9g): click a stream id
                        // to open its full history + current projection state.
                        // stopPropagation so this doesn't also toggle the row's
                        // own raw-JSON expansion.
                        prop.className "text-xs text-primary/70 font-mono truncate w-40 flex-none hover:underline cursor-pointer"
                        prop.title event.StreamId
                        prop.text event.StreamId
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            Route.navigateTo (Stream_detail event.StreamId))
                    ]
                    Html.span [
                        prop.className "text-sm font-medium flex-1 truncate"
                        prop.text (event.EventType.Replace("_", " "))
                    ]
                    Html.span [
                        prop.className "text-xs text-base-content/40 flex-none"
                        prop.text (
                            try
                                let dt = System.DateTimeOffset.Parse(event.Timestamp)
                                dt.LocalDateTime.ToString("MMM d, HH:mm")
                            with _ -> event.Timestamp
                        )
                    ]
                    Html.span [
                        prop.className (
                            "text-base-content/30 transition-transform "
                            + if isExpanded then "rotate-180" else ""
                        )
                        prop.children [
                            Svg.svg [
                                svg.className "w-4 h-4"
                                svg.fill "none"
                                svg.viewBox (0, 0, 24, 24)
                                svg.stroke "currentColor"
                                svg.custom ("strokeWidth", 2)
                                svg.children [
                                    Svg.path [
                                        svg.custom ("strokeLinecap", "round")
                                        svg.custom ("strokeLinejoin", "round")
                                        svg.d "m19.5 8.25-7.5 7.5-7.5-7.5"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            if isExpanded then
                Html.div [
                    prop.className ("px-4 pb-3 " + DesignSystem.animateFadeIn)
                    prop.children [
                        Html.pre [
                            prop.className "bg-base-300/50 rounded-lg p-3 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all"
                            prop.text event.Data
                        ]
                        Html.div [
                            prop.className "flex gap-4 mt-2 text-xs text-base-content/40"
                            prop.children [
                                Html.span [ prop.text $"Stream Position: {event.StreamPosition}" ]
                                Html.span [ prop.text $"Timestamp: {event.Timestamp}" ]
                            ]
                        ]
                    ]
                ]
        ]
    ]

/// Follow toggle (administration-mtf1f). Only meaningful on the newest page —
/// disabled once the user has paged back into history, since Follow would
/// otherwise prepend live rows onto a page they're actively reading (see
/// State.stopFollowing's doc comment).
let private followToggle (model: Model) (dispatch: Msg -> unit) =
    let onFirstPage = model.CurrentBefore = None
    Html.button [
        prop.className (DesignSystem.pill model.Following + " flex items-center gap-2")
        prop.disabled (not onFirstPage)
        prop.title (
            if onFirstPage then "Follow live events matching the active filters"
            else "Follow is only available on the newest page"
        )
        prop.onClick (fun _ -> dispatch Toggle_follow)
        prop.children [
            Html.span [
                prop.className (
                    "inline-block w-2 h-2 rounded-full "
                    + if model.Following then "bg-success animate-pulse" else "bg-base-content/30"
                )
            ]
            Html.span [ prop.text (if model.Following then "Following" else "Follow") ]
        ]
    ]

let private pageSizeOptions = [ 25; 50; 100; 200 ]

let private filterBar (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "flex flex-col gap-3 mb-6"
        prop.children [
            Daisy.input [
                input.bordered
                prop.className "w-full font-mono text-sm"
                prop.placeholder "Search event payloads..."
                prop.value model.Search
                prop.onChange (Search_changed >> dispatch)
            ]
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3"
                prop.children [
                    Daisy.select [
                        prop.className "flex-1"
                        prop.value model.StreamFilter
                        prop.onChange (Stream_filter_changed >> dispatch)
                        prop.children [
                            Html.option [ prop.value ""; prop.text "All streams" ]
                            for stream in model.Streams do
                                Html.option [ prop.value stream; prop.text stream ]
                        ]
                    ]
                    Daisy.select [
                        prop.className "flex-1"
                        prop.value model.EventTypeFilter
                        prop.onChange (Event_type_filter_changed >> dispatch)
                        prop.children [
                            Html.option [ prop.value ""; prop.text "All event types" ]
                            for eventType in model.EventTypes do
                                Html.option [ prop.value eventType; prop.text (eventType.Replace("_", " ")) ]
                        ]
                    ]
                    Daisy.select [
                        prop.className "flex-1"
                        prop.value model.BoundedContextFilter
                        prop.onChange (Bounded_context_filter_changed >> dispatch)
                        prop.children [
                            Html.option [ prop.value ""; prop.text "All bounded contexts" ]
                            for bc in model.BoundedContexts do
                                Html.option [ prop.value bc; prop.text bc ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3 items-stretch sm:items-center"
                prop.children [
                    Html.label [
                        prop.className "flex items-center gap-2 text-xs text-base-content/50 font-mono flex-1"
                        prop.children [
                            Html.span [ prop.text "From" ]
                            Daisy.input [
                                input.bordered
                                input.sm
                                prop.type' "date"
                                prop.className "flex-1"
                                prop.value model.TimestampFrom
                                prop.onChange (Timestamp_from_changed >> dispatch)
                            ]
                        ]
                    ]
                    Html.label [
                        prop.className "flex items-center gap-2 text-xs text-base-content/50 font-mono flex-1"
                        prop.children [
                            Html.span [ prop.text "To" ]
                            Daisy.input [
                                input.bordered
                                input.sm
                                prop.type' "date"
                                prop.className "flex-1"
                                prop.value model.TimestampTo
                                prop.onChange (Timestamp_to_changed >> dispatch)
                            ]
                        ]
                    ]
                    Daisy.select [
                        select.sm
                        prop.value (string model.PageSize)
                        prop.onChange (fun (v: string) -> dispatch (Page_size_changed (int v)))
                        prop.children [
                            for size in pageSizeOptions do
                                Html.option [ prop.value (string size); prop.text $"{size} / page" ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private paginationBar (model: Model) (dispatch: Msg -> unit) =
    let firstShown =
        if model.TotalMatches = 0 then 0
        else (model.CursorStack |> List.length) * model.PageSize + 1
    let lastShown = firstShown + (List.length model.Events) - 1
    Html.div [
        prop.className "flex items-center justify-between mt-4 text-sm text-base-content/40 font-mono"
        prop.children [
            Daisy.button.button [
                button.outline
                button.sm
                prop.disabled (List.isEmpty model.CursorStack || model.IsLoading)
                prop.onClick (fun _ -> dispatch Prev_page)
                prop.text "Prev"
            ]
            Html.span [
                prop.text (
                    if model.TotalMatches = 0 then "No matches"
                    else $"Showing {firstShown}-{lastShown} of {model.TotalMatches}"
                )
            ]
            Daisy.button.button [
                button.outline
                button.sm
                prop.disabled (not model.HasMore || model.IsLoading)
                prop.onClick (fun _ -> dispatch Next_page)
                prop.text "Next"
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "Event Store"
                    ]
                    followToggle model dispatch
                ]
            ]

            filterBar model dispatch

            if model.IsLoading then
                Html.div [
                    prop.className "flex justify-center py-12"
                    prop.children [
                        Daisy.loading [ loading.spinner; loading.lg ]
                    ]
                ]
            else if List.isEmpty model.Events then
                Html.div [
                    prop.className "text-center py-20 text-base-content/30"
                    prop.children [
                        Html.p [ prop.className "font-medium"; prop.text "No events found." ]
                    ]
                ]
            else
                Daisy.card [
                    prop.className "bg-base-100 shadow-md overflow-hidden"
                    prop.children [
                        // Header
                        Html.div [
                            prop.className "flex items-center gap-3 px-4 py-2 bg-base-200/50 text-xs text-base-content/50 font-medium uppercase tracking-wider"
                            prop.children [
                                Html.span [ prop.className "w-10 text-right flex-none"; prop.text "#" ]
                                Html.span [ prop.className "w-40 flex-none"; prop.text "Stream" ]
                                Html.span [ prop.className "flex-1"; prop.text "Event Type" ]
                                Html.span [ prop.className "flex-none"; prop.text "Time" ]
                                Html.span [ prop.className "w-4" ]
                            ]
                        ]
                        for event in model.Events do
                            let isExpanded =
                                match model.ExpandedEvent with
                                | Some pos -> pos = event.GlobalPosition
                                | None -> false
                            let isNewlyArrived = Set.contains event.GlobalPosition model.NewlyArrived
                            eventRow event isExpanded isNewlyArrived dispatch
                    ]
                ]

                paginationBar model dispatch
        ]
    ]
