module Mediatheca.Client.Pages.StreamDetail.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Shared
open Mediatheca.Client
open Mediatheca.Client.Router
open Mediatheca.Client.Pages.StreamDetail.Types

/// Maps a projection row's (route segment, slug) DetailLink to a router page,
/// for the small handful of media-detail routes the projection panel can
/// link to. Unrecognized segments render no link rather than a broken one.
let private pageForDetailLink (segment: string, slug: string) : Page option =
    match segment with
    | "movies" -> Some (Movie_detail slug)
    | "series" -> Some (Series_detail slug)
    | "games" -> Some (Game_detail slug)
    | "friends" -> Some (Friend_detail slug)
    | "catalogs" -> Some (Catalog_detail slug)
    | _ -> None

let private navLink (page: Page) (label: string) (className: string) =
    Html.a [
        prop.className className
        prop.href (Route.toUrl page)
        prop.onClick (fun e ->
            e.preventDefault ()
            Route.navigateTo page)
        prop.text label
    ]

let private header (streamId: string) =
    Html.div [
        prop.className "mb-6"
        prop.children [
            navLink (Admin AdminEvents) "← Back to Event Store" (DesignSystem.mutedText + " hover:text-base-content transition-colors")
            Html.h1 [
                prop.className "text-2xl font-bold font-display text-gradient-primary mt-2 break-all"
                prop.text streamId
            ]
        ]
    ]

let private crossLinkPill (link: StreamCrossLink) =
    Html.button [
        prop.className "inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-xs font-mono bg-primary/10 text-primary hover:bg-primary/20 transition-colors cursor-pointer"
        prop.onClick (fun _ -> Route.navigateTo (Stream_detail link.TargetStreamId))
        prop.children [
            Html.span [ prop.className "text-primary/50"; prop.text (link.Kind + ":") ]
            Html.span [ prop.text link.TargetStreamId ]
        ]
    ]

let private rawJsonBlock (entry: StreamTimelineEntry) =
    Html.div [
        prop.className ("px-4 pb-3 " + DesignSystem.animateFadeIn)
        prop.children [
            Html.pre [
                prop.className "bg-base-300/50 rounded-lg p-3 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all"
                prop.text entry.Data
            ]
            if entry.Metadata <> "{}" && entry.Metadata <> "" then
                Html.pre [
                    prop.className "bg-base-300/30 rounded-lg p-3 mt-2 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all"
                    prop.text entry.Metadata
                ]
            Html.div [
                prop.className "flex gap-4 mt-2 text-xs text-base-content/40 font-mono"
                prop.children [
                    Html.span [ prop.text $"Global Position: {entry.GlobalPosition}" ]
                    Html.span [ prop.text $"Stream Position: {entry.StreamPosition}" ]
                    Html.span [ prop.text $"Timestamp: {entry.Timestamp}" ]
                ]
            ]
        ]
    ]

