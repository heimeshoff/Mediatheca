module Mediatheca.Client.Types

open Mediatheca.Client.Router

type Model = {
    CurrentPage: Page
    DashboardModel: Pages.Dashboard.Types.Model
    MovieListModel: Pages.Movies.Types.Model
    MovieDetailModel: Pages.MovieDetail.Types.Model
    FriendListModel: Pages.Friends.Types.Model
    FriendDetailModel: Pages.FriendDetail.Types.Model
    CatalogListModel: Pages.Catalogs.Types.Model
    CatalogDetailModel: Pages.CatalogDetail.Types.Model
    EventBrowserModel: Pages.EventBrowser.Types.Model
    SettingsModel: Pages.Settings.Types.Model
}

type Msg =
    | Url_changed of string list
    | Dashboard_msg of Pages.Dashboard.Types.Msg
    | Movie_list_msg of Pages.Movies.Types.Msg
    | Movie_detail_msg of Pages.MovieDetail.Types.Msg
    | Friend_list_msg of Pages.Friends.Types.Msg
    | Friend_detail_msg of Pages.FriendDetail.Types.Msg
    | Catalog_list_msg of Pages.Catalogs.Types.Msg
    | Catalog_detail_msg of Pages.CatalogDetail.Types.Msg
    | Event_browser_msg of Pages.EventBrowser.Types.Msg
    | Settings_msg of Pages.Settings.Types.Msg
