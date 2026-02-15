module Mediatheca.Client.Pages.Movies.Types

open Mediatheca.Shared

type Model = {
    Movies: MovieListItem list
    SearchQuery: string
    IsLoading: bool
}

type Msg =
    | Load_movies
    | Movies_loaded of MovieListItem list
    | Search_changed of string
    | Open_tmdb_search
