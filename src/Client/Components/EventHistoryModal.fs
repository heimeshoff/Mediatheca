module Mediatheca.Client.Components.EventHistoryModal

open Feliz
open Fable.Remoting.Client
open Mediatheca.Shared
open Mediatheca.Client

/// Icons for different event categories
let private eventIcon (label: string) =
    let iconClass = "w-4 h-4"
    // Determine icon based on label keywords
    if label.Contains("Added to library") || label.Contains("created") || label.Contains("Friend added") then
        Html.svg [
            prop.className (iconClass + " text-success")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M10 18a8 8 0 100-16 8 8 0 000 16zm1-11a1 1 0 10-2 0v2H7a1 1 0 100 2h2v2a1 1 0 102 0v-2h2a1 1 0 100-2h-2V7z"
                ]
            ]
        ]
    elif label.Contains("Removed") || label.Contains("removed") || label.Contains("cleared") then
        Html.svg [
            prop.className (iconClass + " text-error/70")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M10 18a8 8 0 100-16 8 8 0 000 16zM7 9a1 1 0 000 2h6a1 1 0 100-2H7z"
                ]
            ]
        ]
    elif label.Contains("rating") || label.Contains("Rating") then
        Html.svg [
            prop.className (iconClass + " text-warning")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"
                ]
            ]
        ]
    elif label.Contains("Status") || label.Contains("status") then
        Html.svg [
            prop.className (iconClass + " text-info")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M10 2a8 8 0 100 16 8 8 0 000-16zm1 4a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z"
                ]
            ]
        ]
    elif label.Contains("watched") || label.Contains("Watched") || label.Contains("Watch") || label.Contains("session") then
        Html.svg [
            prop.className (iconClass + " text-primary")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M10 12a2 2 0 100-4 2 2 0 000 4z"
                ]
                Html.path [
                    prop.custom ("fillRule", "evenodd")
                    prop.d "M.458 10C1.732 5.943 5.522 3 10 3s8.268 2.943 9.542 7c-1.274 4.057-5.064 7-9.542 7S1.732 14.057.458 10zM14 10a4 4 0 11-8 0 4 4 0 018 0z"
                    prop.custom ("clipRule", "evenodd")
                ]
            ]
        ]
    elif label.Contains("Focus") then
        Html.svg [
            prop.className (iconClass + " text-accent")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M10 2a8 8 0 100 16 8 8 0 000-16zm0 3a5 5 0 100 10 5 5 0 000-10zm0 2a3 3 0 100 6 3 3 0 000-6z"
                ]
            ]
        ]
    elif label.Contains("Recommendation") || label.Contains("Friend") || label.Contains("friend") then
        Html.svg [
            prop.className (iconClass + " text-secondary")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.d "M9 6a3 3 0 11-6 0 3 3 0 016 0zM17 6a3 3 0 11-6 0 3 3 0 016 0zM12.93 17c.046-.327.07-.66.07-1a6.97 6.97 0 00-1.5-4.33A5 5 0 0119 16v1h-6.07zM6 11a5 5 0 015 5v1H1v-1a5 5 0 015-5z"
                ]
            ]
        ]
    elif label.Contains("Content block") || label.Contains("content block") then
        Html.svg [
            prop.className (iconClass + " text-base-content/50")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.custom ("fillRule", "evenodd")
                    prop.d "M4 4a2 2 0 012-2h4.586A2 2 0 0112 2.586L15.414 6A2 2 0 0116 7.414V16a2 2 0 01-2 2H6a2 2 0 01-2-2V4z"
                    prop.custom ("clipRule", "evenodd")
                ]
            ]
        ]
    else
        // Generic event icon
        Html.svg [
            prop.className (iconClass + " text-base-content/40")
            prop.custom ("viewBox", "0 0 20 20")
            prop.custom ("fill", "currentColor")
            prop.children [
                Html.path [
                    prop.custom ("fillRule", "evenodd")
                    prop.d "M10 18a8 8 0 100-16 8 8 0 000 16zm1-12a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z"
                    prop.custom ("clipRule", "evenodd")
                ]
            ]
        ]

/// Group events by date (extract date from timestamp)
let private groupByDate (entries: EventHistoryEntry list) =
    entries
    |> List.groupBy (fun e ->
        // Timestamp format: "yyyy-MM-dd HH:mm"
        if e.Timestamp.Length >= 10 then e.Timestamp.Substring(0, 10)
        else e.Timestamp)

/// Extract time from timestamp
let private timeFromTimestamp (ts: string) =
    if ts.Length >= 16 then ts.Substring(11, 5)
    else ts

let private api : IMediathecaApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IMediathecaApi>

