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
    Cmd.ofMsg LoadMovies

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadMovies ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getMovies () MoviesLoaded

    | MoviesLoaded movies ->
        { model with Movies = movies; IsLoading = false }, Cmd.none

    | SearchChanged query ->
        { model with SearchQuery = query }, Cmd.none

    | GenreFilterChanged genre ->
        { model with GenreFilter = genre }, Cmd.none

    | OpenTmdbSearch ->
        { model with TmdbSearch = Some (TmdbSearchModal.init ()) }, Cmd.none

    | CloseTmdbSearch ->
        { model with TmdbSearch = None }, Cmd.none

    | TmdbSearchMsg childMsg ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            match childMsg with
            | TmdbSearchModal.Close ->
                { model with TmdbSearch = None }, Cmd.none
            | TmdbSearchModal.QueryChanged q ->
                { model with TmdbSearch = Some { searchModel with Query = q } }, Cmd.none
            | TmdbSearchModal.Search ->
                { model with TmdbSearch = Some { searchModel with IsSearching = true; Error = None } },
                Cmd.OfAsync.either api.searchTmdb searchModel.Query TmdbSearchCompleted (fun ex -> TmdbSearchFailed ex.Message)
            | TmdbSearchModal.SearchCompleted results ->
                { model with TmdbSearch = Some { searchModel with Results = results; IsSearching = false } }, Cmd.none
            | TmdbSearchModal.SearchFailed err ->
                { model with TmdbSearch = Some { searchModel with Error = Some err; IsSearching = false } }, Cmd.none
            | TmdbSearchModal.Import tmdbId ->
                { model with TmdbSearch = Some { searchModel with IsImporting = true } },
                Cmd.OfAsync.either api.addMovie tmdbId TmdbImportCompleted (fun ex -> TmdbImportCompleted (Error ex.Message))
            | TmdbSearchModal.ImportCompleted _ ->
                model, Cmd.none

    | TmdbSearchCompleted results ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            { model with TmdbSearch = Some { searchModel with Results = results; IsSearching = false } }, Cmd.none

    | TmdbSearchFailed err ->
        match model.TmdbSearch with
        | None -> model, Cmd.none
        | Some searchModel ->
            { model with TmdbSearch = Some { searchModel with Error = Some err; IsSearching = false } }, Cmd.none

    | TmdbImportCompleted result ->
        match result with
        | Ok slug ->
            { model with TmdbSearch = None },
            Cmd.batch [
                Cmd.ofMsg LoadMovies
                Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate ("movies", slug))
            ]
        | Error err ->
            match model.TmdbSearch with
            | None -> model, Cmd.none
            | Some searchModel ->
                { model with TmdbSearch = Some { searchModel with Error = Some err; IsImporting = false } }, Cmd.none
