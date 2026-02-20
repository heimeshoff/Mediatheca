module Mediatheca.Client.Pages.CatalogDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.CatalogDetail.Types

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Catalog = None
      AllMovies = []
      IsLoading = true
      ShowAddEntry = false
      AddEntryForm = { MovieSlug = ""; Note = "" }
      EditingNote = None
      ShowEditCatalog = false
      EditName = ""
      EditDescription = ""
      ShowEventHistory = false
      Error = None
      ViewSettings = None },
    Cmd.ofMsg (Load_catalog slug)

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_catalog slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getCatalog slug Catalog_loaded
            Cmd.OfAsync.perform api.getMovies () Movies_loaded
            Cmd.OfAsync.perform api.getViewSettings ("catalog:" + slug) View_settings_loaded
        ]

    | Catalog_loaded catalog ->
        { model with Catalog = catalog; IsLoading = false }, Cmd.none

    | Movies_loaded movies ->
        { model with AllMovies = movies }, Cmd.none

    | Toggle_add_entry ->
        { model with ShowAddEntry = not model.ShowAddEntry; AddEntryForm = { MovieSlug = ""; Note = "" }; Error = None }, Cmd.none

    | Add_entry_movie_changed slug ->
        { model with AddEntryForm = { model.AddEntryForm with MovieSlug = slug } }, Cmd.none

    | Add_entry_note_changed note ->
        { model with AddEntryForm = { model.AddEntryForm with Note = note } }, Cmd.none

    | Submit_add_entry ->
        if model.AddEntryForm.MovieSlug = "" then
            { model with Error = Some "Please select a movie" }, Cmd.none
        else
            let note = if model.AddEntryForm.Note.Trim() = "" then None else Some (model.AddEntryForm.Note.Trim())
            let request: AddCatalogEntryRequest = {
                MovieSlug = model.AddEntryForm.MovieSlug
                Note = note
            }
            model,
            Cmd.OfAsync.either
                (fun () -> api.addCatalogEntry model.Slug request) ()
                Entry_added
                (fun ex -> Entry_added (Error ex.Message))

    | Entry_added (Ok _) ->
        { model with ShowAddEntry = false; AddEntryForm = { MovieSlug = ""; Note = "" } },
        Cmd.OfAsync.perform api.getCatalog model.Slug Catalog_loaded

    | Entry_added (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_entry entryId ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeCatalogEntry model.Slug entryId) ()
            Entry_removed
            (fun ex -> Entry_removed (Error ex.Message))

    | Entry_removed (Ok ()) ->
        model, Cmd.OfAsync.perform api.getCatalog model.Slug Catalog_loaded

    | Entry_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Start_edit_note (entryId, currentNote) ->
        { model with EditingNote = Some { EntryId = entryId; Note = currentNote |> Option.defaultValue "" } }, Cmd.none

    | Edit_note_changed note ->
        match model.EditingNote with
        | Some state -> { model with EditingNote = Some { state with Note = note } }, Cmd.none
        | None -> model, Cmd.none

    | Save_note ->
        match model.EditingNote with
        | Some state ->
            let note = if state.Note.Trim() = "" then None else Some (state.Note.Trim())
            let request: UpdateCatalogEntryRequest = { Note = note }
            { model with EditingNote = None },
            Cmd.OfAsync.either
                (fun () -> api.updateCatalogEntry model.Slug state.EntryId request) ()
                Note_updated
                (fun ex -> Note_updated (Error ex.Message))
        | None -> model, Cmd.none

    | Cancel_edit_note ->
        { model with EditingNote = None }, Cmd.none

    | Note_updated (Ok ()) ->
        model, Cmd.OfAsync.perform api.getCatalog model.Slug Catalog_loaded

    | Note_updated (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_edit_catalog ->
        match model.Catalog with
        | Some c ->
            { model with ShowEditCatalog = true; EditName = c.Name; EditDescription = c.Description }, Cmd.none
        | None -> model, Cmd.none

    | Close_edit_catalog ->
        { model with ShowEditCatalog = false }, Cmd.none

    | Edit_name_changed name ->
        { model with EditName = name }, Cmd.none

    | Edit_description_changed desc ->
        { model with EditDescription = desc }, Cmd.none

    | Submit_edit_catalog ->
        let trimmedName = model.EditName.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            let request: UpdateCatalogRequest = {
                Name = trimmedName
                Description = model.EditDescription.Trim()
            }
            { model with ShowEditCatalog = false },
            Cmd.OfAsync.either
                (fun () -> api.updateCatalog model.Slug request) ()
                Catalog_updated
                (fun ex -> Catalog_updated (Error ex.Message))

    | Catalog_updated (Ok ()) ->
        model, Cmd.OfAsync.perform api.getCatalog model.Slug Catalog_loaded

    | Catalog_updated (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_catalog ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeCatalog model.Slug) ()
            Catalog_removed
            (fun ex -> Catalog_removed (Error ex.Message))

    | Catalog_removed (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "catalogs")

    | Catalog_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Command_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getCatalog model.Slug Catalog_loaded

    | Command_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | View_settings_loaded settings ->
        { model with ViewSettings = settings }, Cmd.none

    | Save_view_settings settings ->
        { model with ViewSettings = Some settings },
        Cmd.OfAsync.either
            (fun () -> api.saveViewSettings ("catalog:" + model.Slug) settings) ()
            (fun () -> View_settings_saved)
            (fun _ -> View_settings_saved)

    | View_settings_saved ->
        model, Cmd.none

    | Open_event_history ->
        { model with ShowEventHistory = true }, Cmd.none

    | Close_event_history ->
        { model with ShowEventHistory = false }, Cmd.none
