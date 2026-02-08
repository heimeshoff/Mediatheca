module Mediatheca.Client.Pages.EventBrowser.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client.Pages.EventBrowser.Types
open Mediatheca.Client.Components

let private eventRow (event: Mediatheca.Shared.EventDto) (isExpanded: bool) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "border-b border-base-300/30 last:border-0"
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
                        prop.className "text-xs text-primary/70 font-mono truncate w-40 flex-none"
                        prop.text event.StreamId
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
                    prop.className "px-4 pb-3 animate-fade-in"
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

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "p-4 lg:p-6 animate-fade-in"
        prop.children [
            Html.h1 [
                prop.className "text-2xl font-bold font-display text-gradient-primary mb-6"
                prop.text "Event Store"
            ]

            // Filters
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3 mb-6"
                prop.children [
                    Daisy.select [
                        prop.className "flex-1"
                        prop.value model.StreamFilter
                        prop.onChange (Stream_filter_changed >> dispatch)
                        prop.children [
                            Html.option [
                                prop.value ""
                                prop.text "All streams"
                            ]
                            for stream in model.Streams do
                                Html.option [
                                    prop.value stream
                                    prop.text stream
                                ]
                        ]
                    ]
                    Daisy.select [
                        prop.className "flex-1"
                        prop.value model.EventTypeFilter
                        prop.onChange (Event_type_filter_changed >> dispatch)
                        prop.children [
                            Html.option [
                                prop.value ""
                                prop.text "All event types"
                            ]
                            for eventType in model.EventTypes do
                                Html.option [
                                    prop.value eventType
                                    prop.text (eventType.Replace("_", " "))
                                ]
                        ]
                    ]
                ]
            ]

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
                            eventRow event isExpanded dispatch
                    ]
                ]

                Html.div [
                    prop.className "mt-4 text-center text-sm text-base-content/40"
                    prop.text $"Showing {List.length model.Events} events"
                ]
        ]
    ]
