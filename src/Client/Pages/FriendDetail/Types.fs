module Mediatheca.Client.Pages.FriendDetail.Types

open Mediatheca.Shared

type EditForm = {
    Name: string
    ImageRef: string option
}

type Model = {
    Slug: string
    Friend: FriendDetail option
    FriendMedia: FriendMedia option
    IsLoading: bool
    IsEditing: bool
    EditForm: EditForm
    Error: string option
    ShowRemoveConfirm: bool
    ShowEventHistory: bool
    CollapsedSections: Set<string>
    SectionSettings: Map<string, ViewSettings>
}

type Msg =
    | Load_friend of string
    | Friend_loaded of FriendDetail option
    | Friend_media_loaded of FriendMedia
    | Start_editing
    | Cancel_editing
    | Edit_name_changed of string
    | Submit_update
    | Update_result of Result<unit, string>
    | Remove_friend
    | Confirm_remove_friend
    | Cancel_remove_friend
    | Remove_result of Result<unit, string>
    | Upload_friend_image of data: byte array * filename: string
    | Image_uploaded of Result<string, string>
    | Remove_from_recommended of mediaSlug: string * routePrefix: string
    | Remove_from_pending of mediaSlug: string * routePrefix: string
    | Media_remove_result of Result<unit, string>
    | Toggle_section of string
    | Collapsed_loaded of string list
    | Section_settings_loaded of section: string * settings: ViewSettings option
    | Save_section_settings of section: string * settings: ViewSettings
    | Settings_saved
    | Open_event_history
    | Close_event_history
