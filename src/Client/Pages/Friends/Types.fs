module Mediatheca.Client.Pages.Friends.Types

open Mediatheca.Shared

type AddFriendForm = {
    Name: string
}

type Model = {
    Friends: FriendListItem list
    IsLoading: bool
    ShowAddForm: bool
    AddForm: AddFriendForm
    Error: string option
}

type Msg =
    | LoadFriends
    | FriendsLoaded of FriendListItem list
    | ToggleAddForm
    | AddFormNameChanged of string
    | SubmitAddFriend
    | FriendAdded of Result<string, string>
    | RemoveFriend of string
    | FriendRemoved of Result<unit, string>
