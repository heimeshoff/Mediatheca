module Mediatheca.Client.Pages.SeriesDetail.Types

open Mediatheca.Shared

type SeriesTab = Overview | Episodes

type FriendPickerKind =
    | Recommend_picker
    | Watch_with_picker
    | Session_friend_picker of rewatchId: string

type Model = {
    Slug: string
    Detail: SeriesDetail option
    IsLoading: bool
    ActiveTab: SeriesTab
    SelectedSeason: int
    SelectedRewatchId: string
    // Rating
    IsRatingOpen: bool
    // Social modals
    ShowFriendPicker: FriendPickerKind option
    Friends: FriendListItem list
    // Episode date editing
    EditingEpisodeDate: (int * int) option
    // Trailer
    TrailerKey: string option
    SeasonTrailerKeys: Map<int, string>
    ShowTrailer: string option
    // Remove
    ConfirmingRemove: bool
    Error: string option
}

type Msg =
    | Load_detail
    | Detail_loaded of SeriesDetail option
    | Set_tab of SeriesTab
    | Select_season of int
    | Select_rewatch of string
    // Episode progress
    | Toggle_episode_watched of seasonNumber: int * episodeNumber: int * isCurrentlyWatched: bool
    | Episode_toggled of Result<unit, string>
    | Mark_season_watched of seasonNumber: int
    | Season_marked of Result<unit, string>
    | Mark_season_unwatched of seasonNumber: int
    | Season_unmarked of Result<unit, string>
    // Episode date
    | Edit_episode_date of int * int
    | Update_episode_date of int * int * string
    | Cancel_edit_episode_date
    | Episode_date_updated of Result<unit, string>
    // Rewatch session management
    | Create_rewatch_session
    | Rewatch_session_created of Result<string, string>
    | Remove_rewatch_session of rewatchId: string
    | Rewatch_session_removed of Result<unit, string>
    // Session friends
    | Add_rewatch_friend of rewatchId: string * friendSlug: string
    | Remove_rewatch_friend of rewatchId: string * friendSlug: string
    | Add_friend_and_add_to_session of rewatchId: string * name: string
    | Rewatch_friend_result of Result<unit, string>
    // Rating
    | Toggle_rating_dropdown
    | Set_rating of int
    | Rating_set of Result<unit, string>
    // Social
    | Open_friend_picker of FriendPickerKind
    | Close_friend_picker
    | Friends_loaded of FriendListItem list
    | Add_recommendation of string
    | Remove_recommendation of string
    | Add_watch_with of string
    | Remove_watch_with of string
    | Add_friend_and_recommend of name: string
    | Add_friend_and_watch_with of name: string
    | Social_updated of Result<unit, string>
    // Content Blocks
    | Add_content_block of AddContentBlockRequest
    | Update_content_block of blockId: string * UpdateContentBlockRequest
    | Remove_content_block of blockId: string
    | Change_content_block_type of blockId: string * blockType: string
    | Reorder_content_blocks of blockIds: string list
    | Content_block_result of Result<unit, string>
    // Trailer
    | Trailer_loaded of string option
    | Season_trailer_loaded of seasonNumber: int * key: string option
    | Open_trailer of key: string
    | Close_trailer
    // Remove
    | Confirm_remove_series
    | Cancel_remove_series
    | Remove_series
    | Series_removed of Result<unit, string>
