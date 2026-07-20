module Mediatheca.Client.Pages.Admin.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Router
open Mediatheca.Client.Pages.Admin.Types

let init (tab: AdminTab) : Model * Cmd<Msg> =
    let eventBrowserModel, eventBrowserCmd = Mediatheca.Client.Pages.EventBrowser.State.init ()
    { ActiveTab = tab
      EventBrowserModel = eventBrowserModel },
    Cmd.map Event_browser_msg eventBrowserCmd

let update (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Event_browser_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.EventBrowser.State.update adminApi childMsg model.EventBrowserModel
        { model with EventBrowserModel = childModel }, Cmd.map Event_browser_msg childCmd
