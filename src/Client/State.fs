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
    let settingsModel, settingsCmd = Pages.Settings.State.init ()

    let model = {
        CurrentPage = Dashboard
        DashboardModel = dashboardModel
        MovieListModel = movieListModel
        MovieDetailModel = movieDetailModel
        FriendListModel = friendListModel
        FriendDetailModel = friendDetailModel
        SettingsModel = settingsModel
    }

    let cmd = Cmd.batch [
        Cmd.map Dashboard_msg dashboardCmd
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
        | Settings ->
            let childModel, childCmd = Pages.Settings.State.init ()
            { model with SettingsModel = childModel },
            Cmd.map Settings_msg childCmd
        | _ -> model, Cmd.none

    | Dashboard_msg childMsg ->
        let childModel, childCmd = Pages.Dashboard.State.update childMsg model.DashboardModel
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

    | Settings_msg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update api childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map Settings_msg childCmd
