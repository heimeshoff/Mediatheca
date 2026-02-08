module Mediatheca.Client.Pages.Catalogs.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Catalogs.Types

let init () : Model * Cmd<Msg> =
    { Catalogs = []
      IsLoading = true
      ShowCreateForm = false
      CreateForm = { Name = ""; Description = ""; IsSorted = false }
      Error = None },
    Cmd.ofMsg Load_catalogs

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_catalogs ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded

    | Catalogs_loaded catalogs ->
        { model with Catalogs = catalogs; IsLoading = false }, Cmd.none

    | Toggle_create_form ->
        { model with ShowCreateForm = not model.ShowCreateForm; CreateForm = { Name = ""; Description = ""; IsSorted = false }; Error = None }, Cmd.none

    | Create_form_name_changed name ->
        { model with CreateForm = { model.CreateForm with Name = name } }, Cmd.none

    | Create_form_description_changed desc ->
        { model with CreateForm = { model.CreateForm with Description = desc } }, Cmd.none

    | Create_form_sorted_changed sorted ->
        { model with CreateForm = { model.CreateForm with IsSorted = sorted } }, Cmd.none

    | Submit_create_catalog ->
        let trimmedName = model.CreateForm.Name.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            let request: CreateCatalogRequest = {
                Name = trimmedName
                Description = model.CreateForm.Description.Trim()
                IsSorted = model.CreateForm.IsSorted
            }
            model,
            Cmd.OfAsync.either
                api.createCatalog request
                Catalog_created
                (fun ex -> Catalog_created (Error ex.Message))

    | Catalog_created (Ok slug) ->
        { model with ShowCreateForm = false; CreateForm = { Name = ""; Description = ""; IsSorted = false } },
        Cmd.batch [
            Cmd.ofMsg Load_catalogs
            Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate ("catalogs", slug))
        ]

    | Catalog_created (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_catalog slug ->
        model,
        Cmd.OfAsync.either
            api.removeCatalog slug
            Catalog_removed
            (fun ex -> Catalog_removed (Error ex.Message))

    | Catalog_removed (Ok ()) ->
        model, Cmd.ofMsg Load_catalogs

    | Catalog_removed (Error err) ->
        { model with Error = Some err }, Cmd.none
