module Mediatheca.Client.Pages.GameDetail.Types

open Mediatheca.Shared

type GameTab = Overview | Journal

type FriendPickerKind =
    | Recommend_picker
    | Play_with_picker
    | Played_with_picker

type ImagePickerKind = Cover_picker | Backdrop_picker

type Model = {
    Slug: string
    Game: GameDetail option
    AllFriends: FriendListItem list
    AllCatalogs: CatalogListItem list
    GameCatalogs: CatalogRef list
    ShowCatalogPicker: bool
    IsLoading: bool
    ShowFriendPicker: FriendPickerKind option
    IsRatingOpen: bool
    IsStatusOpen: bool
    ShowPlayModePicker: bool
    AllPlayModes: string list
    IsDescriptionExpanded: bool
    IsFriendsMenuOpen: bool
    ConfirmingRemove: bool
    ShowImagePicker: ImagePickerKind option
    ImageCandidates: GameImageCandidate list
    IsLoadingImages: bool
    IsSelectingImage: bool
    ImageVersion: int
    ActiveTab: GameTab
    Error: string option
}

type Msg =
    | Set_tab of GameTab
    | Upload_screenshot of data: byte array * filename: string * insertBefore: string option
    | Screenshot_uploaded of Result<string, string> * insertBefore: string option
    | Load_game of string
    | Game_loaded of GameDetail option
    | Friends_loaded of FriendListItem list
    | Recommend_friend of friendSlug: string
    | Remove_recommendation of friendSlug: string
    | Want_to_play_with of friendSlug: string
    | Remove_want_to_play_with of friendSlug: string
    | Add_played_with of friendSlug: string
    | Remove_played_with of friendSlug: string
    | Add_family_owner of friendSlug: string
    | Remove_family_owner of friendSlug: string
    | Toggle_ownership
    | Command_result of Result<unit, string>
    | Open_friend_picker of FriendPickerKind
    | Close_friend_picker
    | Set_game_status of GameStatus
    | Toggle_status_dropdown
    | Toggle_rating_dropdown
    | Set_personal_rating of int
    | Personal_rating_result of Result<unit, string>
    | Toggle_friends_menu
    | Close_friends_menu
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
    | Add_friend_and_play_with of name: string
    | Friend_and_play_with_result of Result<unit, string>
    | Add_friend_and_played_with of name: string
    | Friend_and_played_with_result of Result<unit, string>
    | Add_content_block of AddContentBlockRequest
    | Update_content_block of blockId: string * UpdateContentBlockRequest
    | Remove_content_block of blockId: string
    | Change_content_block_type of blockId: string * blockType: string
    | Reorder_content_blocks of blockIds: string list
    | Group_content_blocks of leftId: string * rightId: string
    | Ungroup_content_block of blockId: string
    | Content_block_result of Result<unit, string>
    | Catalogs_loaded of CatalogListItem list
    | Game_catalogs_loaded of CatalogRef list
    | Open_catalog_picker
    | Close_catalog_picker
    | Add_to_catalog of catalogSlug: string
    | Remove_from_catalog of catalogSlug: string * entryId: string
    | Create_catalog_and_add of name: string
    | Catalog_result of Result<unit, string>
    | Open_image_picker of ImagePickerKind
    | Close_image_picker
    | Image_candidates_loaded of GameImageCandidate list
    | Select_image of url: string
    | Image_selected of Result<unit, string>
    | Toggle_description_expanded
    | Add_play_mode of string
    | Remove_play_mode of string
    | Toggle_play_mode_picker
    | Play_modes_loaded of string list
    | Confirm_remove_game
    | Cancel_remove_game
    | Remove_game
    | Game_removed of Result<unit, string>
