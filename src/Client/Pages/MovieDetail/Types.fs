module Mediatheca.Client.Pages.MovieDetail.Types

open Mediatheca.Shared

type Model = {
    Slug: string
    Movie: MovieDetail option
    AllFriends: FriendListItem list
    IsLoading: bool
    ShowFriendPicker: FriendPickerKind option
    Error: string option
}

and FriendPickerKind =
    | Recommend_picker
    | Watch_with_picker

type Msg =
    | Load_movie of string
    | Movie_loaded of MovieDetail option
    | Friends_loaded of FriendListItem list
    | Recommend_friend of friendSlug: string
    | Remove_recommendation of friendSlug: string
    | Want_to_watch_with of friendSlug: string
    | Remove_want_to_watch_with of friendSlug: string
    | Command_result of Result<unit, string>
    | Remove_movie
    | Movie_removed of Result<unit, string>
    | Open_friend_picker of FriendPickerKind
    | Close_friend_picker
