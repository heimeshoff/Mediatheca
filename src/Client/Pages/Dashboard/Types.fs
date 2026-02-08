module Mediatheca.Client.Pages.Dashboard.Types

type Model = {
    Placeholder: string
    MovieCount: int
    FriendCount: int
    RecentMovies: Mediatheca.Shared.MovieListItem list
    IsLoading: bool
}

type Msg =
    | NoOp
    | Movies_loaded of Mediatheca.Shared.MovieListItem list
    | Friends_loaded of Mediatheca.Shared.FriendListItem list
