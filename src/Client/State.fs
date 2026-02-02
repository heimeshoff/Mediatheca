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
        Cmd.map DashboardMsg dashboardCmd
        Cmd.map MovieListMsg movieListCmd
        Cmd.map SettingsMsg settingsCmd
    ]

    model, cmd

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged segments ->
        let page = Route.parseUrl segments
        let model = { model with CurrentPage = page }
        match page with
        | MovieList ->
            let childModel, childCmd = Pages.Movies.State.init ()
            { model with MovieListModel = childModel },
            Cmd.map MovieListMsg childCmd
        | MovieDetail slug ->
            let childModel, childCmd = Pages.MovieDetail.State.init slug
            { model with MovieDetailModel = childModel },
            Cmd.map MovieDetailMsg childCmd
        | FriendList ->
            let childModel, childCmd = Pages.Friends.State.init ()
            { model with FriendListModel = childModel },
            Cmd.map FriendListMsg childCmd
        | FriendDetail slug ->
            let childModel, childCmd = Pages.FriendDetail.State.init slug
            { model with FriendDetailModel = childModel },
            Cmd.map FriendDetailMsg childCmd
        | _ -> model, Cmd.none

    | DashboardMsg childMsg ->
        let childModel, childCmd = Pages.Dashboard.State.update childMsg model.DashboardModel
        { model with DashboardModel = childModel }, Cmd.map DashboardMsg childCmd

    | MovieListMsg childMsg ->
        let childModel, childCmd = Pages.Movies.State.update api childMsg model.MovieListModel
        { model with MovieListModel = childModel }, Cmd.map MovieListMsg childCmd

    | MovieDetailMsg childMsg ->
        let childModel, childCmd = Pages.MovieDetail.State.update api childMsg model.MovieDetailModel
        { model with MovieDetailModel = childModel }, Cmd.map MovieDetailMsg childCmd

    | FriendListMsg childMsg ->
        let childModel, childCmd = Pages.Friends.State.update api childMsg model.FriendListModel
        { model with FriendListModel = childModel }, Cmd.map FriendListMsg childCmd

    | FriendDetailMsg childMsg ->
        let childModel, childCmd = Pages.FriendDetail.State.update api childMsg model.FriendDetailModel
        { model with FriendDetailModel = childModel }, Cmd.map FriendDetailMsg childCmd

    | SettingsMsg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map SettingsMsg childCmd
