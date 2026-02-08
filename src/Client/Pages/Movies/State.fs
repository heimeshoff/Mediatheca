module Mediatheca.Client.Pages.Movies.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Movies.Types
open Mediatheca.Client.Components

let init () : Model * Cmd<Msg> =
    { Movies = []
      SearchQuery = ""
      GenreFilter = None
      IsLoading = true
      TmdbSearch = None },
    Cmd.ofMsg Load_movies

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_movies ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getMovies () Movies_loaded

    | Movies_loaded movies ->
        { model with Movies = movies; IsLoading = false }, Cmd.none

    | Search_changed query ->
        { model with SearchQuery = query }, Cmd.none

    | Genre_filter_changed genre ->
        { model with GenreFilter = genre }, Cmd.none

    | Open_tmdb_search ->
        { model with TmdbSearch = Some (TmdbSearchModal.init ()) }, Cmd.none

    | Close_tmdb_search ->
        { model with TmdbSearch = None }, Cmd.none

    | Tmdb_search_msg childMsg ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            match childMsg with
            | TmdbSearchModal.Close ->
                { model with TmdbSearch = None }, Cmd.none
            | TmdbSearchModal.Query_changed q ->
                { model with TmdbSearch = Some { searchModel with Query = q } }, Cmd.none
            | TmdbSearchModal.Search ->
                { model with TmdbSearch = Some { searchModel with IsSearching = true; Error = None } },
                Cmd.OfAsync.either api.searchTmdb searchModel.Query Tmdb_search_completed (fun ex -> Tmdb_search_failed ex.Message)
            | TmdbSearchModal.Search_completed results ->
                { model with TmdbSearch = Some { searchModel with Results = results; IsSearching = false } }, Cmd.none
            | TmdbSearchModal.Search_failed err ->
                { model with TmdbSearch = Some { searchModel with Error = Some err; IsSearching = false } }, Cmd.none
            | TmdbSearchModal.Import tmdbId ->
                { model with TmdbSearch = Some { searchModel with IsImporting = true } },
                Cmd.OfAsync.either api.addMovie tmdbId Tmdb_import_completed (fun ex -> Tmdb_import_completed (Error ex.Message))
            | TmdbSearchModal.Import_completed _ ->
                model, Cmd.none

    | Tmdb_search_completed results ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            { model with TmdbSearch = Some { searchModel with Results = results; IsSearching = false } }, Cmd.none

    | Tmdb_search_failed err ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            { model with TmdbSearch = Some { searchModel with Error = Some err; IsSearching = false } }, Cmd.none

    | Tmdb_import_completed result ->
        match result with
        | Ok slug ->
            { model with TmdbSearch = None },
            Cmd.batch [
                Cmd.ofMsg Load_movies
                Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate ("movies", slug))
            ]
        | Error err ->
            match model.TmdbSearch with
            | None -> model, Cmd.none
            | Some searchModel ->
                { model with TmdbSearch = Some { searchModel with Error = Some err; IsImporting = false } }, Cmd.none
