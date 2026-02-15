module Mediatheca.Client.Types

open Mediatheca.Client.Router
open Mediatheca.Client.Components

type Model = {
    CurrentPage: Page
    DashboardModel: Pages.Dashboard.Types.Model
    MovieListModel: Pages.Movies.Types.Model
    MovieDetailModel: Pages.MovieDetail.Types.Model
    SeriesListModel: Pages.Series.Types.Model
    SeriesDetailModel: Pages.SeriesDetail.Types.Model
    GameListModel: Pages.Games.Types.Model
    GameDetailModel: Pages.GameDetail.Types.Model
    FriendListModel: Pages.Friends.Types.Model
    FriendDetailModel: Pages.FriendDetail.Types.Model
    CatalogListModel: Pages.Catalogs.Types.Model
    CatalogDetailModel: Pages.CatalogDetail.Types.Model
    EventBrowserModel: Pages.EventBrowser.Types.Model
    SettingsModel: Pages.Settings.Types.Model
    StyleGuideModel: Pages.StyleGuide.Types.Model
    SearchModal: SearchModal.Model option
}

type Msg =
    | Url_changed of string list
    | Open_search_modal
    | Search_modal_msg of SearchModal.Msg
    | Dashboard_msg of Pages.Dashboard.Types.Msg
    | Movie_list_msg of Pages.Movies.Types.Msg
    | Movie_detail_msg of Pages.MovieDetail.Types.Msg
    | Series_list_msg of Pages.Series.Types.Msg
    | Series_detail_msg of Pages.SeriesDetail.Types.Msg
    | Game_list_msg of Pages.Games.Types.Msg
    | Game_detail_msg of Pages.GameDetail.Types.Msg
    | Friend_list_msg of Pages.Friends.Types.Msg
    | Friend_detail_msg of Pages.FriendDetail.Types.Msg
    | Catalog_list_msg of Pages.Catalogs.Types.Msg
    | Catalog_detail_msg of Pages.CatalogDetail.Types.Msg
    | Event_browser_msg of Pages.EventBrowser.Types.Msg
    | Settings_msg of Pages.Settings.Types.Msg
    | Styleguide_msg of Pages.StyleGuide.Types.Msg
