module Mediatheca.Client.Pages.AdminImages.Types

open Mediatheca.Shared

/// The Images tab (administration-xx3mw): images/ cache stats, orphan
/// detection, and a hard-delete purge. See ADR-0025 for the ref-source /
/// not-dirty guard / TOCTOU-safe purge rationale.
type PurgeIntent =
    | PurgeSelectedIntent
    | PurgeAllIntent

type Model = {
    Stats: ImageCacheStats option
    IsLoadingStats: bool
    OrphanScan: OrphanScan option
    IsLoadingOrphans: bool
    /// Relative paths the operator has checked for a specific-subset purge.
    /// Pruned to the current orphan set whenever a fresh scan lands, so a
    /// stale selection can't reference a path that's no longer orphan.
    Selected: Set<string>
    /// Set while the confirm dialog is open — which action it's confirming.
    PendingIntent: PurgeIntent option
    IsPurging: bool
    LastPurgeResult: PurgeResult option
}

type Msg =
    | Load
    | Stats_loaded of ImageCacheStats
    | Orphans_loaded of OrphanScan
    | Toggle_selected of string
    | Select_all
    | Select_none
    | Purge_selected_clicked
    | Purge_all_clicked
    | Cancel_purge
    | Confirm_purge
    | Purge_completed of PurgeResult
