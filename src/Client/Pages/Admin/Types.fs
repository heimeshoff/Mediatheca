module Mediatheca.Client.Pages.Admin.Types

// The Admin composite (administration-k3vmt): a headless child holding the
// six former /admin tab models, now rendered as inline collapsible sections
// on Settings rather than as a tabbed page in its own right (see
// Pages/Admin/Views.fs's per-section render functions and
// Pages/Settings/State.fs's lazy-load/expand wiring). Events delegates to
// EventBrowser, Health to AdminHealth (administration-hw74a), Projections to
// AdminProjections (administration-qjcp4), Images to AdminImages
// (administration-xx3mw), Jobs to AdminJobs (administration-yamm5), Surgery
// to AdminSurgery (administration-wwc36).
type Model = {
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
