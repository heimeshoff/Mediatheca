module Mediatheca.Client.State

open Elmish
open Mediatheca.Client.Router
open Mediatheca.Client.Types

let init () : Model * Cmd<Msg> =
    let dashboardModel, dashboardCmd = Pages.Dashboard.State.init ()
    let moviesModel, moviesCmd = Pages.Movies.State.init ()
    let friendsModel, friendsCmd = Pages.Friends.State.init ()
    let catalogModel, catalogCmd = Pages.Catalog.State.init ()
    let settingsModel, settingsCmd = Pages.Settings.State.init ()

    let model = {
        CurrentPage = Dashboard
        DashboardModel = dashboardModel
        MoviesModel = moviesModel
        FriendsModel = friendsModel
        CatalogModel = catalogModel
        SettingsModel = settingsModel
    }

    let cmd = Cmd.batch [
        Cmd.map DashboardMsg dashboardCmd
        Cmd.map MoviesMsg moviesCmd
        Cmd.map FriendsMsg friendsCmd
        Cmd.map CatalogMsg catalogCmd
        Cmd.map SettingsMsg settingsCmd
    ]

    model, cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged segments ->
        let page = Route.parseUrl segments
        { model with CurrentPage = page }, Cmd.none

    | DashboardMsg childMsg ->
        let childModel, childCmd = Pages.Dashboard.State.update childMsg model.DashboardModel
        { model with DashboardModel = childModel }, Cmd.map DashboardMsg childCmd

    | MoviesMsg childMsg ->
        let childModel, childCmd = Pages.Movies.State.update childMsg model.MoviesModel
        { model with MoviesModel = childModel }, Cmd.map MoviesMsg childCmd

    | FriendsMsg childMsg ->
        let childModel, childCmd = Pages.Friends.State.update childMsg model.FriendsModel
        { model with FriendsModel = childModel }, Cmd.map FriendsMsg childCmd

    | CatalogMsg childMsg ->
        let childModel, childCmd = Pages.Catalog.State.update childMsg model.CatalogModel
        { model with CatalogModel = childModel }, Cmd.map CatalogMsg childCmd

    | SettingsMsg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map SettingsMsg childCmd
