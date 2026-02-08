module Mediatheca.Client.Pages.Movies.Types

open Mediatheca.Shared
open Mediatheca.Client.Components

type Model = {
    Movies: MovieListItem list
    SearchQuery: string
    GenreFilter: string option
    IsLoading: bool
    TmdbSearch: TmdbSearchModal.Model option
}

type Msg =
    | Load_movies
    | Movies_loaded of MovieListItem list
    | Search_changed of string
    | Genre_filter_changed of string option
    | Open_tmdb_search
    | Close_tmdb_search
    | Tmdb_search_msg of TmdbSearchModal.Msg
    | Tmdb_search_completed of TmdbSearchResult list
    | Tmdb_search_failed of string
    | Tmdb_import_completed of Result<string, string>
