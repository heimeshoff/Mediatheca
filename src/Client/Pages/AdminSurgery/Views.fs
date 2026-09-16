module Mediatheca.Client.Pages.AdminSurgery.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client
open Mediatheca.Client.Components
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminSurgery.Types

/// Human-readable byte size — same scale as AdminImages'/AdminHealth's.
let private formatBytes (bytes: int64) =
    let units = [| "B"; "KB"; "MB"; "GB"; "TB" |]
    let mutable value = float bytes
    let mutable unitIndex = 0
    while value >= 1024.0 && unitIndex < units.Length - 1 do
        value <- value / 1024.0
        unitIndex <- unitIndex + 1
    if unitIndex = 0 then sprintf "%d B" bytes
    else sprintf "%.1f %s" value units.[unitIndex]

let private resultBanner (result: SurgeryResult option) =
    match result with
    | None -> Html.none
    | Some (BackupFailed reason) ->
        Html.p [ prop.className "text-sm text-error"; prop.text (sprintf "Backup failed — no row was touched: %s" reason) ]
    | Some (Applied (backupPath, affectedRows)) ->
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text (sprintf "Applied — %d row%s affected. Backup: %s" affectedRows (if affectedRows = 1 then "" else "s") backupPath)
        ]

let private sectionCard (title: string) (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children (Html.h3 [ prop.className DesignSystem.cardTitle; prop.text title ] :: children)
    ]

let private globalPositionInput (label: string) (value: string) (onChange: string -> unit) (onLoad: unit -> unit) (loading: bool) =
    Html.div [
        prop.className "flex gap-2 items-end"
        prop.children [
            Html.label [
                prop.className "flex-1 flex flex-col gap-1"
                prop.children [
                    Html.span [ prop.className DesignSystem.eyebrow; prop.text label ]
                    Daisy.input [
                        input.sm
                        prop.className "font-mono w-full"
                        prop.placeholder "global_position"
                        prop.value value
                        prop.onChange onChange
                    ]
                ]
            ]
            Daisy.button.button [
                button.outline
                button.sm
                prop.disabled loading
                prop.onClick (fun _ -> onLoad ())
                prop.text (if loading then "Loading..." else "Load")
            ]
        ]
    ]

// ── Edit ──

let private editPanel (model: Model) (dispatch: Msg -> unit) =
    sectionCard "Edit event" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Edit one event's data/metadata by exact global position. Re-syncs full-text search after the change."
        ]
        globalPositionInput "Global position" model.EditGlobalPositionInput (Edit_global_position_changed >> dispatch) (fun () -> dispatch Load_edit_preview) model.EditLoading
        match model.EditError with
        | Some err -> Html.p [ prop.className "text-sm text-error"; prop.text err ]
        | None -> Html.none
        match model.EditPreview with
        | None -> Html.none
        | Some row ->
            Html.div [
                prop.className "flex flex-col gap-2"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.dataText
                        prop.text (sprintf "%s @ %s (stream position %d)" row.StreamId row.EventType row.StreamPosition)
                    ]
                    Html.label [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            Html.span [ prop.className DesignSystem.eyebrow; prop.text "Data" ]
                            Daisy.textarea [
                                prop.className "font-mono text-xs w-full"
                                prop.rows 6
                                prop.value model.EditDataInput
                                prop.onChange (Edit_data_changed >> dispatch)
                            ]
                        ]
                    ]
                    Html.label [
                        prop.className "flex flex-col gap-1"
                        prop.children [
                            Html.span [ prop.className DesignSystem.eyebrow; prop.text "Metadata" ]
                            Daisy.textarea [
                                prop.className "font-mono text-xs w-full"
                                prop.rows 3
                                prop.value model.EditMetadataInput
                                prop.onChange (Edit_metadata_changed >> dispatch)
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex justify-end"
                        prop.children [
                            Daisy.button.button [
                                button.primary
                                button.sm
                                prop.disabled model.IsCommitting
                                prop.onClick (fun _ -> dispatch Save_edit_clicked)
                                prop.text "Save edit..."
                            ]
                        ]
                    ]
                ]
            ]
    ]

