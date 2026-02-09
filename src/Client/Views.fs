module Mediatheca.Client.Views

open Fable.Core
open Feliz
open Feliz.Router
open Mediatheca.Client.Router
open Mediatheca.Client.Types
open Mediatheca.Client.Components

[<ReactComponent>]
let private KeyboardListener (dispatch: Msg -> unit) =
    React.useEffectOnce (fun () ->
        let handler (e: Browser.Types.Event) =
            let ke = e :?> Browser.Types.KeyboardEvent
            if (ke.ctrlKey || ke.metaKey) && ke.key = "k" then
                ke.preventDefault()
                dispatch Open_search_modal
        Browser.Dom.document.addEventListener("keydown", handler)
        React.createDisposable (fun () ->
            Browser.Dom.document.removeEventListener("keydown", handler)
        )
    )
    Html.none

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
    | Catalog_list ->
        Pages.Catalogs.Views.view model.CatalogListModel (Catalog_list_msg >> dispatch)
    | Catalog_detail _ ->
        Pages.CatalogDetail.Views.view model.CatalogDetailModel (Catalog_detail_msg >> dispatch)
    | Event_browser ->
        Pages.EventBrowser.Views.view model.EventBrowserModel (Event_browser_msg >> dispatch)
    | Settings ->
        Pages.Settings.Views.view model.SettingsModel (Settings_msg >> dispatch)
    | Not_found ->
        Pages.NotFound.Views.view ()

let view (model: Model) (dispatch: Msg -> unit) =
    React.router [
        router.onUrlChanged (Url_changed >> dispatch)
        router.children [
            KeyboardListener dispatch
            Layout.view model.CurrentPage (pageContent model dispatch)
            match model.SearchModal with
            | Some m -> SearchModal.view m (Search_modal_msg >> dispatch)
            | None -> ()
        ]
    ]
