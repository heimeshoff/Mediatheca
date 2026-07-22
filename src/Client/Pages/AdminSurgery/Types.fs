module Mediatheca.Client.Pages.AdminSurgery.Types

open Mediatheca.Shared

/// The Surgery tab (administration-wwc36, ADR-0034): the raw-log escape
/// hatch for cases the compensating-event composer (administration-xjmda,
/// ADR-0032) can't reach — a genuinely wrong-payload event, or a stranded
/// event-type name. Three independent operations (edit / delete / rename),
/// each behind the SAME guardrail shape: load a preview, confirm in a
/// paper-overlay dialog (ADR-0016), commit, show the typed `SurgeryResult`.
/// `PendingAction` tracks which operation the confirm dialog is currently
/// showing — only one action can be pending confirmation at a time.
type PendingAction =
    | PendingEdit of target: SurgeryEventRow * newData: string * newMetadata: string
    | PendingDelete of preview: SurgeryDeletePreview
    | PendingRename of oldType: string * newType: string * preview: SurgeryRenamePreview

type Model = {
    // Edit
    EditGlobalPositionInput: string
    EditPreview: SurgeryEventRow option
    EditDataInput: string
    EditMetadataInput: string
    EditLoading: bool
    EditError: string option
    // Delete
    DeleteGlobalPositionInput: string
    DeletePreview: SurgeryDeletePreview option
    DeleteLoading: bool
    DeleteError: string option
    // Rename
    RenameOldTypeInput: string
    RenameNewTypeInput: string
    RenamePreview: SurgeryRenamePreview option
    RenameLoading: bool
    RenameError: string option
    // Shared confirm/commit
    PendingAction: PendingAction option
    IsCommitting: bool
    CommitError: string option
    LastResult: SurgeryResult option
    // Backups (keep-all retention panel)
    BackupStats: BackupStats option
}

type Msg =
    | Edit_global_position_changed of string
    | Edit_data_changed of string
    | Edit_metadata_changed of string
    | Load_edit_preview
    | Edit_preview_loaded of SurgeryEventRow option
    | Save_edit_clicked
    | Delete_global_position_changed of string
    | Load_delete_preview
    | Delete_preview_loaded of SurgeryDeletePreview option
    | Delete_clicked
    | Rename_old_type_changed of string
    | Rename_new_type_changed of string
    | Load_rename_preview
    | Rename_preview_loaded of SurgeryRenamePreview
    | Rename_clicked
    | Cancel_pending
    | Confirm_pending
    | Mutation_completed of SurgeryResult
    | Load_backup_stats
    | Backup_stats_loaded of BackupStats
