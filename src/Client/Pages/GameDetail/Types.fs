module Mediatheca.Client.Pages.GameDetail.Types

open Mediatheca.Shared

type FriendPickerKind =
    | Recommend_picker
    | Play_with_picker
    | Played_with_picker
    | Family_owner_picker

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
    ShowStoreInput: bool
    HltbInput: string
    IsEditingHltb: bool
    ConfirmingRemove: bool
    Error: string option
}

type Msg =
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
    | Command_result of Result<unit, string>
    | Open_friend_picker of FriendPickerKind
    | Close_friend_picker
    | Set_game_status of GameStatus
    | Toggle_status_dropdown
    | Toggle_rating_dropdown
    | Set_personal_rating of int
    | Personal_rating_result of Result<unit, string>
    | Add_store of string
    | Remove_store of string
    | Toggle_store_input
    | Set_hltb_hours of string
    | Start_editing_hltb
    | Save_hltb
    | Cancel_editing_hltb
    | Hltb_result of Result<unit, string>
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
    | Add_friend_and_play_with of name: string
    | Friend_and_play_with_result of Result<unit, string>
    | Add_friend_and_family_owner of name: string
    | Friend_and_family_owner_result of Result<unit, string>
    | Add_friend_and_played_with of name: string
    | Friend_and_played_with_result of Result<unit, string>
    | Add_content_block of AddContentBlockRequest
    | Update_content_block of blockId: string * UpdateContentBlockRequest
    | Remove_content_block of blockId: string
    | Change_content_block_type of blockId: string * blockType: string
    | Reorder_content_blocks of blockIds: string list
    | Content_block_result of Result<unit, string>
    | Catalogs_loaded of CatalogListItem list
    | Game_catalogs_loaded of CatalogRef list
    | Open_catalog_picker
    | Close_catalog_picker
    | Add_to_catalog of catalogSlug: string
    | Remove_from_catalog of catalogSlug: string * entryId: string
    | Create_catalog_and_add of name: string
    | Catalog_result of Result<unit, string>
    | Confirm_remove_game
    | Cancel_remove_game
    | Remove_game
    | Game_removed of Result<unit, string>
