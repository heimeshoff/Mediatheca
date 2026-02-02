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
    | LoadFriend of string
    | FriendLoaded of FriendDetail option
    | StartEditing
    | CancelEditing
    | EditNameChanged of string
    | SubmitUpdate
    | UpdateResult of Result<unit, string>
    | RemoveFriend
    | RemoveResult of Result<unit, string>
