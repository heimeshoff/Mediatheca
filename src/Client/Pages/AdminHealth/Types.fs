module Mediatheca.Client.Pages.AdminHealth.Types

open Mediatheca.Shared

/// The Health tab (administration-hw74a). Loads the whole panel from one
/// aggregate DTO (IAdminApi.getHealthStats) — a single round trip, per the
/// task's acceptance criteria.
///
/// curation-w9fkq (ADR-0079 §5 resolved): also hosts the one-off "Backfill
/// catalog entry media types" action — no preview/confirm ceremony (it's
/// additive, non-destructive, and safely re-runnable), so a button + its
/// last report is all this tab needs.
type Model = {
    Stats: HealthStats option
    IsLoading: bool
    CatalogBackfillRunning: bool
    CatalogBackfillReport: CatalogMediaTypeBackfillReport option
}

type Msg =
    | Load
    | Stats_loaded of HealthStats
    | Run_catalog_media_type_backfill_clicked
    | Catalog_media_type_backfill_completed of CatalogMediaTypeBackfillReport
