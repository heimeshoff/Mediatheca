module Mediatheca.Client.Pages.Admin.Types

open Mediatheca.Client.Router

// The Admin console tab shell. Events is a fully wired tab (delegates to the
// existing EventBrowser page), Health delegates to AdminHealth
// (administration-hw74a), Projections delegates to AdminProjections
// (administration-qjcp4), Images delegates to AdminImages
// (administration-xx3mw), Jobs delegates to AdminJobs (administration-yamm5),
// Surgery delegates to AdminSurgery (administration-wwc36).
type Model = {
    ActiveTab: AdminTab
    EventBrowserModel: Mediatheca.Client.Pages.EventBrowser.Types.Model
    HealthModel: Mediatheca.Client.Pages.AdminHealth.Types.Model
    ProjectionsModel: Mediatheca.Client.Pages.AdminProjections.Types.Model
    ImagesModel: Mediatheca.Client.Pages.AdminImages.Types.Model
    JobsModel: Mediatheca.Client.Pages.AdminJobs.Types.Model
    SurgeryModel: Mediatheca.Client.Pages.AdminSurgery.Types.Model
}

type Msg =
    | Event_browser_msg of Mediatheca.Client.Pages.EventBrowser.Types.Msg
    | Health_msg of Mediatheca.Client.Pages.AdminHealth.Types.Msg
    | Projections_msg of Mediatheca.Client.Pages.AdminProjections.Types.Msg
    | Images_msg of Mediatheca.Client.Pages.AdminImages.Types.Msg
    | Jobs_msg of Mediatheca.Client.Pages.AdminJobs.Types.Msg
    | Surgery_msg of Mediatheca.Client.Pages.AdminSurgery.Types.Msg
