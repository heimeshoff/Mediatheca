module Mediatheca.Client.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Router
open Mediatheca.Client.Types

let init (api: IMediathecaApi) () : Model * Cmd<Msg> =
    let dashboardModel, dashboardCmd = Pages.Dashboard.State.init ()
    let movieListModel, movieListCmd = Pages.Movies.State.init ()
    let movieDetailModel, movieDetailCmd = Pages.MovieDetail.State.init ""
    let friendListModel, friendListCmd = Pages.Friends.State.init ()
    let friendDetailModel, friendDetailCmd = Pages.FriendDetail.State.init ""
    let catalogListModel, catalogListCmd = Pages.Catalogs.State.init ()
    let catalogDetailModel, catalogDetailCmd = Pages.CatalogDetail.State.init ""
    let eventBrowserModel, eventBrowserCmd = Pages.EventBrowser.State.init ()
    let settingsModel, settingsCmd = Pages.Settings.State.init ()

    let model = {
        CurrentPage = Dashboard
        DashboardModel = dashboardModel
        MovieListModel = movieListModel
        MovieDetailModel = movieDetailModel
        FriendListModel = friendListModel
        FriendDetailModel = friendDetailModel
        CatalogListModel = catalogListModel
        CatalogDetailModel = catalogDetailModel
        EventBrowserModel = eventBrowserModel
        SettingsModel = settingsModel
    }

    let cmd = Cmd.batch [
        Cmd.map Dashboard_msg dashboardCmd
        Cmd.OfAsync.perform api.getDashboardStats () (fun stats -> Dashboard_msg (Pages.Dashboard.Types.Stats_loaded stats))
        Cmd.OfAsync.perform api.getRecentActivity 10 (fun activity -> Dashboard_msg (Pages.Dashboard.Types.Activity_loaded activity))
        Cmd.OfAsync.perform api.getMovies () (fun movies -> Dashboard_msg (Pages.Dashboard.Types.Movies_loaded movies))
        Cmd.map Movie_list_msg movieListCmd
        Cmd.map Settings_msg settingsCmd
    ]

    model, cmd

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Url_changed segments ->
        let page = Route.parseUrl segments
        let model = { model with CurrentPage = page }
        match page with
        | Movie_list ->
            let childModel, childCmd = Pages.Movies.State.init ()
            { model with MovieListModel = childModel },
            Cmd.map Movie_list_msg childCmd
        | Movie_detail slug ->
            let childModel, childCmd = Pages.MovieDetail.State.init slug
            { model with MovieDetailModel = childModel },
            Cmd.map Movie_detail_msg childCmd
        | Friend_list ->
            let childModel, childCmd = Pages.Friends.State.init ()
            { model with FriendListModel = childModel },
            Cmd.map Friend_list_msg childCmd
        | Friend_detail slug ->
            let childModel, childCmd = Pages.FriendDetail.State.init slug
            { model with FriendDetailModel = childModel },
            Cmd.map Friend_detail_msg childCmd
        | Catalog_list ->
            let childModel, childCmd = Pages.Catalogs.State.init ()
            { model with CatalogListModel = childModel },
            Cmd.map Catalog_list_msg childCmd
        | Catalog_detail slug ->
            let childModel, childCmd = Pages.CatalogDetail.State.init slug
            { model with CatalogDetailModel = childModel },
            Cmd.map Catalog_detail_msg childCmd
        | Event_browser ->
            let childModel, childCmd = Pages.EventBrowser.State.init ()
            { model with EventBrowserModel = childModel },
            Cmd.map Event_browser_msg childCmd
        | Settings ->
            let childModel, childCmd = Pages.Settings.State.init ()
            { model with SettingsModel = childModel },
            Cmd.map Settings_msg childCmd
        | Dashboard ->
            let childModel, childCmd = Pages.Dashboard.State.init ()
            { model with DashboardModel = childModel },
            Cmd.batch [
                Cmd.map Dashboard_msg childCmd
                Cmd.OfAsync.perform api.getDashboardStats () (fun stats -> Dashboard_msg (Pages.Dashboard.Types.Stats_loaded stats))
                Cmd.OfAsync.perform api.getRecentActivity 10 (fun activity -> Dashboard_msg (Pages.Dashboard.Types.Activity_loaded activity))
                Cmd.OfAsync.perform api.getMovies () (fun movies -> Dashboard_msg (Pages.Dashboard.Types.Movies_loaded movies))
            ]
        | _ -> model, Cmd.none

    | Dashboard_msg childMsg ->
        let childModel, childCmd = Pages.Dashboard.State.update api childMsg model.DashboardModel
        { model with DashboardModel = childModel }, Cmd.map Dashboard_msg childCmd

    | Movie_list_msg childMsg ->
        let childModel, childCmd = Pages.Movies.State.update api childMsg model.MovieListModel
        { model with MovieListModel = childModel }, Cmd.map Movie_list_msg childCmd

    | Movie_detail_msg childMsg ->
        let childModel, childCmd = Pages.MovieDetail.State.update api childMsg model.MovieDetailModel
        { model with MovieDetailModel = childModel }, Cmd.map Movie_detail_msg childCmd

    | Friend_list_msg childMsg ->
        let childModel, childCmd = Pages.Friends.State.update api childMsg model.FriendListModel
        { model with FriendListModel = childModel }, Cmd.map Friend_list_msg childCmd

    | Friend_detail_msg childMsg ->
        let childModel, childCmd = Pages.FriendDetail.State.update api childMsg model.FriendDetailModel
        { model with FriendDetailModel = childModel }, Cmd.map Friend_detail_msg childCmd

    | Catalog_list_msg childMsg ->
        let childModel, childCmd = Pages.Catalogs.State.update api childMsg model.CatalogListModel
        { model with CatalogListModel = childModel }, Cmd.map Catalog_list_msg childCmd

    | Catalog_detail_msg childMsg ->
        let childModel, childCmd = Pages.CatalogDetail.State.update api childMsg model.CatalogDetailModel
        { model with CatalogDetailModel = childModel }, Cmd.map Catalog_detail_msg childCmd

    | Event_browser_msg childMsg ->
        let childModel, childCmd = Pages.EventBrowser.State.update api childMsg model.EventBrowserModel
        { model with EventBrowserModel = childModel }, Cmd.map Event_browser_msg childCmd

    | Settings_msg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update api childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map Settings_msg childCmd
