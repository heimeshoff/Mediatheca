module Mediatheca.Client.Pages.CatalogDetail.Types

open Mediatheca.Shared

type AddEntryForm = {
    MovieSlug: string
    Note: string
}

type EditNoteState = {
    EntryId: string
    Note: string
}

type Model = {
    Slug: string
    Catalog: CatalogDetail option
    AllMovies: MovieListItem list
    IsLoading: bool
    ShowAddEntry: bool
    AddEntryForm: AddEntryForm
    EditingNote: EditNoteState option
    ShowEditCatalog: bool
    EditName: string
    EditDescription: string
    ShowEventHistory: bool
    Error: string option
    ViewSettings: ViewSettings option
}

type Msg =
    | Load_catalog of string
    | Catalog_loaded of CatalogDetail option
    | Movies_loaded of MovieListItem list
    | Toggle_add_entry
    | Add_entry_movie_changed of string
    | Add_entry_note_changed of string
    | Submit_add_entry
    | Entry_added of Result<string, string>
    | Remove_entry of string
    | Entry_removed of Result<unit, string>
    | Start_edit_note of entryId: string * currentNote: string option
    | Edit_note_changed of string
    | Save_note
    | Cancel_edit_note
    | Note_updated of Result<unit, string>
    | Open_edit_catalog
    | Close_edit_catalog
    | Edit_name_changed of string
    | Edit_description_changed of string
    | Submit_edit_catalog
    | Catalog_updated of Result<unit, string>
    | Remove_catalog
    | Catalog_removed of Result<unit, string>
    | Command_result of Result<unit, string>
    | View_settings_loaded of ViewSettings option
    | Save_view_settings of ViewSettings
    | View_settings_saved
    | Open_event_history
    | Close_event_history