// ── Delete ──

let private deletePanel (model: Model) (dispatch: Msg -> unit) =
    sectionCard "Delete event" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Deletes one event by exact global position. Leaves a permanent gap in the stream's position sequence — no renumbering."
        ]
        globalPositionInput "Global position" model.DeleteGlobalPositionInput (Delete_global_position_changed >> dispatch) (fun () -> dispatch Load_delete_preview) model.DeleteLoading
        match model.DeleteError with
        | Some err -> Html.p [ prop.className "text-sm text-error"; prop.text err ]
        | None -> Html.none
        match model.DeletePreview with
        | None -> Html.none
        | Some preview ->
            Html.div [
                prop.className "flex flex-col gap-2"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.dataText
                        prop.text (sprintf "%s @ %s (stream position %d)" preview.Event.StreamId preview.Event.EventType preview.Event.StreamPosition)
                    ]
                    Html.pre [
                        prop.className "bg-base-300/50 rounded-lg p-3 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all"
                        prop.text preview.Event.Data
                    ]
                    Html.p [
                        prop.className "text-sm text-warning"
                        prop.text (sprintf "The stream is currently at position %d — deleting this event leaves a permanent gap in %s's position sequence." preview.StreamCurrentPosition preview.Event.StreamId)
                    ]
                    Html.div [
                        prop.className "flex justify-end"
                        prop.children [
                            Daisy.button.button [
                                button.error
                                button.sm
                                prop.disabled model.IsCommitting
                                prop.onClick (fun _ -> dispatch Delete_clicked)
                                prop.text "Delete..."
                            ]
                        ]
                    ]
                ]
            ]
    ]

// ── Rename ──

