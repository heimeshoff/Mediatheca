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
    | Load_friends
    | Friends_loaded of FriendListItem list
    | Toggle_add_form
    | Add_form_name_changed of string
    | Submit_add_friend
    | Friend_added of Result<string, string>
    | Remove_friend of string
    | Friend_removed of Result<unit, string>
    | Upload_friend_image of slug: string * data: byte array * filename: string
    | Image_uploaded of Result<string, string>
