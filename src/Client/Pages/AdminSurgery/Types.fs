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
    /// curation-j4qqt (ADR-0080 §10-11), Gate 2 — "Purge legacy stores".
    | PendingPurgeLegacyNotes of preview: PurgeLegacyNotesPreview

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
    // curation-j4qqt (ADR-0080 §10-11): Gate 1 — "Migrate to Notes" (preview
    // -> confirm, additive/idempotent) and Gate 2 — "Purge legacy stores"
    // (preview -> confirm, destructive, reuses the shared PendingAction/
    // confirm-dialog/Mutation_completed plumbing above since it returns the
    // same SurgeryResult edit/delete/rename do). Gate 2 refuses to run
    // before Gate 1 has been confirmed at least once THIS SESSION
    // (NotesMigrationConfirmedThisSession) — a human-eye guard, not a
    // server-enforced one; the operator is expected to run Gate 1 first.
    NotesMigrationPreview: NotesMigrationPreview option
    NotesMigrationLoading: bool
    NotesMigrationReport: NotesMigrationReport option
    NotesMigrationConfirmedThisSession: bool
    PurgeLegacyNotesPreview: PurgeLegacyNotesPreview option
    PurgeLegacyNotesLoading: bool
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
    | Load_notes_migration_preview
    | Notes_migration_preview_loaded of NotesMigrationPreview
    | Run_notes_migration_clicked
    | Notes_migration_completed of NotesMigrationReport
    | Load_purge_legacy_notes_preview
    | Purge_legacy_notes_preview_loaded of PurgeLegacyNotesPreview
    | Purge_legacy_notes_clicked
