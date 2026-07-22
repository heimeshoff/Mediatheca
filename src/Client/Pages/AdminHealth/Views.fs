module Mediatheca.Client.Pages.AdminHealth.Views

open Feliz
open Mediatheca.Client
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminHealth.Types

/// Human-readable byte size (B/KB/MB/GB) — storage figures are otherwise raw
/// int64 byte counts, unreadable at a glance.
let private formatBytes (bytes: int64) =
    let units = [| "B"; "KB"; "MB"; "GB"; "TB" |]
    let mutable value = float bytes
    let mutable unitIndex = 0
    while value >= 1024.0 && unitIndex < units.Length - 1 do
        value <- value / 1024.0
        unitIndex <- unitIndex + 1
    if unitIndex = 0 then $"{bytes} B"
    else sprintf "%.1f %s" value units.[unitIndex]

let private statCard (label: string) (value: string) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-1")
        prop.children [
            Html.span [ prop.className DesignSystem.eyebrow; prop.text label ]
            Html.span [ prop.className "text-2xl font-mono text-base-content"; prop.text value ]
        ]
    ]

/// Horizontal bar row — label, mono count, and a proportional fill bar
/// relative to the largest count in the list it's part of. Used for both the
/// bounded-context breakdown and the top-event-types list.
let private barRow (label: string) (count: int) (maxCount: int) =
    let pct = if maxCount <= 0 then 0.0 else float count / float maxCount * 100.0
    Html.div [
        prop.key label
        prop.className "flex flex-col gap-1"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.span [ prop.className DesignSystem.bodyText; prop.text label ]
                    Html.span [ prop.className DesignSystem.dataText; prop.text (string count) ]
                ]
            ]
            Html.div [
                prop.className "h-1.5 rounded-full bg-base-300/50 overflow-hidden"
                prop.children [
                    Html.div [
                        prop.className "h-full rounded-full bg-primary/60"
                        prop.style [ style.width (length.percent pct) ]
                    ]
                ]
            ]
        ]
    ]

let private sectionCard (title: string) (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.h3 [ prop.className DesignSystem.cardTitle; prop.text title ]
            yield! children
        ]
    ]

/// Mini-bars for the last ~90 days — no charting dependency, plain CSS bars
/// with a native title tooltip per bar (date + count).
let private eventsOverTime (days: DailyEventCount list) =
    let maxCount = days |> List.map (fun d -> d.Count) |> List.fold max 1
    Html.div [
        prop.className "flex items-end gap-[1px] h-20"
        prop.children [
            for day in days ->
                let heightPct = if day.Count = 0 then 2.0 else max 4.0 (float day.Count / float maxCount * 100.0)
                Html.div [
                    prop.key day.Date
                    prop.title $"{day.Date}: {day.Count} event(s)"
                    prop.className (
                        "flex-1 rounded-[1px] min-w-[1px] "
                        + (if day.Count = 0 then "bg-base-content/10" else "bg-primary/60")
                    )
                    prop.style [ style.height (length.percent heightPct) ]
                ]
        ]
    ]

let private topStreamsTable (streams: StreamEventCount list) =
    Html.div [
        prop.className "flex flex-col"
        prop.children [
            for stream in streams ->
                Html.div [
                    prop.key stream.StreamId
                    prop.className "flex items-center justify-between gap-3 py-1.5 border-b border-base-content/5 last:border-b-0"
                    prop.children [
                        Html.span [ prop.className (DesignSystem.dataText + " truncate"); prop.text stream.StreamId ]
                        Html.span [ prop.className DesignSystem.dataText; prop.text (string stream.Count) ]
                    ]
                ]
        ]
    ]

/// A single row of the unknown-event report: type name + count, and the
/// sample event's raw JSON — same rendering as the stream drill-in's raw-JSON
/// toggle (`Pages/StreamDetail/Views.fs`'s `rawJsonBlock`).
let private unknownEventRow (row: UnknownEventTypeRow) =
    Html.div [
        prop.key row.EventType
        prop.className "py-2 border-b border-base-content/5 last:border-b-0"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.span [ prop.className DesignSystem.dataText; prop.text row.EventType ]
                    Html.span [ prop.className DesignSystem.dataText; prop.text (string row.Count) ]
                ]
            ]
            Html.pre [
                prop.className "bg-base-300/50 rounded-lg p-3 mt-1.5 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all"
                prop.text row.SampleData
            ]
        ]
    ]

