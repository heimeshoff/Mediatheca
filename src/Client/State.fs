module Mediatheca.Client.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Router
open Mediatheca.Client.Types
open Mediatheca.Client.Components

let private debounceCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        Fable.Core.JS.setTimeout (fun () -> dispatch msg) ms |> ignore
    )

let init (api: IMediathecaApi) () : Model * Cmd<Msg> =
    let dashboardModel, dashboardCmd = Pages.Dashboard.State.init ()
    let movieListModel, movieListCmd = Pages.Movies.State.init ()
    let movieDetailModel, movieDetailCmd = Pages.MovieDetail.State.init ""
    let seriesListModel, seriesListCmd = Pages.Series.State.init ()
    let seriesDetailModel, seriesDetailCmd = Pages.SeriesDetail.State.init ""
    let friendListModel, friendListCmd = Pages.Friends.State.init ()
    let friendDetailModel, friendDetailCmd = Pages.FriendDetail.State.init ""
    let catalogListModel, catalogListCmd = Pages.Catalogs.State.init ()
    let catalogDetailModel, catalogDetailCmd = Pages.CatalogDetail.State.init ""
    let eventBrowserModel, eventBrowserCmd = Pages.EventBrowser.State.init ()
    let settingsModel, settingsCmd = Pages.Settings.State.init ()
    let styleGuideModel, styleGuideCmd = Pages.StyleGuide.State.init ()

    let model = {
        CurrentPage = Dashboard
        DashboardModel = dashboardModel
        MovieListModel = movieListModel
        MovieDetailModel = movieDetailModel
        SeriesListModel = seriesListModel
        SeriesDetailModel = seriesDetailModel
        FriendListModel = friendListModel
        FriendDetailModel = friendDetailModel
        CatalogListModel = catalogListModel
        CatalogDetailModel = catalogDetailModel
        EventBrowserModel = eventBrowserModel
        SettingsModel = settingsModel
        StyleGuideModel = styleGuideModel
        SearchModal = None
    }

    let cmd = Cmd.batch [
        Cmd.map Dashboard_msg dashboardCmd
        Cmd.OfAsync.perform api.getDashboardStats () (fun stats -> Dashboard_msg (Pages.Dashboard.Types.Stats_loaded stats))
        Cmd.OfAsync.perform api.getRecentActivity 10 (fun activity -> Dashboard_msg (Pages.Dashboard.Types.Activity_loaded activity))
        Cmd.OfAsync.perform api.getMovies () (fun movies -> Dashboard_msg (Pages.Dashboard.Types.Movies_loaded movies))
        Cmd.map Movie_list_msg movieListCmd
        Cmd.map Series_list_msg seriesListCmd
        Cmd.map Settings_msg settingsCmd
    ]

    model, cmd

