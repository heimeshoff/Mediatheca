module Mediatheca.Client.Pages.AdminProjections.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client
open Mediatheca.Client.Components
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

/// Backup section (administration-vrc56, ADR-0029; extended by
/// administration-n8kqw, ADR-0038): export the full event log as NDJSON (a
/// plain `<a href>` download — the server sets Content-Disposition, no
/// client state needed), import an NDJSON export into an empty store, or
/// wipe-and-reimport over a store that already has events (a separate
/// route/control, since the safe Import above always refuses a non-empty
/// store). After a successful import, projections are left untouched on
/// purpose (checkpoints stay put, so the store reads as dirty via the
/// existing lag detection above) — the operator runs "Rebuild all" next,
/// reusing the same control rather than growing a second rebuild path.
let private backupSection (model: Model) (dispatch: Msg -> unit) =
    let importInputId = "admin-import-events-input"
    let wipeImportInputId = "admin-wipe-import-events-input"
    let wipeImportBusy = model.IsWipeImporting || model.WipeImportPreviewLoading
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

            Html.div [ prop.className "divider my-0" ]

            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Wipe & re-import replaces the ENTIRE event log with the uploaded file, after taking a backup — unlike Import above, this works even when the store already has events."
            ]
            Html.div [
                prop.className "flex items-center gap-3"
                prop.children [
                    Html.label [
                        prop.htmlFor wipeImportInputId
                        prop.className ("btn btn-outline btn-error btn-sm" + (if wipeImportBusy then " btn-disabled" else ""))
                        prop.text (
                            if model.IsWipeImporting then "Wiping & importing..."
                            elif model.WipeImportPreviewLoading then "Loading preview..."
                            else "Wipe & re-import")
                    ]
                    Html.input [
                        prop.id wipeImportInputId
                        prop.type' "file"
                        prop.accept ".ndjson,application/x-ndjson,text/plain"
                        prop.className "hidden"
                        prop.disabled wipeImportBusy
                        prop.onChange (fun (e: Browser.Types.Event) ->
                            let input: Browser.Types.HTMLInputElement = unbox e.target
                            let files = input.files
                            if files.length > 0 then
                                dispatch (WipeImport_file_selected files.[0])
                                input.value <- "")
                    ]
                    if wipeImportBusy then Daisy.loading [ loading.spinner; loading.xs ]
                ]
            ]
            match model.WipeImportBackupPath with
            | Some path -> Html.p [ prop.className (DesignSystem.dataText + " text-xs"); prop.text (sprintf "Backup taken: %s" path) ]
            | None -> Html.none
            match model.WipeImportResult with
            | Some (eventsImported, eventsDiscarded) ->
                Html.p [
                    prop.className "text-xs text-success"
                    prop.text (sprintf "Wiped %d event%s, imported %d. Run Rebuild all below to bring projections up to date." eventsDiscarded (if eventsDiscarded = 1 then "" else "s") eventsImported)
                ]
            | None -> Html.none
            match model.WipeImportMessage with
            | Some message -> Html.p [ prop.className "text-xs text-warning"; prop.text message ]
            | None -> Html.none
        ]
    ]

/// Wipe-import confirm dialog (administration-n8kqw, ADR-0038; paper-overlay,
/// ADR-0016, same `ModalPanel` the Surgery tab's confirm dialogs use): shows
/// both the discard-side server stats and the incoming-side client-computed
/// line count, so the operator sees what's about to be thrown away and what
/// it's being replaced with in one place. Cancel is model-only — dispatched
/// straight to `WipeImport_cancel`, no `Cmd.ofEffect`, so "untouched" holds
/// by construction rather than by any rollback. An empty incoming file is
/// allowed to proceed (net effect: wipe to empty) — called out explicitly
/// rather than silently blocked or silently proceeding.
let private wipeImportConfirmDialog (model: Model) (dispatch: Msg -> unit) =
    match model.WipeImportPendingFile, model.WipeImportPreview with
    | Some file, Some preview ->
        ModalPanel.viewWithFooter
            "Confirm Wipe & Import"
            (fun () -> dispatch WipeImport_cancel)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text "This replaces the entire event log. A backup is taken first (VACUUM INTO); the wipe and re-import share one transaction, so a malformed line rolls back everything, leaving the store exactly as it was before."
                ]
                Html.div [
                    prop.className "grid grid-cols-2 gap-3 mt-3"
                    prop.children [
                        Html.div [
                            prop.children [
                                Html.span [ prop.className DesignSystem.eyebrow; prop.text "Currently in store (to be discarded)" ]
                                Html.div [
                                    prop.className (DesignSystem.dataText + " text-sm")
                                    prop.text (sprintf "%d event%s across %d stream%s" preview.EventCount (if preview.EventCount = 1 then "" else "s") preview.DistinctStreamCount (if preview.DistinctStreamCount = 1 then "" else "s"))
                                ]
                            ]
                        ]
                        Html.div [
                            prop.children [
                                Html.span [ prop.className DesignSystem.eyebrow; prop.text "Incoming file" ]
                                Html.div [
                                    prop.className (DesignSystem.dataText + " text-sm")
                                    prop.text (sprintf "%s: %d line%s" file.name model.WipeImportClientLineCount (if model.WipeImportClientLineCount = 1 then "" else "s"))
                                ]
                            ]
                        ]
                    ]
                ]
                if model.WipeImportClientLineCount = 0 then
                    Html.p [
                        prop.className "text-sm text-warning mt-2"
                        prop.text "The incoming file has no events — confirming will wipe the store to empty."
                    ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch WipeImport_cancel); prop.text "Cancel" ]
                Daisy.button.button [ button.error; prop.onClick (fun _ -> dispatch WipeImport_confirm); prop.text "Wipe & import" ]
            ]
    | _ -> Html.none