let private renamePanel (model: Model) (dispatch: Msg -> unit) =
    sectionCard "Rename event type" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Renames an event type store-wide — the schema-migration verb for a code-side DU rename that left old-named rows stranded."
        ]
        Html.div [
            prop.className "flex flex-col sm:flex-row gap-2 items-end"
            prop.children [
                Html.label [
                    prop.className "flex-1 flex flex-col gap-1"
                    prop.children [
                        Html.span [ prop.className DesignSystem.eyebrow; prop.text "Old event type" ]
                        Daisy.input [
                            input.sm
                            prop.className "font-mono w-full"
                            prop.value model.RenameOldTypeInput
                            prop.onChange (Rename_old_type_changed >> dispatch)
                        ]
                    ]
                ]
                Html.label [
                    prop.className "flex-1 flex flex-col gap-1"
                    prop.children [
                        Html.span [ prop.className DesignSystem.eyebrow; prop.text "New event type" ]
                        Daisy.input [
                            input.sm
                            prop.className "font-mono w-full"
                            prop.value model.RenameNewTypeInput
                            prop.onChange (Rename_new_type_changed >> dispatch)
                        ]
                    ]
                ]
                Daisy.button.button [
                    button.outline
                    button.sm
                    prop.disabled model.RenameLoading
                    prop.onClick (fun _ -> dispatch Load_rename_preview)
                    prop.text (if model.RenameLoading then "Loading..." else "Preview")
                ]
            ]
        ]
        match model.RenameError with
        | Some err -> Html.p [ prop.className "text-sm text-error"; prop.text err ]
        | None -> Html.none
        match model.RenamePreview with
        | None -> Html.none
        | Some preview ->
            Html.div [
                prop.className "flex flex-col gap-2"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.mutedText
                        prop.text (sprintf "%d row%s at '%s'" preview.Count (if preview.Count = 1 then "" else "s") model.RenameOldTypeInput)
                    ]
                    if not (List.isEmpty preview.Sample) then
                        Html.div [
                            prop.className "overflow-x-auto max-h-48 overflow-y-auto"
                            prop.children [
                                Html.table [
                                    prop.className "table table-sm"
                                    prop.children [
                                        Html.thead [ Html.tr [ Html.th [ prop.text "Global pos" ]; Html.th [ prop.text "Stream" ] ] ]
                                        Html.tbody [
                                            for row in preview.Sample ->
                                                Html.tr [
                                                    prop.key (string row.GlobalPosition)
                                                    prop.children [
                                                        Html.td [ prop.className DesignSystem.dataText; prop.text (string row.GlobalPosition) ]
                                                        Html.td [ prop.className DesignSystem.dataText; prop.text row.StreamId ]
                                                    ]
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    Html.div [
                        prop.className "flex justify-end"
                        prop.children [
                            Daisy.button.button [
                                button.error
                                button.sm
                                prop.disabled model.IsCommitting
                                prop.onClick (fun _ -> dispatch Rename_clicked)
                                prop.text "Rename..."
                            ]
                        ]
                    ]
                ]
            ]
    ]

// ── Notes migration (curation-j4qqt, ADR-0080 §10-11) ──
// Two separate gates, each preview -> confirm, sharing one owner-resolution
// report. Gate 1 is additive/idempotent; Gate 2 is destructive and reuses
// this tab's shared PendingAction/confirm-dialog/Mutation_completed
// plumbing (it returns the same SurgeryResult edit/delete/rename do).

let private ownerRefRow (label: string) (slug: string) =
    Html.div [
        prop.key (label + "/" + slug)
        prop.className "flex items-center justify-between gap-3 py-1 border-b border-base-content/5 last:border-b-0"
        prop.children [
            Html.span [ prop.className DesignSystem.dataText; prop.text slug ]
            Html.span [ prop.className DesignSystem.mutedText; prop.text label ]
        ]
    ]

let private notesMigrationPanel (model: Model) (dispatch: Msg -> unit) =
    sectionCard "Gate 1 — Migrate to Notes" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Converts every legacy content-block/game-journal owner into one Notes_saved event (ADR-0080). Exact-match owner resolution only — ambiguous or orphaned slugs are reported below, never guessed. Additive and safe to re-run; an owner already migrated is skipped."
        ]
        match model.NotesMigrationPreview with
        | None ->
            Daisy.button.button [
                button.outline; button.sm
                prop.disabled model.NotesMigrationLoading
                prop.onClick (fun _ -> dispatch Load_notes_migration_preview)
                prop.text "Load preview"
            ]
        | Some preview ->
            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    Html.span [
                        prop.className DesignSystem.bodyText
                        prop.text (sprintf "%d resolved, %d ambiguous, %d orphaned" (List.length preview.Resolved) (List.length preview.Ambiguous) (List.length preview.Orphan))
                    ]
                    if not (List.isEmpty preview.Ambiguous) then
                        Html.div [
                            prop.children [
                                yield Html.span [ prop.className DesignSystem.eyebrow; prop.text "Ambiguous — matched more than one media type" ]
                                yield! preview.Ambiguous |> List.map (fun r -> ownerRefRow "ambiguous" r.Slug)
                            ]
                        ]
                    if not (List.isEmpty preview.Orphan) then
                        Html.div [
                            prop.children [
                                yield Html.span [ prop.className DesignSystem.eyebrow; prop.text "Orphaned — matched no media type" ]
                                yield! preview.Orphan |> List.map (fun r -> ownerRefRow "orphan" r.Slug)
                            ]
                        ]
                    Daisy.button.button [
                        button.primary; button.sm
                        prop.disabled (model.NotesMigrationLoading || List.isEmpty preview.Resolved)
                        prop.onClick (fun _ -> dispatch Run_notes_migration_clicked)
                        prop.text (if model.NotesMigrationLoading then "Running…" else "Confirm migration")
                    ]
                ]
            ]
        match model.NotesMigrationReport with
        | None -> Html.none
        | Some report ->
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text (sprintf "Last run: %d converted, %d resolved, %d ambiguous, %d orphaned" report.Converted (List.length report.Resolved) (List.length report.Ambiguous) (List.length report.Orphan))
            ]
    ]

