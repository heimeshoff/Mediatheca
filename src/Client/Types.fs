module Mediatheca.Client.Types

open Mediatheca.Client.Router

type Model = {
    CurrentPage: Page
    DashboardModel: Pages.Dashboard.Types.Model
    MoviesModel: Pages.Movies.Types.Model
    FriendsModel: Pages.Friends.Types.Model
    CatalogModel: Pages.Catalog.Types.Model
    SettingsModel: Pages.Settings.Types.Model
}

type Msg =
    | UrlChanged of string list
    | DashboardMsg of Pages.Dashboard.Types.Msg
    | MoviesMsg of Pages.Movies.Types.Msg
    | FriendsMsg of Pages.Friends.Types.Msg
    | CatalogMsg of Pages.Catalog.Types.Msg
    | SettingsMsg of Pages.Settings.Types.Msg
