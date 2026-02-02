module Mediatheca.Client.Views

open Feliz
open Feliz.Router
open Mediatheca.Client.Router
open Mediatheca.Client.Types
open Mediatheca.Client.Components

let private pageContent (model: Model) (dispatch: Msg -> unit) =
    match model.CurrentPage with
    | Dashboard ->
        Pages.Dashboard.Views.view model.DashboardModel (DashboardMsg >> dispatch)
    | MovieList ->
        Pages.Movies.Views.view model.MovieListModel (MovieListMsg >> dispatch)
    | MovieDetail _ ->
        Pages.MovieDetail.Views.view model.MovieDetailModel (MovieDetailMsg >> dispatch)
    | FriendList ->
        Pages.Friends.Views.view model.FriendListModel (FriendListMsg >> dispatch)
    | FriendDetail _ ->
        Pages.FriendDetail.Views.view model.FriendDetailModel (FriendDetailMsg >> dispatch)
    | Settings ->
        Pages.Settings.Views.view model.SettingsModel (SettingsMsg >> dispatch)
    | NotFound ->
        Pages.NotFound.Views.view ()

let view (model: Model) (dispatch: Msg -> unit) =
    React.router [
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [
            Layout.view model.CurrentPage (pageContent model dispatch)
        ]
    ]
