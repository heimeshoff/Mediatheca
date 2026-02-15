module Mediatheca.Client.Pages.Series.Types

open Mediatheca.Shared

type Model = {
    Series: SeriesListItem list
    SearchQuery: string
    IsLoading: bool
}

type Msg =
    | Load_series
    | Series_loaded of SeriesListItem list
    | Search_changed of string
    | Open_tmdb_search