/// Event History Modal component.
/// streamPrefix: e.g. "Movie-inception-2010", "Game-factorio-2020", "Friend-john"
[<ReactComponent>]
let view (streamPrefix: string) (onClose: unit -> unit) =
    let events, setEvents = React.useState<EventHistoryEntry list option>(None)
    let isLoading, setIsLoading = React.useState true

    React.useEffect((fun () ->
        async {
            try
                let! result = api.getStreamEvents streamPrefix
                setEvents (Some result)
            with _ ->
                setEvents (Some [])
            setIsLoading false
        } |> Async.StartImmediate
    ), [| streamPrefix :> obj |])

    Html.div [
        prop.className DesignSystem.modalContainer
        prop.children [
            // Backdrop
            Html.div [
                prop.className DesignSystem.modalBackdrop
                prop.onClick (fun _ -> onClose ())
            ]
            // Modal panel
            Html.div [
                prop.className ("relative w-full max-w-lg mx-4 max-h-[75vh] flex flex-col " + DesignSystem.modalPanel)
                prop.children [
                    // Header
                    Html.div [
                        prop.className "p-5 pb-3"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between"
                                prop.children [
                                    Html.div [
                                        prop.className "flex items-center gap-2.5"
                                        prop.children [
                                            // Timeline icon
                                            Html.svg [
                                                prop.className "w-5 h-5 text-primary"
                                                prop.custom ("viewBox", "0 0 20 20")
                                                prop.custom ("fill", "currentColor")
                                                prop.children [
                                                    Html.path [
                                                        prop.custom ("fillRule", "evenodd")
                                                        prop.d "M10 18a8 8 0 100-16 8 8 0 000 16zm1-12a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z"
                                                        prop.custom ("clipRule", "evenodd")
                                                    ]
                                                ]
                                            ]
                                            Html.h3 [
                                                prop.className "font-bold text-lg font-display"
                                                prop.text "Event Log"
                                            ]
                                        ]
                                    ]
                                    Html.button [
                                        prop.className "w-8 h-8 flex items-center justify-center rounded-full hover:bg-base-content/10 transition-colors text-base-content/50 hover:text-base-content cursor-pointer"
                                        prop.onClick (fun _ -> onClose ())
                                        prop.children [
                                            Html.svg [
                                                prop.className "w-5 h-5"
                                                prop.custom ("viewBox", "0 0 20 20")
                                                prop.custom ("fill", "currentColor")
                                                prop.children [
                                                    Html.path [
                                                        prop.custom ("fillRule", "evenodd")
                                                        prop.d "M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
                                                        prop.custom ("clipRule", "evenodd")
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    // Content
                    Html.div [
                        prop.className "flex-1 overflow-y-auto px-5 pb-5"
                        prop.children [
                            if isLoading then
                                Html.div [
                                    prop.className "flex items-center justify-center py-12"
                                    prop.children [
                                        Html.span [
                                            prop.className "loading loading-spinner loading-md text-primary"
                                        ]
                                    ]
                                ]
                            else
                                match events with
                                | Some [] | None ->
                                    Html.div [
                                        prop.className "text-center py-12 text-base-content/50"
                                        prop.children [
                                            Html.p [ prop.text "No events found." ]
                                        ]
                                    ]
                                | Some eventList ->
                                    let groups = groupByDate eventList
                                    for (date, dayEvents) in groups do
                                        Html.div [
                                            prop.className "mb-5 last:mb-0"
                                            prop.children [
                                                // Date header
                                                Html.div [
                                                    prop.className "flex items-center gap-2 mb-3"
                                                    prop.children [
                                                        Html.div [
                                                            prop.className "h-px flex-1 bg-base-content/10"
                                                        ]
                                                        Html.span [
                                                            prop.className "text-xs font-medium text-base-content/40 uppercase tracking-wider px-2"
                                                            prop.text date
                                                        ]
                                                        Html.div [
                                                            prop.className "h-px flex-1 bg-base-content/10"
                                                        ]
                                                    ]
                                                ]
                                                // Events for this date
                                                Html.div [
                                                    prop.className "space-y-1.5"
                                                    prop.children [
                                                        for entry in dayEvents do
                                                            Html.div [
                                                                prop.className "flex items-start gap-3 px-2 py-2 rounded-lg hover:bg-base-content/5 transition-colors"
                                                                prop.children [
                                                                    // Icon
                                                                    Html.div [
                                                                        prop.className "mt-0.5 flex-shrink-0"
                                                                        prop.children [ eventIcon entry.Label ]
                                                                    ]
                                                                    // Content
                                                                    Html.div [
                                                                        prop.className "flex-1 min-w-0"
                                                                        prop.children [
                                                                            Html.div [
                                                                                prop.className "flex items-baseline gap-2"
                                                                                prop.children [
                                                                                    Html.span [
                                                                                        prop.className "text-sm font-medium text-base-content"
                                                                                        prop.text entry.Label
                                                                                    ]
                                                                                    Html.span [
                                                                                        prop.className "text-xs text-base-content/40 flex-shrink-0"
                                                                                        prop.text (timeFromTimestamp entry.Timestamp)
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                            if not (List.isEmpty entry.Details) then
                                                                                Html.div [
                                                                                    prop.className "mt-0.5"
                                                                                    prop.children [
                                                                                        for detail in entry.Details do
                                                                                            Html.p [
                                                                                                prop.className "text-xs text-base-content/50"
                                                                                                prop.text detail
                                                                                            ]
                                                                                    ]
                                                                                ]
                                                                        ]
                                                                    ]
                                                                ]
                                                            ]
                                                    ]
                                                ]
                                            ]
                                        ]
                        ]
                    ]
                ]
            ]
        ]
    ]
