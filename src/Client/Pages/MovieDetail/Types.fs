module Mediatheca.Client.Pages.MovieDetail.Types

open Mediatheca.Shared

type Model = {
    Slug: string
    Movie: MovieDetail option
    AllFriends: FriendListItem list
    AllCatalogs: CatalogListItem list
    MovieCatalogs: CatalogRef list
    ShowCatalogPicker: bool
    IsLoading: bool
    ShowFriendPicker: FriendPickerKind option
    EditingSessionDate: string option
    FullCredits: FullCreditsDto option
    TrailerKey: string option
    ShowTrailer: bool
    IsRatingOpen: bool
    IsFriendsMenuOpen: bool
    ConfirmingRemove: bool
    ShowEventHistory: bool
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
    | Confirm_remove_movie
    | Cancel_remove_movie
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
    | Change_content_block_type of blockId: string * blockType: string
    | Reorder_content_blocks of blockIds: string list
    | Upload_screenshot of data: byte array * filename: string * insertBefore: string option
    | Screenshot_uploaded of Result<string, string> * insertBefore: string option
    | Group_content_blocks of leftId: string * rightId: string
    | Ungroup_content_block of blockId: string
    | Content_block_result of Result<unit, string>
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
    | Add_friend_and_watch_with of name: string
    | Friend_and_watch_with_result of Result<unit, string>
    | Load_full_credits
    | Full_credits_loaded of Result<FullCreditsDto, string>
    | Trailer_loaded of string option
    | Open_trailer
    | Close_trailer
    | Toggle_rating_dropdown
    | Toggle_friends_menu
    | Close_friends_menu
    | Set_personal_rating of int
    | Personal_rating_result of Result<unit, string>
    | Set_in_focus of bool
    | In_focus_result of Result<unit, string>
    | Catalogs_loaded of CatalogListItem list
    | Movie_catalogs_loaded of CatalogRef list
    | Open_catalog_picker
    | Close_catalog_picker
    | Add_to_catalog of catalogSlug: string
    | Remove_from_catalog of catalogSlug: string * entryId: string
    | Create_catalog_and_add of name: string
    | Catalog_result of Result<unit, string>
    | Open_event_history
    | Close_event_history
