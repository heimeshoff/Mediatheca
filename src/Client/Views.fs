module Mediatheca.Client.Views

open Feliz
open Feliz.Router
open Mediatheca.Client.Router
open Mediatheca.Client.Types
open Mediatheca.Client.Components

let private pageContent (model: Model) (dispatch: Msg -> unit) =
    match model.CurrentPage with
    | Dashboard ->
        Pages.Dashboard.Views.view model.DashboardModel (Dashboard_msg >> dispatch)
    | Movie_list ->
        Pages.Movies.Views.view model.MovieListModel (Movie_list_msg >> dispatch)
    | Movie_detail _ ->
        Pages.MovieDetail.Views.view model.MovieDetailModel (Movie_detail_msg >> dispatch)
    | Friend_list ->
        Pages.Friends.Views.view model.FriendListModel (Friend_list_msg >> dispatch)
    | Friend_detail _ ->
        Pages.FriendDetail.Views.view model.FriendDetailModel (Friend_detail_msg >> dispatch)
    | Settings ->
        Pages.Settings.Views.view model.SettingsModel (Settings_msg >> dispatch)
    | Not_found ->
        Pages.NotFound.Views.view ()

let view (model: Model) (dispatch: Msg -> unit) =
    React.router [
        router.onUrlChanged (Url_changed >> dispatch)
        router.children [
            Layout.view model.CurrentPage (pageContent model dispatch)
        ]
    ]
