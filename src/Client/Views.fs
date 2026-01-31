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
    | Movies ->
        Pages.Movies.Views.view model.MoviesModel (MoviesMsg >> dispatch)
    | Friends ->
        Pages.Friends.Views.view model.FriendsModel (FriendsMsg >> dispatch)
    | Catalog ->
        Pages.Catalog.Views.view model.CatalogModel (CatalogMsg >> dispatch)
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
