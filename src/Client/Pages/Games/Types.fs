module Mediatheca.Client.Pages.Games.Types

open Mediatheca.Shared

type Model = {
    Games: GameListItem list
    SearchQuery: string
    StatusFilter: GameStatus option
    IsLoading: bool
}

type Msg =
    | Load_games
    | Games_loaded of GameListItem list
    | Search_changed of string
    | Status_filter_changed of GameStatus option
    | Open_search_modal