let private updateSearchModal (api: IMediathecaApi) (childMsg: SearchModal.Msg) (model: Model) : Model * Cmd<Msg> =
    match model.SearchModal with
    | None -> model, Cmd.none
    | Some searchModel ->
        match childMsg with
        | SearchModal.Close ->
            { model with SearchModal = None }, Cmd.none

        | SearchModal.Query_changed q ->
            let newVersion = searchModel.SearchVersion + 1
            let updatedSearch = {
                searchModel with
                    Query = q
                    SearchVersion = newVersion
                    IsSearchingTmdb = q <> ""
                    TmdbResults = if q = "" then [] else searchModel.TmdbResults
                    Error = None
            }
            let cmds =
                if q = "" then Cmd.none
                else debounceCmd 300 (Search_modal_msg (SearchModal.Debounce_tmdb_expired newVersion))
            { model with SearchModal = Some updatedSearch }, cmds

        | SearchModal.Debounce_tmdb_expired version ->
            if version <> searchModel.SearchVersion || searchModel.Query = "" then
                model, Cmd.none
            else
                let searchBoth = async {
                    let! movieResults = api.searchTmdb searchModel.Query
                    let! seriesResults = api.searchTvSeries searchModel.Query
                    return movieResults @ seriesResults
                }
                model,
                Cmd.OfAsync.either
                    (fun () -> searchBoth) ()
                    (fun results -> Search_modal_msg (SearchModal.Tmdb_search_completed results))
                    (fun ex -> Search_modal_msg (SearchModal.Tmdb_search_failed ex.Message))

        | SearchModal.Tmdb_search_completed results ->
            { model with SearchModal = Some { searchModel with TmdbResults = results; IsSearchingTmdb = false } }, Cmd.none

        | SearchModal.Tmdb_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingTmdb = false; Error = Some err } }, Cmd.none

        | SearchModal.Import (tmdbId, mediaType) ->
            let importCmd =
                match mediaType with
                | MediaType.Movie ->
                    Cmd.OfAsync.either
                        api.addMovie tmdbId
                        (fun result -> Search_modal_msg (SearchModal.Import_completed (result |> Result.map (fun slug -> slug, MediaType.Movie))))
                        (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                | MediaType.Series ->
                    Cmd.OfAsync.either
                        api.addSeries tmdbId
                        (fun result -> Search_modal_msg (SearchModal.Import_completed (result |> Result.map (fun slug -> slug, MediaType.Series))))
                        (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None } }, importCmd

        | SearchModal.Import_completed result ->
            match result with
            | Ok (slug, mediaType) ->
                let reloadCmd, navSegments =
                    match mediaType with
                    | MediaType.Movie ->
                        Cmd.ofMsg (Movie_list_msg Pages.Movies.Types.Load_movies), ("movies", slug)
                    | MediaType.Series ->
                        Cmd.ofMsg (Series_list_msg Pages.Series.Types.Load_series), ("series", slug)
                { model with SearchModal = None },
                Cmd.batch [
                    reloadCmd
                    Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate (fst navSegments, snd navSegments))
                ]
            | Error err ->
                { model with SearchModal = Some { searchModel with Error = Some err; IsImporting = false } }, Cmd.none

        | SearchModal.Navigate_to (slug, mediaType) ->
            let navSegments =
                match mediaType with
                | MediaType.Movie -> ("movies", slug)
                | MediaType.Series -> ("series", slug)
            { model with SearchModal = None },
            Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate (fst navSegments, snd navSegments))

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Url_changed segments ->
        let page = Route.parseUrl segments
        let model = { model with CurrentPage = page }
        match page with
        | Movie_list ->
            let childModel, childCmd = Pages.Movies.State.init ()
            { model with MovieListModel = childModel },
            Cmd.map Movie_list_msg childCmd
        | Movie_detail slug ->
            let childModel, childCmd = Pages.MovieDetail.State.init slug
            { model with MovieDetailModel = childModel },
            Cmd.map Movie_detail_msg childCmd
        | Series_list ->
            let childModel, childCmd = Pages.Series.State.init ()
            { model with SeriesListModel = childModel },
            Cmd.map Series_list_msg childCmd
        | Series_detail slug ->
            let childModel, childCmd = Pages.SeriesDetail.State.init slug
            { model with SeriesDetailModel = childModel },
            Cmd.map Series_detail_msg childCmd
        | Friend_list ->
            let childModel, childCmd = Pages.Friends.State.init ()
            { model with FriendListModel = childModel },
            Cmd.map Friend_list_msg childCmd
        | Friend_detail slug ->
            let childModel, childCmd = Pages.FriendDetail.State.init slug
            { model with FriendDetailModel = childModel },
            Cmd.map Friend_detail_msg childCmd
        | Catalog_list ->
            let childModel, childCmd = Pages.Catalogs.State.init ()
            { model with CatalogListModel = childModel },
            Cmd.map Catalog_list_msg childCmd
        | Catalog_detail slug ->
            let childModel, childCmd = Pages.CatalogDetail.State.init slug
            { model with CatalogDetailModel = childModel },
            Cmd.map Catalog_detail_msg childCmd
        | Event_browser ->
            let childModel, childCmd = Pages.EventBrowser.State.init ()
            { model with EventBrowserModel = childModel },
            Cmd.map Event_browser_msg childCmd
        | Settings ->
            let childModel, childCmd = Pages.Settings.State.init ()
            { model with SettingsModel = childModel },
            Cmd.map Settings_msg childCmd
        | Styleguide ->
            let childModel, childCmd = Pages.StyleGuide.State.init ()
            { model with StyleGuideModel = childModel },
            Cmd.map Styleguide_msg childCmd
        | Dashboard ->
            let childModel, childCmd = Pages.Dashboard.State.init ()
            { model with DashboardModel = childModel },
            Cmd.batch [
                Cmd.map Dashboard_msg childCmd
                Cmd.OfAsync.perform api.getDashboardStats () (fun stats -> Dashboard_msg (Pages.Dashboard.Types.Stats_loaded stats))
                Cmd.OfAsync.perform api.getRecentActivity 10 (fun activity -> Dashboard_msg (Pages.Dashboard.Types.Activity_loaded activity))
                Cmd.OfAsync.perform api.getMovies () (fun movies -> Dashboard_msg (Pages.Dashboard.Types.Movies_loaded movies))
            ]
        | _ -> model, Cmd.none

    | Open_search_modal ->
        { model with SearchModal = Some (SearchModal.init model.MovieListModel.Movies model.SeriesListModel.Series) }, Cmd.none

    | Search_modal_msg childMsg ->
        updateSearchModal api childMsg model

    | Dashboard_msg childMsg ->
        let childModel, childCmd = Pages.Dashboard.State.update api childMsg model.DashboardModel
        { model with DashboardModel = childModel }, Cmd.map Dashboard_msg childCmd

    | Movie_list_msg childMsg ->
        match childMsg with
        | Pages.Movies.Types.Open_tmdb_search ->
            { model with SearchModal = Some (SearchModal.init model.MovieListModel.Movies model.SeriesListModel.Series) }, Cmd.none
        | _ ->
            let childModel, childCmd = Pages.Movies.State.update api childMsg model.MovieListModel
            { model with MovieListModel = childModel }, Cmd.map Movie_list_msg childCmd

    | Movie_detail_msg childMsg ->
        let childModel, childCmd = Pages.MovieDetail.State.update api childMsg model.MovieDetailModel
        { model with MovieDetailModel = childModel }, Cmd.map Movie_detail_msg childCmd

    | Series_list_msg childMsg ->
        match childMsg with
        | Pages.Series.Types.Open_tmdb_search ->
            { model with SearchModal = Some (SearchModal.init model.MovieListModel.Movies model.SeriesListModel.Series) }, Cmd.none
        | _ ->
            let childModel, childCmd = Pages.Series.State.update api childMsg model.SeriesListModel
            { model with SeriesListModel = childModel }, Cmd.map Series_list_msg childCmd

    | Series_detail_msg childMsg ->
        let childModel, childCmd = Pages.SeriesDetail.State.update api childMsg model.SeriesDetailModel
        { model with SeriesDetailModel = childModel }, Cmd.map Series_detail_msg childCmd

    | Friend_list_msg childMsg ->
        let childModel, childCmd = Pages.Friends.State.update api childMsg model.FriendListModel
        { model with FriendListModel = childModel }, Cmd.map Friend_list_msg childCmd

    | Friend_detail_msg childMsg ->
        let childModel, childCmd = Pages.FriendDetail.State.update api childMsg model.FriendDetailModel
        { model with FriendDetailModel = childModel }, Cmd.map Friend_detail_msg childCmd

    | Catalog_list_msg childMsg ->
        let childModel, childCmd = Pages.Catalogs.State.update api childMsg model.CatalogListModel
        { model with CatalogListModel = childModel }, Cmd.map Catalog_list_msg childCmd

    | Catalog_detail_msg childMsg ->
        let childModel, childCmd = Pages.CatalogDetail.State.update api childMsg model.CatalogDetailModel
        { model with CatalogDetailModel = childModel }, Cmd.map Catalog_detail_msg childCmd

    | Event_browser_msg childMsg ->
        let childModel, childCmd = Pages.EventBrowser.State.update api childMsg model.EventBrowserModel
        { model with EventBrowserModel = childModel }, Cmd.map Event_browser_msg childCmd

    | Settings_msg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update api childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map Settings_msg childCmd

    | Styleguide_msg childMsg ->
        let childModel, childCmd = Pages.StyleGuide.State.update childMsg model.StyleGuideModel
        { model with StyleGuideModel = childModel }, Cmd.map Styleguide_msg childCmd
