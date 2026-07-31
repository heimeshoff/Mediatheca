module Mediatheca.Client.Pages.AdminProjections.Types

open Mediatheca.Shared

/// The Projections tab (administration-qjcp4): checkpoint/lag/row-count
/// listing for every registered projection handler, plus a per-projection
/// "Rebuild" command whose progress streams over SSE from
/// `/api/stream/rebuild-projection/{name}` — not through Remoting, since
/// that route is a long-lived stream rather than a request/response call.
/// "Rebuild all" drives the same single-projection command sequentially
/// through `PendingRebuildAllQueue`, so it reuses the same server-side
/// concurrency guard (one rebuild in flight per projection at a time) rather
/// than needing its own route.
type RebuildProgress = {
    Position: int64
    Head: int64
    EventsProcessed: int64
}

/// Outcome of a successful import via `/api/stream/import-events`
/// (administration-vrc56, ADR-0029).
type ImportOutcome = { EventsImported: int }

/// One row-level discrepancy from a shadow-replay drift check
/// (administration-btvqa, ADR-0031). `Kind` is one of "onlyInLive" /
/// "onlyInShadow" / "columnMismatch" (mirrors `Administration.DriftDiscrepancy`
/// server-side); `Columns` is populated only for "columnMismatch".
type DriftDiscrepancy = {
    Table: string
    PrimaryKey: string
    Kind: string
    Columns: string list
}

type ProjectionDrift = {
    Name: string
    Discrepancies: DriftDiscrepancy list
}

/// The `complete` event's full payload from `/api/stream/drift-check`.
type DriftCheckResult = {
    Projections: ProjectionDrift list
    TotalDiscrepancies: int
}

type Model = {
    Stats: ProjectionStatRow list
    IsLoading: bool
    /// Live progress for projections currently rebuilding, client-tracked
    /// for the session (not persisted — a page reload just shows the
    /// server's IsRebuilding flag on the next Load).
    RebuildProgress: Map<string, RebuildProgress>
    /// Names currently mid-rebuild from this client's own perspective —
    /// drives spinner/disabled state without waiting for a Stats reload.
    RebuildingNames: Set<string>
    /// Last rejection/error message per projection, cleared on the next
    /// rebuild attempt for that projection.
    RebuildMessages: Map<string, string>
    IsRebuildingAll: bool
    PendingRebuildAllQueue: string list
    /// Event log backup (administration-vrc56, ADR-0029): export is a plain
    /// `<a href>` download (no client state needed — the browser handles
    /// it), import streams SSE progress from `/api/stream/import-events`
    /// the same way a projection rebuild does.
    IsImporting: bool
    ImportResult: ImportOutcome option
    /// Refusal (non-empty target store) or malformed-line failure message,
    /// cleared on the next import attempt.
    ImportMessage: string option
    /// Shadow-table replay drift detector (administration-btvqa, ADR-0031):
    /// "Run check" streams SSE progress from `/api/stream/drift-check` the
    /// same way a projection rebuild does.
    IsDriftChecking: bool
    /// Name of the projection whose shadow replay most recently finished,
    /// for a lightweight "currently replaying" indicator during the run.
    DriftCheckProgress: string option
    DriftCheckResult: DriftCheckResult option
    /// Rejection (dirty projection) or error message, cleared on the next
    /// "Run check" click.
    DriftCheckMessage: string option
    // Wipe-first event log import (administration-n8kqw, ADR-0038):
    // overwriting a non-empty store. `WipeImportPendingFile` + `WipeImportPreview`
    // both `Some` is what the view treats as "the confirm dialog is showing" —
    // Cancel clears both with no network request ever sent ("untouched" holds
    // by construction, not rollback).
    /// The file picked, held until Cancel/Confirm resolves it. `None` means
    /// no confirm dialog is showing.
    WipeImportPendingFile: Browser.Types.File option
    /// Non-blank line count of the picked file, computed client-side (via
    /// `File.text()`) before the preview request — no staging area, no
    /// second upload phase.
    WipeImportClientLineCount: int
    /// True between file selection and the preview response landing.
    WipeImportPreviewLoading: bool
    /// Discard-side stats from `getWipeImportPreview` — what's currently in
    /// the store, i.e. what a Wipe & Import would throw away. `Some` (along
    /// with `WipeImportPendingFile`) is what makes the confirm dialog show.
    WipeImportPreview: WipeImportPreview option
    /// The `backup` SSE event's path, rendered as soon as it arrives —
    /// before the transaction that could still fail even starts.
    WipeImportBackupPath: string option
    IsWipeImporting: bool
    /// (eventsImported, eventsDiscarded) from a committed `complete` event.
    WipeImportResult: (int * int) option
    /// Rejection or error message (backup failure or malformed-line
    /// rollback), cleared on the next wipe-import attempt.
    WipeImportMessage: string option
}

type Msg =
    | Load
    | Stats_loaded of ProjectionStatRow list
    | Rebuild_clicked of string
    | Rebuild_all_clicked
    | Start_next_queued_rebuild
    | Rebuild_progress of string * RebuildProgress
    | Rebuild_rejected of string * string
    | Rebuild_completed of string
    | Rebuild_failed of string * string
    | Import_file_selected of Browser.Types.File
    | Import_completed of ImportOutcome
    | Import_rejected of string
    | Import_failed of string
    | Drift_check_clicked
    | Drift_check_progress of string
    | Drift_check_completed of DriftCheckResult
    | Drift_check_rejected of string
    | Drift_check_failed of string
    | WipeImport_file_selected of Browser.Types.File
    | WipeImport_file_counted of Browser.Types.File * int
    | WipeImport_preview_loaded of WipeImportPreview
    | WipeImport_cancel
    | WipeImport_confirm
    | WipeImport_backup_received of string
    | WipeImport_completed of eventsImported: int * eventsDiscarded: int
    | WipeImport_rejected of string
    | WipeImport_failed of string
