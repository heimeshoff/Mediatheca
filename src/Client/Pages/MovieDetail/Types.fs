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
    | RecommendPicker
    | WatchWithPicker

type Msg =
    | LoadMovie of string
    | MovieLoaded of MovieDetail option
    | FriendsLoaded of FriendListItem list
    | RecommendFriend of friendSlug: string
    | RemoveRecommendation of friendSlug: string
    | WantToWatchWith of friendSlug: string
    | RemoveWantToWatchWith of friendSlug: string
    | CommandResult of Result<unit, string>
    | RemoveMovie
    | MovieRemoved of Result<unit, string>
    | OpenFriendPicker of FriendPickerKind
    | CloseFriendPicker
