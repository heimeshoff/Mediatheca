module Mediatheca.Client.Pages.Admin.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Router
open Mediatheca.Client.Pages.Admin.Types

let init (tab: AdminTab) : Model * Cmd<Msg> =
    let eventBrowserModel, eventBrowserCmd = Mediatheca.Client.Pages.EventBrowser.State.init ()
    let healthModel, healthCmd = Mediatheca.Client.Pages.AdminHealth.State.init ()
    { ActiveTab = tab
      EventBrowserModel = eventBrowserModel
      HealthModel = healthModel },
    Cmd.batch [
        Cmd.map Event_browser_msg eventBrowserCmd
        Cmd.map Health_msg healthCmd
    ]

/// Called from root `State.Url_changed` when the user navigates away from the
/// Admin page entirely (administration-mtf1f iteration 2). Bumps the Event
/// Browser's Follow epoch so any Follow poll already scheduled — or a
/// `getEventsAfter` request already in flight at the moment of navigation —
/// is stale by the time it (re)dispatches, and is dropped by the existing
/// `Poll_tail`/`Tail_loaded` epoch guard instead of rescheduling. Idempotent:
/// a no-op when Follow was already off.
let stopFollowing (model: Model) : Model =
    { model with EventBrowserModel = Mediatheca.Client.Pages.EventBrowser.State.stopFollowing model.EventBrowserModel }

let update (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Event_browser_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.EventBrowser.State.update adminApi childMsg model.EventBrowserModel
        { model with EventBrowserModel = childModel }, Cmd.map Event_browser_msg childCmd

    | Health_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminHealth.State.update adminApi childMsg model.HealthModel
        { model with HealthModel = childModel }, Cmd.map Health_msg childCmd
