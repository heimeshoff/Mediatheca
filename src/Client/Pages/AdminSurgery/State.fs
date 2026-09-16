module Mediatheca.Client.Pages.AdminSurgery.State

open System
open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminSurgery.Types

let init () : Model * Cmd<Msg> =
    { EditGlobalPositionInput = ""
      EditPreview = None
      EditDataInput = ""
      EditMetadataInput = ""
      EditLoading = false
      EditError = None
      DeleteGlobalPositionInput = ""
      DeletePreview = None
      DeleteLoading = false
      DeleteError = None
      RenameOldTypeInput = ""
      RenameNewTypeInput = ""
      RenamePreview = None
      RenameLoading = false
      RenameError = None
      PendingAction = None
      IsCommitting = false
      CommitError = None
      LastResult = None
      BackupStats = None },
    Cmd.ofMsg Load_backup_stats

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Edit_global_position_changed value ->
        { model with EditGlobalPositionInput = value; EditPreview = None; EditError = None }, Cmd.none

    | Edit_data_changed value ->
        { model with EditDataInput = value }, Cmd.none

    | Edit_metadata_changed value ->
        { model with EditMetadataInput = value }, Cmd.none

    | Load_edit_preview ->
        match Int64.TryParse model.EditGlobalPositionInput with
        | true, gp ->
            { model with EditLoading = true; EditError = None },
            Cmd.OfAsync.perform api.previewEventEdit gp Edit_preview_loaded
        | false, _ ->
            { model with EditError = Some "Enter a valid global position (a whole number)" }, Cmd.none

    | Edit_preview_loaded None ->
        { model with EditLoading = false; EditPreview = None; EditError = Some "No event exists at that global position" }, Cmd.none

    | Edit_preview_loaded (Some row) ->
        { model with
            EditLoading = false
            EditPreview = Some row
            EditDataInput = row.Data
            EditMetadataInput = row.Metadata
            EditError = None },
        Cmd.none

    | Save_edit_clicked ->
        match model.EditPreview with
        | None -> model, Cmd.none
        | Some row ->
            { model with PendingAction = Some (PendingEdit(row, model.EditDataInput, model.EditMetadataInput)) }, Cmd.none

    | Delete_global_position_changed value ->
        { model with DeleteGlobalPositionInput = value; DeletePreview = None; DeleteError = None }, Cmd.none

    | Load_delete_preview ->
        match Int64.TryParse model.DeleteGlobalPositionInput with
        | true, gp ->
            { model with DeleteLoading = true; DeleteError = None },
            Cmd.OfAsync.perform api.previewEventDelete gp Delete_preview_loaded
        | false, _ ->
            { model with DeleteError = Some "Enter a valid global position (a whole number)" }, Cmd.none

    | Delete_preview_loaded None ->
        { model with DeleteLoading = false; DeletePreview = None; DeleteError = Some "No event exists at that global position" }, Cmd.none

    | Delete_preview_loaded (Some preview) ->
        { model with DeleteLoading = false; DeletePreview = Some preview; DeleteError = None }, Cmd.none

    | Delete_clicked ->
        match model.DeletePreview with
        | None -> model, Cmd.none
        | Some preview -> { model with PendingAction = Some (PendingDelete preview) }, Cmd.none

    | Rename_old_type_changed value ->
        { model with RenameOldTypeInput = value; RenamePreview = None; RenameError = None }, Cmd.none

    | Rename_new_type_changed value ->
        { model with RenameNewTypeInput = value }, Cmd.none

    | Load_rename_preview ->
        if model.RenameOldTypeInput.Trim() = "" then
            { model with RenameError = Some "Enter the event type to rename" }, Cmd.none
        else
            { model with RenameLoading = true; RenameError = None },
            Cmd.OfAsync.perform api.previewEventTypeRename model.RenameOldTypeInput Rename_preview_loaded

    | Rename_preview_loaded preview ->
        { model with RenameLoading = false; RenamePreview = Some preview }, Cmd.none

    | Rename_clicked ->
        match model.RenamePreview with
        | None -> model, Cmd.none
        | Some preview ->
            if model.RenameNewTypeInput.Trim() = "" then
                { model with RenameError = Some "Enter the new event type name" }, Cmd.none
            else
                { model with PendingAction = Some (PendingRename(model.RenameOldTypeInput, model.RenameNewTypeInput, preview)) }, Cmd.none

    | Cancel_pending ->
        { model with PendingAction = None; CommitError = None }, Cmd.none

    | Confirm_pending ->
        match model.PendingAction with
        | None -> model, Cmd.none
        | Some (PendingEdit(row, newData, newMetadata)) ->
            { model with IsCommitting = true; PendingAction = None; CommitError = None },
            Cmd.OfAsync.perform (api.editEvent row.GlobalPosition newData) newMetadata Mutation_completed
        | Some (PendingDelete preview) ->
            { model with IsCommitting = true; PendingAction = None; CommitError = None },
            Cmd.OfAsync.perform api.deleteEvent preview.Event.GlobalPosition Mutation_completed
        | Some (PendingRename(oldType, newType, _)) ->
            { model with IsCommitting = true; PendingAction = None; CommitError = None },
            Cmd.OfAsync.perform (api.renameEventType oldType) newType Mutation_completed

    | Mutation_completed result ->
        let model =
            { model with
                IsCommitting = false
                LastResult = Some result
                EditPreview = None
                EditGlobalPositionInput = ""
                DeletePreview = None
                DeleteGlobalPositionInput = ""
                RenamePreview = None
                RenameOldTypeInput = ""
                RenameNewTypeInput = "" }
        model, Cmd.ofMsg Load_backup_stats

    | Load_backup_stats ->
        model, Cmd.OfAsync.perform api.getBackupStats () Backup_stats_loaded

    | Backup_stats_loaded stats ->
        { model with BackupStats = Some stats }, Cmd.none
