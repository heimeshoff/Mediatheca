module Mediatheca.Client.Pages.Admin.Types

open Mediatheca.Client.Router

// The Admin console tab shell. Events is a fully wired tab (delegates to the
// existing EventBrowser page); Projections/Health/Jobs/Surgery are placeholder
// panels until their own tasks (administration-g5dfy, -v4y9g, -mtf1f, -qjcp4,
// -hw74a and backlog surgery/ops tasks) land.
type Model = {
    ActiveTab: AdminTab
    EventBrowserModel: Mediatheca.Client.Pages.EventBrowser.Types.Model
}

type Msg =
    | Event_browser_msg of Mediatheca.Client.Pages.EventBrowser.Types.Msg