let private purgeLegacyNotesPanel (model: Model) (dispatch: Msg -> unit) =
    sectionCard "Gate 2 — Purge legacy stores" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Bulk-deletes every ContentBlocks-* stream belonging to a Gate-1-resolved owner, then drops the content_blocks and game_journal_blocks tables (ADR-0034 protocol — VACUUM INTO backup first). Destructive, single confirm, no undo except the backup."
        ]
        if not model.NotesMigrationConfirmedThisSession then
            Html.p [
                prop.className "text-sm text-warning"
                prop.text "Run Gate 1 — Migrate to Notes at least once this session before purging."
            ]
        match model.PurgeLegacyNotesPreview with
        | None ->
            Daisy.button.button [
                button.outline; button.sm
                prop.disabled (model.PurgeLegacyNotesLoading || not model.NotesMigrationConfirmedThisSession)
                prop.onClick (fun _ -> dispatch Load_purge_legacy_notes_preview)
                prop.text "Load preview"
            ]
        | Some preview ->
            Html.div [
                prop.className "flex flex-col gap-3"
                prop.children [
                    Html.span [
                        prop.className DesignSystem.bodyText
                        prop.text (sprintf "%d stream%s, %d event%s to delete" (List.length preview.StreamIds) (if List.length preview.StreamIds = 1 then "" else "s") preview.EventCount (if preview.EventCount = 1 then "" else "s"))
                    ]
                    if not (List.isEmpty preview.ExcludedStreamIds) then
                        Html.div [
                            prop.children [
                                yield Html.span [ prop.className DesignSystem.eyebrow; prop.text "Excluded — ambiguous/orphan owners' streams" ]
                                yield! preview.ExcludedStreamIds |> List.map (fun s -> ownerRefRow "excluded" s)
                            ]
                        ]
                    Daisy.button.button [
                        button.error; button.sm
                        prop.disabled (model.PurgeLegacyNotesLoading || not model.NotesMigrationConfirmedThisSession)
                        prop.onClick (fun _ -> dispatch Purge_legacy_notes_clicked)
                        prop.text "Confirm purge"
                    ]
                ]
            ]
    ]

// ── Backup stats (keep-all retention) ──

let private backupStatsPanel (model: Model) =
    sectionCard "Backups" [
        Html.p [
            prop.className DesignSystem.mutedText
            prop.text "Every surgery mutation takes a VACUUM INTO snapshot before touching anything. Keep-all retention — nothing here is ever auto-pruned."
        ]
        match model.BackupStats with
        | None -> Html.p [ prop.className DesignSystem.mutedText; prop.text "Loading..." ]
        | Some stats ->
            Html.div [
                prop.className "grid grid-cols-2 gap-3"
                prop.children [
                    Html.div [
                        prop.children [
                            Html.span [ prop.className DesignSystem.eyebrow; prop.text "Backup count" ]
                            Html.div [ prop.className (DesignSystem.dataText + " text-sm"); prop.text (string stats.Count) ]
                        ]
                    ]
                    Html.div [
                        prop.children [
                            Html.span [ prop.className DesignSystem.eyebrow; prop.text "Total size" ]
                            Html.div [ prop.className (DesignSystem.dataText + " text-sm"); prop.text (formatBytes stats.TotalBytes) ]
                        ]
                    ]
                ]
            ]
    ]

// ── Confirm dialog (paper-overlay, ADR-0016 — same ModalPanel the compensating-event composer and image purge use) ──

