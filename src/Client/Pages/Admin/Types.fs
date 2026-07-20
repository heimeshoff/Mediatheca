module Mediatheca.Client.Pages.Admin.Types

open Mediatheca.Client.Router

// The Admin console tab shell. Events is a fully wired tab (delegates to the
// existing EventBrowser page), Health delegates to AdminHealth
// (administration-hw74a); Projections/Jobs/Surgery are placeholder panels
// until their own tasks (administration-v4y9g, -mtf1f, -qjcp4 and backlog
// surgery/ops tasks) land.
type Model = {
    ActiveTab: AdminTab
    EventBrowserModel: Mediatheca.Client.Pages.EventBrowser.Types.Model
    HealthModel: Mediatheca.Client.Pages.AdminHealth.Types.Model
}

type Msg =
    | Event_browser_msg of Mediatheca.Client.Pages.EventBrowser.Types.Msg
    | Health_msg of Mediatheca.Client.Pages.AdminHealth.Types.Msg
