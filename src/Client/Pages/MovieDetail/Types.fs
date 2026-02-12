module Mediatheca.Client.Pages.MovieDetail.Types

open Mediatheca.Shared

type SessionFormState = {
    Date: string
    SelectedFriends: Set<string>
}

type Model = {
    Slug: string
    Movie: MovieDetail option
    AllFriends: FriendListItem list
    IsLoading: bool
    ShowFriendPicker: FriendPickerKind option
    ShowRecordSession: bool
    SessionForm: SessionFormState
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
    | Open_record_session
    | Close_record_session
    | Session_date_changed of string
    | Toggle_session_friend of string
    | Submit_record_session
    | Session_recorded of Result<string, string>
    | Add_content_block of AddContentBlockRequest
    | Update_content_block of blockId: string * UpdateContentBlockRequest
    | Remove_content_block of blockId: string
    | Content_block_result of Result<unit, string>
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
    | Add_friend_and_watch_with of name: string
    | Friend_and_watch_with_result of Result<unit, string>