let private confirmDialog (model: Model) (dispatch: Msg -> unit) =
    match model.PendingAction with
    | None -> Html.none
    | Some (PendingEdit(row, newData, _)) ->
        ModalPanel.viewWithFooter
            "Confirm edit"
            (fun () -> dispatch Cancel_pending)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text (sprintf "Edit event %d on %s. A backup is taken first; full-text search is re-synced afterward." row.GlobalPosition row.StreamId)
                ]
                Html.pre [
                    prop.className "bg-base-300/50 rounded-lg p-3 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all mt-2"
                    prop.text newData
                ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch Cancel_pending); prop.text "Cancel" ]
                Daisy.button.button [ button.primary; prop.disabled model.IsCommitting; prop.onClick (fun _ -> dispatch Confirm_pending); prop.text "Confirm edit" ]
            ]
    | Some (PendingDelete preview) ->
        ModalPanel.viewWithFooter
            "Confirm delete"
            (fun () -> dispatch Cancel_pending)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text (sprintf "Delete event %d (%s) on %s. A backup is taken first." preview.Event.GlobalPosition preview.Event.EventType preview.Event.StreamId)
                ]
                Html.p [
                    prop.className "text-sm text-warning mt-2"
                    prop.text (sprintf "%s is currently at position %d — this leaves a permanent gap. This is a hard delete — there is no trash or undo (the backup file is the only way back)." preview.Event.StreamId preview.StreamCurrentPosition)
                ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch Cancel_pending); prop.text "Cancel" ]
                Daisy.button.button [ button.error; prop.disabled model.IsCommitting; prop.onClick (fun _ -> dispatch Confirm_pending); prop.text "Confirm delete" ]
            ]
    | Some (PendingRename(oldType, newType, preview)) ->
        ModalPanel.viewWithFooter
            "Confirm rename"
            (fun () -> dispatch Cancel_pending)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text (sprintf "Rename %d event%s from '%s' to '%s'. A backup is taken first." preview.Count (if preview.Count = 1 then "" else "s") oldType newType)
                ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch Cancel_pending); prop.text "Cancel" ]
                Daisy.button.button [ button.error; prop.disabled model.IsCommitting; prop.onClick (fun _ -> dispatch Confirm_pending); prop.text "Confirm rename" ]
            ]
    | Some (PendingPurgeLegacyNotes preview) ->
        ModalPanel.viewWithFooter
            "Confirm purge"
            (fun () -> dispatch Cancel_pending)
            [
                Html.p [
                    prop.className DesignSystem.bodyText
                    prop.text (sprintf "Delete %d event%s across %d ContentBlocks-* stream%s, then drop content_blocks and game_journal_blocks. A backup is taken first." preview.EventCount (if preview.EventCount = 1 then "" else "s") (List.length preview.StreamIds) (if List.length preview.StreamIds = 1 then "" else "s"))
                ]
                Html.p [
                    prop.className "text-sm text-warning mt-2"
                    prop.text "This is a hard delete — there is no trash or undo (the backup file is the only way back). Every projection is flagged dirty until the next Rebuild all."
                ]
            ]
            [
                Daisy.button.button [ button.ghost; prop.onClick (fun _ -> dispatch Cancel_pending); prop.text "Cancel" ]
                Daisy.button.button [ button.error; prop.disabled model.IsCommitting; prop.onClick (fun _ -> dispatch Confirm_pending); prop.text "Confirm purge" ]
            ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Raw log surgery — the escape hatch for cases the compensating-event composer can't reach. Every mutation is backed up first (VACUUM INTO), previewed and confirmed, and leaves every projection flagged dirty until the next Rebuild all."
            ]
            resultBanner model.LastResult
            editPanel model dispatch
            deletePanel model dispatch
            renamePanel model dispatch
            notesMigrationPanel model dispatch
            purgeLegacyNotesPanel model dispatch
            backupStatsPanel model
            confirmDialog model dispatch
        ]
    ]
