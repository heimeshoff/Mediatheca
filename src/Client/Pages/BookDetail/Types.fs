module Mediatheca.Client.Pages.BookDetail.Types

open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.Progress

/// Which of the "Update progress" popover's two inputs is active.
type ProgressTab =
    | ByPercentTab
    | ByPageTab

/// The popover's local draft — raw text fields (parsed only on submit, via
/// `Progress.buildProgressRequest`) plus whatever validation message the
/// last attempt produced.
type ProgressDraft = {
    Tab: ProgressTab
    PercentText: string
    PageText: string
    TotalText: string
    DateText: string
    Error: string option
}

type Model = {
    Slug: string
    Book: BookDetail option
    AllFriends: FriendListItem list
    AllCatalogs: CatalogListItem list
    BookCatalogs: CatalogRef list
    ShowCatalogPicker: bool
    IsLoading: bool
    IsRatingOpen: bool
    IsStatusOpen: bool
    ShowFriendPicker: bool
    ConfirmingRemove: bool
    ShowEventHistory: bool
    ShowProgressPopover: bool
    ProgressDraft: ProgressDraft
    // Progress-history rows are keyed by their natural key (ADR-0076 §2) —
    // there is no synthetic id to carry instead.
    ConfirmingRemoveObservation: (string * ProgressSource) option
    ShowFormatPicker: bool
    Error: string option
}

type Msg =
    | Load_book of string
    | Book_loaded of BookDetail option
    | Friends_loaded of FriendListItem list
    | Catalogs_loaded of CatalogListItem list
    | Book_catalogs_loaded of CatalogRef list
    | Open_catalog_picker
    | Close_catalog_picker
    | Add_to_catalog of catalogSlug: string
    | Remove_from_catalog of catalogSlug: string * entryId: string
    | Create_catalog_and_add of name: string
    | Catalog_result of Result<unit, string>
    | Command_result of Result<unit, string>
    | Toggle_rating_dropdown
    | Set_personal_rating of int
    | Personal_rating_result of Result<unit, string>
    | Toggle_status_dropdown
    | Set_book_status of BookStatus
    | Status_result of Result<unit, string>
    | Open_progress_popover
    | Close_progress_popover
    | Set_progress_tab of ProgressTab
    | Set_progress_percent_text of string
    | Set_progress_page_text of string
    | Set_progress_total_text of string
    | Set_progress_date_text of string
    | Submit_progress
    | Progress_result of Result<unit, string>
    | Confirm_remove_observation of observedOn: string * source: ProgressSource
    | Cancel_remove_observation
    | Remove_observation of observedOn: string * source: ProgressSource
    | Observation_removed of Result<unit, string>
    | Open_friend_picker
    | Close_friend_picker
    | Recommend_friend of friendSlug: string
    | Remove_recommendation of friendSlug: string
    | Add_friend_and_recommend of name: string
    | Friend_and_recommend_result of Result<unit, string>
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
    | Open_event_history
    | Close_event_history
    | Open_format_picker
    | Close_format_picker
    | Set_format of BookFormat
    | Format_result of Result<unit, string>
    | Refresh_metadata
    | Refresh_result of Result<unit, string>
    | Confirm_remove_book
    | Cancel_remove_book
    | Remove_book
    | Book_removed of Result<unit, string>
