module Mediatheca.Client.Pages.AdminHealth.Types

open Mediatheca.Shared

/// The Health tab (administration-hw74a). Loads the whole panel from one
/// aggregate DTO (IAdminApi.getHealthStats) — a single round trip, per the
/// task's acceptance criteria.
type Model = {
    Stats: HealthStats option
    IsLoading: bool
}

type Msg =
    | Load
    | Stats_loaded of HealthStats
