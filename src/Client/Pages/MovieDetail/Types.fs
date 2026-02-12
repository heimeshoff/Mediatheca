module Mediatheca.Client.Pages.MovieDetail.Types

open Mediatheca.Shared

type Model = {
    Slug: string
    Movie: MovieDetail option
    AllFriends: FriendListItem list
    IsLoading: bool
    ShowFriendPicker: FriendPickerKind option
    EditingSessionDate: string option
    Error: string option
}

and FriendPickerKind =
    | Recommend_picker
    | Watch_with_picker
    | Session_friend_picker of sessionId: string

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
    | Record_quick_session
    | Quick_session_recorded of Result<string, string>
    | Edit_session_date of sessionId: string
    | Update_session_date of sessionId: string * date: string
    | Add_friend_to_session of sessionId: string * friendSlug: string
    | Remove_friend_from_session of sessionId: string * friendSlug: string
    | Remove_watch_session of sessionId: string
    | Add_new_friend_to_session of sessionId: string * name: string
    | New_friend_for_session_result of Result<unit, string>
    | Add_content_block of AddContentBlockRequest
    | Update_content_block of blockId: string * UpdateContentBlockRequest
    | Remove_content_block of blockId: string
    | Content_block_result of Result<unit, string>
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
    | Add_friend_and_watch_with of name: string
    | Friend_and_watch_with_result of Result<unit, string>
