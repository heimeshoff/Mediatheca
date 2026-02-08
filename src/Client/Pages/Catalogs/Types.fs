module Mediatheca.Client.Pages.Catalogs.Types

open Mediatheca.Shared

type CreateCatalogForm = {
    Name: string
    Description: string
    IsSorted: bool
}

type Model = {
    Catalogs: CatalogListItem list
    IsLoading: bool
    ShowCreateForm: bool
    CreateForm: CreateCatalogForm
    Error: string option
}

type Msg =
    | Load_catalogs
    | Catalogs_loaded of CatalogListItem list
    | Toggle_create_form
    | Create_form_name_changed of string
    | Create_form_description_changed of string
    | Create_form_sorted_changed of bool
    | Submit_create_catalog
    | Catalog_created of Result<string, string>
    | Remove_catalog of string
    | Catalog_removed of Result<unit, string>