let private timelineEntry (entry: StreamTimelineEntry) (isExpanded: bool) (dispatch: Msg -> unit) =
    let isUnformatted = entry.FormattedLabel.IsNone
    Html.div [
        prop.className "border-b border-base-300/30 last:border-0"
        prop.children [
            Html.div [
                prop.className "flex items-start gap-3 px-4 py-3 cursor-pointer hover:bg-base-300/30 transition-colors"
                prop.onClick (fun _ -> dispatch (Toggle_raw entry.GlobalPosition))
                prop.children [
                    Html.span [
                        prop.className "text-xs text-base-content/30 font-mono w-10 text-right flex-none pt-0.5"
                        prop.text (string entry.GlobalPosition)
                    ]
                    Html.div [
                        prop.className "flex-1 min-w-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center gap-2 flex-wrap"
                                prop.children [
                                    Html.span [
                                        prop.className "text-sm font-medium"
                                        prop.text (
                                            entry.FormattedLabel
                                            |> Option.defaultValue (entry.EventType.Replace("_", " ")))
                                    ]
                                    if isUnformatted then
                                        Html.span [
                                            prop.className "text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded bg-base-300/60 text-base-content/40 font-mono"
                                            prop.text "unformatted"
                                        ]
                                    Html.span [
                                        prop.className "text-xs text-base-content/40 font-mono"
                                        prop.text (
                                            try
                                                let dt = System.DateTimeOffset.Parse(entry.Timestamp)
                                                dt.LocalDateTime.ToString("MMM d, HH:mm")
                                            with _ -> entry.Timestamp
                                        )
                                    ]
                                ]
                            ]
                            if not (List.isEmpty entry.FormattedDetails) then
                                Html.div [
                                    prop.className "mt-0.5 flex flex-col gap-0.5"
                                    prop.children [
                                        for detail in entry.FormattedDetails do
                                            Html.p [ prop.className DesignSystem.mutedText; prop.text detail ]
                                    ]
                                ]
                            if not (List.isEmpty entry.CrossLinks) then
                                Html.div [
                                    prop.className "mt-1.5 flex gap-1.5 flex-wrap"
                                    prop.onClick (fun e -> e.stopPropagation ())
                                    prop.children [ for link in entry.CrossLinks do crossLinkPill link ]
                                ]
                        ]
                    ]
                ]
            ]
            // Unformatted events have no formatted view to toggle away from —
            // show the raw payload directly, marked but not alarming.
            if isUnformatted || isExpanded then
                rawJsonBlock entry
        ]
    ]

let private projectionCard (row: ProjectionStateRow) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4")
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-3"
                prop.children [
                    Html.span [ prop.className DesignSystem.cardTitle; prop.text row.Kind ]
                    match row.DetailLink |> Option.bind pageForDetailLink with
                    | Some page -> navLink page "View in library →" (DesignSystem.mutedText + " hover:text-primary transition-colors")
                    | None -> Html.none
                ]
            ]
            Html.div [
                prop.className "flex flex-col gap-1.5"
                prop.children [
                    for (label, value) in row.Fields do
                        Html.div [
                            prop.className "flex items-baseline justify-between gap-4 text-sm"
                            prop.children [
                                Html.span [ prop.className DesignSystem.mutedText; prop.text label ]
                                Html.span [ prop.className "font-mono text-right break-all"; prop.text value ]
                            ]
                        ]
                ]
            ]
        ]
    ]

let private projectionPanel (rows: ProjectionStateRow list) =
    if List.isEmpty rows then Html.none
    else
        Html.div [
            prop.className "mb-6"
            prop.children [
                Html.h2 [ prop.className (DesignSystem.eyebrow + " mb-2"); prop.text "Current State" ]
                Html.div [
                    prop.className "flex flex-col gap-3"
                    prop.children [ for row in rows do projectionCard row ]
                ]
            ]
        ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " " + DesignSystem.animateFadeIn)
        prop.children [
            header model.StreamId

            if model.IsLoading then
                Html.div [
                    prop.className "flex justify-center py-12"
                    prop.children [ Daisy.loading [ loading.spinner; loading.lg ] ]
                ]
            else
                match model.Error with
                | Some err ->
                    Html.div [
                        prop.className "text-center py-20 text-error/70"
                        prop.children [ Html.p [ prop.text $"Failed to load stream: {err}" ] ]
                    ]
                | None ->
                    match model.Detail with
                    | None -> Html.none
                    | Some detail ->
                        projectionPanel detail.ProjectionRows

                        Html.h2 [ prop.className (DesignSystem.eyebrow + " mb-2"); prop.text "Timeline" ]
                        if List.isEmpty detail.Entries then
                            Html.div [
                                prop.className "text-center py-20 text-base-content/30"
                                prop.children [
                                    Html.p [ prop.className "font-medium"; prop.text "No events found for this stream." ]
                                ]
                            ]
                        else
                            Daisy.card [
                                prop.className "bg-base-100 shadow-md overflow-hidden"
                                prop.children [
                                    for entry in detail.Entries do
                                        let isExpanded =
                                            match model.ExpandedEntry with
                                            | Some pos -> pos = entry.GlobalPosition
                                            | None -> false
                                        timelineEntry entry isExpanded dispatch
                                ]
                            ]
        ]
    ]