/// One row-level discrepancy in the drift-check results (administration-btvqa,
/// ADR-0031): table, primary key, kind (only-in-live / only-in-shadow /
/// column-mismatch), and — for a column mismatch — which columns differ.
let private driftDiscrepancyRow (d: DriftDiscrepancy) =
    let kindLabel =
        match d.Kind with
        | "onlyInLive" -> "only in live"
        | "onlyInShadow" -> "only in shadow"
        | "columnMismatch" -> "column mismatch"
        | other -> other
    Html.div [
        prop.key (d.Table + "|" + d.PrimaryKey + "|" + d.Kind)
        prop.className "flex flex-wrap items-baseline gap-x-2 gap-y-1"
        prop.children [
            Html.span [ prop.className (DesignSystem.dataText + " text-sm"); prop.text d.Table ]
            Html.span [ prop.className (DesignSystem.mutedText + " text-sm"); prop.text d.PrimaryKey ]
            Daisy.badge [ badge.warning; badge.sm; prop.text kindLabel ]
            if not (List.isEmpty d.Columns) then
                Html.span [ prop.className (DesignSystem.dataText + " text-sm"); prop.text (String.concat ", " d.Columns) ]
        ]
    ]

let private driftProjectionSection (p: ProjectionDrift) =
    Html.div [
        prop.key p.Name
        prop.className "flex flex-col gap-1"
        prop.children [
            Html.h4 [ prop.className DesignSystem.eyebrow; prop.text p.Name ]
            for d in p.Discrepancies -> driftDiscrepancyRow d
        ]
    ]

/// Shadow-table replay drift detector (administration-btvqa, ADR-0031): "Run
/// check" streams SSE progress from `/api/stream/drift-check` (thin wrapper
/// over `Administration.checkProjectionDrift`, gated by the same not-dirty
/// guard the image-cache orphan scan uses, ADR-0025) then renders per-table
/// discrepancies grouped by projection, or a clean-bill-of-health message.
let private driftCheckSection (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.h3 [ prop.className DesignSystem.cardTitle; prop.text "Drift check" ]
                    Daisy.button.button [
                        button.outline
                        button.sm
                        prop.disabled model.IsDriftChecking
                        prop.onClick (fun _ -> dispatch Drift_check_clicked)
                        prop.text (if model.IsDriftChecking then "Checking..." else "Run check")
                    ]
                ]
            ]
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Replays the full event log into a throwaway shadow copy and compares it row-by-row against the live projection tables — verifies every read model is exactly what the log says, without touching live data."
            ]
            if model.IsDriftChecking then
                Html.div [
                    prop.className "flex items-center gap-2"
                    prop.children [
                        Daisy.loading [ loading.spinner; loading.xs ]
                        Html.span [
                            prop.className (DesignSystem.dataText + " text-sm")
                            prop.text (
                                match model.DriftCheckProgress with
                                | Some name -> sprintf "Replayed %s..." name
                                | None -> "Starting...")
                        ]
                    ]
                ]
            match model.DriftCheckResult with
            | Some result when result.TotalDiscrepancies = 0 ->
                Html.p [
                    prop.className "text-xs text-success"
                    prop.text (sprintf "No discrepancies found across %d projections. Every read model matches the event log." (List.length result.Projections))
                ]
            | Some result ->
                Html.div [
                    prop.className "flex flex-col gap-3"
                    prop.children [
                        Html.p [
                            prop.className "text-xs text-warning"
                            prop.text (sprintf "%d discrepancies found." result.TotalDiscrepancies)
                        ]
                        for p in result.Projections do
                            if not (List.isEmpty p.Discrepancies) then
                                driftProjectionSection p
                    ]
                ]
            | None -> Html.none
            match model.DriftCheckMessage with
            | Some message -> Html.p [ prop.className "text-xs text-warning"; prop.text message ]
            | None -> Html.none
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            backupSection model dispatch

            driftCheckSection model dispatch

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

            wipeImportConfirmDialog model dispatch
        ]
    ]
