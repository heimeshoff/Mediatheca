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
    | LoadMovies
    | MoviesLoaded of MovieListItem list
    | SearchChanged of string
    | GenreFilterChanged of string option
    | OpenTmdbSearch
    | CloseTmdbSearch
    | TmdbSearchMsg of TmdbSearchModal.Msg
    | TmdbSearchCompleted of TmdbSearchResult list
    | TmdbSearchFailed of string
    | TmdbImportCompleted of Result<string, string>
