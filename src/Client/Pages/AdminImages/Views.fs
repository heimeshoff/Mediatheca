module Mediatheca.Client.Pages.AdminImages.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client
open Mediatheca.Client.Components
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminImages.Types

/// Human-readable byte size (B/KB/MB/GB) — same scale as AdminHealth's
/// storage figures.
let private formatBytes (bytes: int64) =
    let units = [| "B"; "KB"; "MB"; "GB"; "TB" |]
    let mutable value = float bytes
    let mutable unitIndex = 0
    while value >= 1024.0 && unitIndex < units.Length - 1 do
        value <- value / 1024.0
        unitIndex <- unitIndex + 1
    if unitIndex = 0 then sprintf "%d B" bytes
    else sprintf "%.1f %s" value units.[unitIndex]

let private statBlock (label: string) (value: string) =
    Html.div [
        prop.children [
            Html.span [ prop.className DesignSystem.eyebrow; prop.text label ]
            Html.div [ prop.className (DesignSystem.dataText + " text-sm"); prop.text value ]
        ]
    ]

let private subfolderTable (subfolders: ImageSubfolderStat list) =
    Html.div [
        prop.className "overflow-x-auto"
        prop.children [
            Html.table [
                prop.className "table table-sm"
                prop.children [
                    Html.thead [
                        Html.tr [
                            Html.th [ prop.text "Subfolder" ]
                            Html.th [ prop.text "Files" ]
                            Html.th [ prop.text "Size" ]
                        ]
                    ]
                    Html.tbody [
                        for s in subfolders ->
                            Html.tr [
                                prop.key s.Subfolder
                                prop.children [
                                    Html.td [ prop.className DesignSystem.dataText; prop.text s.Subfolder ]
                                    Html.td [ prop.className DesignSystem.dataText; prop.text (string s.FileCount) ]
                                    Html.td [ prop.className DesignSystem.dataText; prop.text (formatBytes s.SizeBytes) ]
                                ]
                            ]
                    ]
                ]
            ]
        ]
    ]

let private statsPanel (model: Model) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.h3 [ prop.className DesignSystem.cardTitle; prop.text "Cache footprint" ]
            match model.Stats with
            | None ->
                Html.p [ prop.className DesignSystem.mutedText; prop.text "Loading..." ]
            | Some stats ->
                Html.div [
                    prop.className "grid grid-cols-2 gap-3"
                    prop.children [
                        statBlock "Total size" (formatBytes stats.TotalBytes)
                        statBlock "Total files" (string stats.TotalFileCount)
                    ]
                ]
                subfolderTable stats.Subfolders
        ]
    ]

let private orphanRow (model: Model) (dispatch: Msg -> unit) (o: OrphanImage) =
    Html.tr [
        prop.key o.RelativePath
        prop.children [
            Html.td [
                Daisy.checkbox [
                    checkbox.sm
                    prop.isChecked (Set.contains o.RelativePath model.Selected)
                    prop.onChange (fun (_: bool) -> dispatch (Toggle_selected o.RelativePath))
                ]
            ]
            Html.td [ prop.className DesignSystem.dataText; prop.text o.RelativePath ]
            Html.td [ prop.className DesignSystem.dataText; prop.text o.Subfolder ]
            Html.td [ prop.className DesignSystem.dataText; prop.text (formatBytes o.SizeBytes) ]
        ]
    ]

/// Confirm dialog — counts + total bytes come from the client's held scan
/// (not re-fetched), per the task's "accurate count + bytes before commit"
/// acceptance criterion. The server independently re-derives and re-checks
/// at commit time (ADR-0025); this dialog only reflects what was scanned.
let private confirmDialog (model: Model) (dispatch: Msg -> unit) (orphans: OrphanImage list) =
    match model.PendingIntent with
    | None -> Html.none
    | Some intent ->
        let targets =
            match intent with
            | PurgeAllIntent -> orphans
            | PurgeSelectedIntent -> orphans |> List.filter (fun o -> Set.contains o.RelativePath model.Selected)
        let count = List.length targets
        let totalBytes = targets |> List.sumBy (fun o -> o.SizeBytes)
        ModalPanel.viewWithFooter
            "Confirm purge"
            (fun () -> dispatch Cancel_purge)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text (sprintf "Delete %d file%s, freeing %s. This is a hard delete — there is no trash or undo." count (if count = 1 then "" else "s") (formatBytes totalBytes))
                ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch Cancel_purge); prop.text "Cancel" ]
                Daisy.button.button [ button.error; prop.onClick (fun _ -> dispatch Confirm_purge); prop.text "Delete" ]
            ]

