module Mediatheca.Client.Types

open Mediatheca.Client.Router

type Model = {
    CurrentPage: Page
    DashboardModel: Pages.Dashboard.Types.Model
    MovieListModel: Pages.Movies.Types.Model
    MovieDetailModel: Pages.MovieDetail.Types.Model
    FriendListModel: Pages.Friends.Types.Model
    FriendDetailModel: Pages.FriendDetail.Types.Model
    SettingsModel: Pages.Settings.Types.Model
}

type Msg =
    | UrlChanged of string list
    | DashboardMsg of Pages.Dashboard.Types.Msg
    | MovieListMsg of Pages.Movies.Types.Msg
    | MovieDetailMsg of Pages.MovieDetail.Types.Msg
    | FriendListMsg of Pages.Friends.Types.Msg
    | FriendDetailMsg of Pages.FriendDetail.Types.Msg
    | SettingsMsg of Pages.Settings.Types.Msg
