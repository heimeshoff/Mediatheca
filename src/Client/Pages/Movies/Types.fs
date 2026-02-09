module Mediatheca.Client.Pages.Movies.Types

open Mediatheca.Shared

type Model = {
    Movies: MovieListItem list
    SearchQuery: string
    GenreFilter: string option
    IsLoading: bool
}

type Msg =
    | Load_movies
    | Movies_loaded of MovieListItem list
    | Search_changed of string
    | Genre_filter_changed of string option
    | Open_tmdb_search