let private orphansPanel (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.h3 [ prop.className DesignSystem.cardTitle; prop.text "Orphaned images" ]
                    Daisy.button.button [
                        button.outline
                        button.sm
                        prop.disabled model.IsLoadingOrphans
                        prop.onClick (fun _ -> dispatch Load)
                        prop.text "Rescan"
                    ]
                ]
            ]
            match model.OrphanScan with
            | None ->
                Html.p [ prop.className DesignSystem.mutedText; prop.text "Loading..." ]
            | Some (OrphanScanBlocked reason) ->
                Html.p [ prop.className "text-sm text-warning"; prop.text reason ]
            | Some (OrphanScanReady ([], _)) ->
                Html.p [ prop.className DesignSystem.mutedText; prop.text "No orphaned images — the cache is clean." ]
            | Some (OrphanScanReady (orphans, totalBytes)) ->
                Html.div [
                    prop.className "flex items-center justify-between gap-3"
                    prop.children [
                        Html.p [
                            prop.className DesignSystem.mutedText
                            prop.text (sprintf "%d orphaned file%s, %s reclaimable" (List.length orphans) (if List.length orphans = 1 then "" else "s") (formatBytes totalBytes))
                        ]
                        Html.div [
                            prop.className "flex gap-2"
                            prop.children [
                                Daisy.button.button [ button.ghost; button.sm; prop.onClick (fun _ -> dispatch Select_all); prop.text "Select all" ]
                                Daisy.button.button [ button.ghost; button.sm; prop.onClick (fun _ -> dispatch Select_none); prop.text "Select none" ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "overflow-x-auto max-h-96 overflow-y-auto"
                    prop.children [
                        Html.table [
                            prop.className "table table-sm"
                            prop.children [
                                Html.thead [
                                    Html.tr [
                                        Html.th []
                                        Html.th [ prop.text "Path" ]
                                        Html.th [ prop.text "Subfolder" ]
                                        Html.th [ prop.text "Size" ]
                                    ]
                                ]
                                Html.tbody [ for o in orphans -> orphanRow model dispatch o ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-center justify-end gap-3"
                    prop.children [
                        match model.LastPurgeResult with
                        | Some (PurgeDone (deleted, freed, skipped)) ->
                            Html.p [
                                prop.className DesignSystem.mutedText
                                prop.text (sprintf "Last purge: deleted %d (%s freed)%s" deleted (formatBytes freed) (if List.isEmpty skipped then "" else sprintf ", skipped %d" (List.length skipped)))
                            ]
                        | Some (PurgeBlocked reason) ->
                            Html.p [ prop.className "text-sm text-warning"; prop.text reason ]
                        | None -> Html.none
                        Daisy.button.button [
                            button.outline
                            prop.disabled (Set.isEmpty model.Selected || model.IsPurging)
                            prop.onClick (fun _ -> dispatch Purge_selected_clicked)
                            prop.text (sprintf "Purge selected (%d)" (Set.count model.Selected))
                        ]
                        Daisy.button.button [
                            button.error
                            prop.disabled model.IsPurging
                            prop.onClick (fun _ -> dispatch Purge_all_clicked)
                            prop.text "Purge all"
                        ]
                    ]
                ]
                confirmDialog model dispatch orphans
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Size/count breakdown of the images/ cache, plus orphan detection and purge (hard delete, filesystem-only — the event store is never touched)."
            ]
            statsPanel model
            orphansPanel model dispatch
        ]
    ]
