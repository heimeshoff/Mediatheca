module Mediatheca.Client.Pages.FriendDetail.Types

open Mediatheca.Shared

type EditForm = {
    Name: string
    ImageRef: string option
}

type Model = {
    Slug: string
    Friend: FriendDetail option
    IsLoading: bool
    IsEditing: bool
    EditForm: EditForm
    Error: string option
}

type Msg =
    | Load_friend of string
    | Friend_loaded of FriendDetail option
    | Start_editing
    | Cancel_editing
    | Edit_name_changed of string
    | Submit_update
    | Update_result of Result<unit, string>
    | Remove_friend
    | Remove_result of Result<unit, string>
    | Upload_friend_image of data: byte array * filename: string
    | Image_uploaded of Result<string, string>
