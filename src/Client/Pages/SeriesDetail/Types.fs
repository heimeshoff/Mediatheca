module Mediatheca.Client.Pages.SeriesDetail.Types

open Mediatheca.Shared

type SeriesTab = Overview | Episodes

type FriendPickerKind =
    | Recommend_picker
    | Watch_with_picker

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
    // Remove
    | Confirm_remove_series
    | Cancel_remove_series
    | Remove_series
    | Series_removed of Result<unit, string>