/// Renders an unknown-event list, or an empty-state message when the check
/// found nothing to flag (mirrors the Event Browser's filter-empty message
/// pattern: a clean check gets its own reassuring message, not silence).
let private unknownEventList (emptyMessage: string) (rows: UnknownEventTypeRow list) =
    if List.isEmpty rows then
        Html.p [ prop.className DesignSystem.mutedText; prop.text emptyMessage ]
    else
        Html.div [
            prop.className "flex flex-col"
            prop.children [ for row in rows -> unknownEventRow row ]
        ]

let private storageCard (storage: StorageStats) =
    sectionCard "Storage" [
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                Html.div [
                    prop.className "flex items-center justify-between"
                    prop.children [
                        Html.span [ prop.className DesignSystem.bodyText; prop.text "mediatheca.db" ]
                        Html.span [ prop.className DesignSystem.dataText; prop.text (formatBytes storage.DbSizeBytes) ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-center justify-between"
                    prop.children [
                        Html.span [ prop.className DesignSystem.bodyText; prop.text "WAL sidecar" ]
                        Html.span [ prop.className DesignSystem.dataText; prop.text (formatBytes storage.WalSizeBytes) ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-center justify-between"
                    prop.children [
                        Html.span [ prop.className DesignSystem.bodyText; prop.text "images/ cache" ]
                        Html.span [
                            prop.className DesignSystem.dataText
                            prop.text (sprintf "%s (%d files)" (formatBytes storage.ImagesSizeBytes) storage.ImagesFileCount)
                        ]
                    ]
                ]
            ]
        ]
    ]

let private loadedView (stats: HealthStats) =
    let maxBcCount = stats.BoundedContextCounts |> List.map (fun c -> c.Count) |> List.fold max 1
    let maxTypeCount = stats.TopEventTypes |> List.map (fun c -> c.Count) |> List.fold max 1
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            Html.div [
                prop.className DesignSystem.statsGrid
                prop.children [
                    statCard "Total events" (string stats.TotalEventCount)
                    statCard "Event types" (string stats.DistinctEventTypeCount)
                    statCard "Database" (formatBytes stats.Storage.DbSizeBytes)
                    statCard "Image cache" (formatBytes stats.Storage.ImagesSizeBytes)
                ]
            ]

            sectionCard "Events over time" [
                Html.span [ prop.className DesignSystem.mutedText; prop.text "Last 90 days" ]
                eventsOverTime stats.DailyCounts
            ]

            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-2 gap-4"
                prop.children [
                    sectionCard "Events by bounded context" [
                        for bc in stats.BoundedContextCounts ->
                            barRow bc.BoundedContext bc.Count maxBcCount
                    ]
                    sectionCard "Top event types" [
                        for et in stats.TopEventTypes ->
                            barRow et.EventType et.Count maxTypeCount
                    ]
                ]
            ]

            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-2 gap-4"
                prop.children [
                    sectionCard "Largest streams" [ topStreamsTable stats.TopStreams ]
                    storageCard stats.Storage
                ]
            ]

            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-2 gap-4"
                prop.children [
                    sectionCard "Unhandled event types" [
                        unknownEventList "No unhandled event types — every stored event type is recognized by its bounded context." stats.UnhandledEventTypes
                    ]
                    sectionCard "Unformattable event types" [
                        unknownEventList "No unformattable event types — every stored event type renders in the timeline." stats.UnformattableEventTypes
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (_dispatch: Msg -> unit) =
    match model.Stats with
    | Some stats -> loadedView stats
    | None ->
        Html.div [
            prop.className (DesignSystem.velvetCard + " p-8 text-center " + DesignSystem.pagePadding)
            prop.children [
                Html.p [
                    prop.className DesignSystem.mutedText
                    prop.text (if model.IsLoading then "Loading health stats..." else "No health stats available.")
                ]
            ]
        ]
