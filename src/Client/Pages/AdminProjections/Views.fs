module Mediatheca.Client.Pages.AdminProjections.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminProjections.Types

let private formatUpdatedAt (updatedAt: string option) =
    match updatedAt with
    | Some ts ->
        match System.DateTimeOffset.TryParse(ts) with
        | true, dto -> dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        | false, _ -> ts
    | None -> "never"

let private statBlock (label: string) (value: string) (valueClass: string) =
    Html.div [
        prop.children [
            Html.span [ prop.className DesignSystem.eyebrow; prop.text label ]
            Html.div [ prop.className valueClass; prop.text value ]
        ]
    ]

let private tableCountsRow (counts: ProjectionTableCount list) =
    Html.div [
        prop.className "flex flex-wrap gap-x-4 gap-y-1"
        prop.children [
            for t in counts ->
                Html.span [
                    prop.key t.TableName
                    prop.className DesignSystem.dataText
                    prop.text (sprintf "%s: %d" t.TableName t.RowCount)
                ]
        ]
    ]

/// Continuous progress bar for a rebuild in flight — `progress.Head` is the
/// store's tip when the rebuild started (Projection.RebuildProgress), so the
/// percentage denominator is stable for the whole run.
let private rebuildProgressBar (progress: RebuildProgress) =
    let pct =
        if progress.Head <= 0L then 0
        else int (min 100L (progress.Position * 100L / progress.Head))
    Html.div [
        prop.className "flex-1 space-y-1"
        prop.children [
            Html.div [
                prop.className "flex justify-between"
                prop.children [
                    Html.span [ prop.className DesignSystem.dataText; prop.text (sprintf "%d / %d events" progress.EventsProcessed progress.Head) ]
                    Html.span [ prop.className DesignSystem.dataText; prop.text (sprintf "%d%%" pct) ]
                ]
            ]
            Daisy.progress [
                prop.className "progress-primary w-full"
                prop.value pct
                prop.max 100
            ]
        ]
    ]

let private projectionCard (model: Model) (dispatch: Msg -> unit) (row: ProjectionStatRow) =
    let isRebuilding = row.IsRebuilding || Set.contains row.Name model.RebuildingNames
    Html.div [
        prop.key row.Name
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.h3 [ prop.className DesignSystem.cardTitle; prop.text row.Name ]
                    Daisy.button.button [
                        button.outline
                        button.sm
                        prop.disabled (isRebuilding || model.IsRebuildingAll)
                        prop.onClick (fun _ -> dispatch (Rebuild_clicked row.Name))
                        prop.text (if isRebuilding then "Rebuilding..." else "Rebuild")
                    ]
                ]
            ]

            Html.div [
                prop.className "grid grid-cols-2 sm:grid-cols-4 gap-3"
                prop.children [
                    statBlock "Checkpoint" (string row.CheckpointPosition) (DesignSystem.dataText + " text-sm")
                    statBlock "Lag" (string row.Lag) (DesignSystem.dataText + " text-sm" + (if row.Lag > 0L then " text-warning" else ""))
                    statBlock "Updated" (formatUpdatedAt row.UpdatedAt) (DesignSystem.dataText + " text-sm")
                ]
            ]

            tableCountsRow row.TableCounts

            match model.RebuildProgress |> Map.tryFind row.Name with
            | Some progress ->
                Html.div [
                    prop.className "flex items-center gap-2"
                    prop.children [
                        Daisy.loading [ loading.spinner; loading.xs ]
                        rebuildProgressBar progress
                    ]
                ]
            | None -> Html.none

            match model.RebuildMessages |> Map.tryFind row.Name with
            | Some message -> Html.p [ prop.className "text-xs text-warning"; prop.text message ]
            | None -> Html.none
        ]
    ]

/// Backup section (administration-vrc56, ADR-0029): export the full event
/// log as NDJSON (a plain `<a href>` download — the server sets
/// Content-Disposition, no client state needed), or import an NDJSON export
/// into an empty store. After a successful import, projections are left
/// untouched on purpose (checkpoints stay put, so the store reads as dirty
/// via the existing lag detection above) — the operator runs "Rebuild all"
/// next, reusing the same control rather than growing a second rebuild
/// path. Import into a non-empty store is refused server-side and surfaced
/// here as a visible message, not a raw error.
let private backupSection (model: Model) (dispatch: Msg -> unit) =
    let importInputId = "admin-import-events-input"
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.h3 [ prop.className DesignSystem.cardTitle; prop.text "Backup" ]
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Export the full event log as NDJSON, or import one into a freshly emptied store. Import into a store that already has events is refused."
            ]
            Html.div [
                prop.className "flex items-center gap-3"
                prop.children [
                    Html.a [
                        prop.href "/api/stream/export-events"
                        prop.className "btn btn-outline btn-sm"
                        prop.text "Export events"
                    ]
                    Html.label [
                        prop.htmlFor importInputId
                        prop.className ("btn btn-outline btn-sm" + (if model.IsImporting then " btn-disabled" else ""))
                        prop.text (if model.IsImporting then "Importing..." else "Import events")
                    ]
                    Html.input [
                        prop.id importInputId
                        prop.type' "file"
                        prop.accept ".ndjson,application/x-ndjson,text/plain"
                        prop.className "hidden"
                        prop.disabled model.IsImporting
                        prop.onChange (fun (e: Browser.Types.Event) ->
                            let input: Browser.Types.HTMLInputElement = unbox e.target
                            let files = input.files
                            if files.length > 0 then
                                dispatch (Import_file_selected files.[0])
                                input.value <- "")
                    ]
                    if model.IsImporting then Daisy.loading [ loading.spinner; loading.xs ]
                ]
            ]
            match model.ImportResult with
            | Some outcome ->
                Html.p [
                    prop.className "text-xs text-success"
                    prop.text (sprintf "Imported %d events. Run Rebuild all below to bring projections up to date." outcome.EventsImported)
                ]
            | None -> Html.none
            match model.ImportMessage with
            | Some message -> Html.p [ prop.className "text-xs text-warning"; prop.text message ]
            | None -> Html.none
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            backupSection model dispatch

            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.mutedText
                        prop.text "Checkpoint position, lag versus the store head, and row counts for every registered projection."
                    ]
                    Daisy.button.button [
                        button.primary
                        button.sm
                        prop.disabled (model.IsRebuildingAll || not (Set.isEmpty model.RebuildingNames) || List.isEmpty model.Stats)
                        prop.onClick (fun _ -> dispatch Rebuild_all_clicked)
                        prop.text (if model.IsRebuildingAll then "Rebuilding all..." else "Rebuild all")
                    ]
                ]
            ]

            if model.IsLoading && List.isEmpty model.Stats then
                Html.div [
                    prop.className (DesignSystem.velvetCard + " p-8 text-center")
                    prop.children [ Html.p [ prop.className DesignSystem.mutedText; prop.text "Loading projection stats..." ] ]
                ]
            else
                Html.div [
                    prop.className "flex flex-col gap-3"
                    prop.children [ for row in model.Stats -> projectionCard model dispatch row ]
                ]
        ]
    ]
