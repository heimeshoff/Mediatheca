module Mediatheca.Client.Views

open Fable.Core
open Feliz
open Feliz.Router
open Mediatheca.Shared
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
    | Series_list ->
        Pages.Series.Views.view model.SeriesListModel (Series_list_msg >> dispatch)
    | Series_detail _ ->
        Pages.SeriesDetail.Views.view model.SeriesDetailModel (Series_detail_msg >> dispatch)
    | Game_list ->
        Pages.Games.Views.view model.GameListModel (Game_list_msg >> dispatch)
    | Game_detail _ ->
        Pages.GameDetail.Views.view model.GameDetailModel (Game_detail_msg >> dispatch)
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
    | Styleguide ->
        Pages.StyleGuide.Views.view model.StyleGuideModel (Styleguide_msg >> dispatch)
    | Not_found ->
        Pages.NotFound.Views.view ()

let private jellyfinSyncIndicator (model: Model) =
    if model.JellyfinSyncing then
        Html.div [
            prop.className "fixed top-3 right-3 z-50 flex items-center gap-2 rounded-lg px-3 py-1.5 text-xs text-base-content/70"
            prop.style [
                style.backgroundColor "oklch(0.25 0.015 264 / 0.65)"
                style.custom ("backdropFilter", "blur(24px) saturate(1.2)")
                style.custom ("border", "1px solid oklch(0.80 0 0 / 0.15)")
                style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08)")
            ]
            prop.children [
                Html.span [
                    prop.className "loading loading-spinner loading-xs text-primary"
                ]
                Html.span [ prop.text "Syncing Jellyfin..." ]
            ]
        ]
    else Html.none

let private jellyfinSyncToast (model: Model) (dispatch: Msg -> unit) =
    match model.ShowJellyfinSyncToast, model.JellyfinSyncResult with
    | true, Some result ->
        let parts =
            [ if result.MoviesAdded > 0 then sprintf "%d movie%s" result.MoviesAdded (if result.MoviesAdded > 1 then "s" else "")
              if result.EpisodesAdded > 0 then sprintf "%d episode%s" result.EpisodesAdded (if result.EpisodesAdded > 1 then "s" else "")
              if result.MoviesAutoAdded > 0 then sprintf "%d movie%s auto-added" result.MoviesAutoAdded (if result.MoviesAutoAdded > 1 then "s" else "")
              if result.SeriesAutoAdded > 0 then sprintf "%d series auto-added" result.SeriesAutoAdded ]
        let summary = parts |> String.concat ", "
        Html.div [
            prop.className "fixed bottom-20 lg:bottom-4 right-4 z-50 flex items-center gap-3 rounded-lg px-4 py-3 text-sm text-base-content shadow-xl max-w-sm"
            prop.style [
                style.backgroundColor "oklch(0.25 0.015 264 / 0.70)"
                style.custom ("backdropFilter", "blur(24px) saturate(1.2)")
                style.custom ("border", "1px solid oklch(0.80 0 0 / 0.15)")
                style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08)")
            ]
            prop.children [
                Html.div [
                    prop.className "flex-1"
                    prop.children [
                        Html.div [
                            prop.className "font-medium text-primary mb-0.5"
                            prop.text "Jellyfin synced"
                        ]
                        Html.div [
                            prop.className "text-base-content/70"
                            prop.text summary
                        ]
                    ]
                ]
                Html.button [
                    prop.className "btn btn-ghost btn-xs btn-circle"
                    prop.onClick (fun _ -> dispatch DismissJellyfinSyncToast)
                    prop.children [
                        Html.span [ prop.className "text-lg"; prop.text "\u00D7" ]
                    ]
                ]
            ]
        ]
    | _ -> Html.none

let view (model: Model) (dispatch: Msg -> unit) =
    React.router [
        router.onUrlChanged (Url_changed >> dispatch)
        router.children [
            KeyboardListener dispatch
            Layout.view model.CurrentPage (pageContent model dispatch)
            jellyfinSyncIndicator model
            jellyfinSyncToast model dispatch
            match model.SearchModal with
            | Some m -> SearchModal.view m (Search_modal_msg >> dispatch)
            | None -> ()
        ]
    ]
