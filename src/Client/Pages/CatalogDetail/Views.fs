module Mediatheca.Client.Pages.CatalogDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.CatalogDetail.Types
open Mediatheca.Client.Components

let private entryCard (entry: Mediatheca.Shared.CatalogEntryDto) (editingNote: EditNoteState option) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 group"
        prop.children [
            Html.a [
                prop.href (Router.format ("movies", entry.MovieSlug))
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate ("movies", entry.MovieSlug)
                )
                prop.className "flex-none cursor-pointer"
                prop.children [
                    PosterCard.thumbnail entry.MoviePosterRef entry.MovieName
                ]
            ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.a [
                        prop.href (Router.format ("movies", entry.MovieSlug))
                        prop.onClick (fun e ->
                            e.preventDefault()
                            Router.navigate ("movies", entry.MovieSlug)
                        )
                        prop.className "font-semibold text-sm truncate hover:text-primary transition-colors cursor-pointer block"
                        prop.text entry.MovieName
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.text (string entry.MovieYear)
                    ]
                    match editingNote with
                    | Some state when state.EntryId = entry.EntryId ->
                        Html.div [
                            prop.className "mt-1 flex gap-1"
                            prop.children [
                                Daisy.input [
                                    input.sm
                                    prop.className "flex-1 text-xs"
                                    prop.value state.Note
                                    prop.onChange (Edit_note_changed >> dispatch)
                                    prop.onKeyDown (fun e ->
                                        if e.key = "Enter" then dispatch Save_note
                                        elif e.key = "Escape" then dispatch Cancel_edit_note
                                    )
                                    prop.autoFocus true
                                ]
                                Daisy.button.button [
                                    button.success
                                    button.xs
                                    prop.onClick (fun _ -> dispatch Save_note)
                                    prop.text "Save"
                                ]
                                Daisy.button.button [
                                    button.ghost
                                    button.xs
                                    prop.onClick (fun _ -> dispatch Cancel_edit_note)
                                    prop.text "Cancel"
                                ]
                            ]
                        ]
                    | _ ->
                        match entry.Note with
                        | Some note ->
                            Html.p [
                                prop.className "text-xs text-base-content/40 mt-0.5 italic cursor-pointer hover:text-base-content/60"
                                prop.onClick (fun _ -> dispatch (Start_edit_note (entry.EntryId, entry.Note)))
                                prop.text note
                            ]
                        | None ->
                            Html.p [
                                prop.className "text-xs text-base-content/20 mt-0.5 cursor-pointer hover:text-base-content/40"
                                prop.onClick (fun _ -> dispatch (Start_edit_note (entry.EntryId, None)))
                                prop.text "Add note..."
                            ]
                ]
            ]
            Html.div [
                prop.className "flex-none opacity-0 group-hover:opacity-100 transition-opacity"
                prop.children [
                    Daisy.button.button [
                        button.ghost
                        button.xs
                        prop.className "text-error"
                        prop.onClick (fun _ -> dispatch (Remove_entry entry.EntryId))
                        prop.text "Remove"
                    ]
                ]
            ]
        ]
    ]

let private addEntryForm (model: Model) (dispatch: Msg -> unit) =
    let existingMovieSlugs =
        model.Catalog
        |> Option.map (fun c -> c.Entries |> List.map (fun e -> e.MovieSlug) |> Set.ofList)
        |> Option.defaultValue Set.empty
    let availableMovies =
        model.AllMovies
        |> List.filter (fun m -> not (existingMovieSlugs.Contains m.Slug))

    Daisy.card [
        prop.className "bg-base-100 shadow-md mb-4 animate-scale-in"
        prop.children [
            Daisy.cardBody [
                prop.children [
                    Html.h3 [
                        prop.className "font-bold mb-3"
                        prop.text "Add Movie"
                    ]
                    Html.div [
                        prop.className "flex flex-col gap-3"
                        prop.children [
                            Daisy.select [
                                prop.value model.AddEntryForm.MovieSlug
                                prop.onChange (Add_entry_movie_changed >> dispatch)
                                prop.children [
                                    Html.option [
                                        prop.value ""
                                        prop.text "Select a movie..."
                                    ]
                                    for movie in availableMovies do
                                        Html.option [
                                            prop.value movie.Slug
                                            prop.text $"{movie.Name} ({movie.Year})"
                                        ]
                                ]
                            ]
                            Daisy.input [
                                prop.placeholder "Note (optional)..."
                                prop.value model.AddEntryForm.Note
                                prop.onChange (Add_entry_note_changed >> dispatch)
                                prop.onKeyDown (fun e ->
                                    if e.key = "Enter" then dispatch Submit_add_entry
                                )
                            ]
                            Daisy.button.button [
                                button.primary
                                prop.onClick (fun _ -> dispatch Submit_add_entry)
                                prop.text "Add"
                            ]
                        ]
                    ]
                    match model.Error with
                    | Some err ->
                        Daisy.alert [
                            alert.error
                            prop.className "mt-2"
                            prop.text err
                        ]
                    | None -> ()
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    match model.IsLoading, model.Catalog with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-20"
            prop.children [
                Daisy.loading [ loading.spinner; loading.lg ]
            ]
        ]
    | false, None ->
        Html.div [
            prop.className "text-center py-20 animate-fade-in"
            prop.children [
                Html.p [
                    prop.className "text-base-content/50 font-medium"
                    prop.text "Catalog not found."
                ]
                Html.a [
                    prop.href (Router.format "catalogs")
                    prop.onClick (fun e ->
                        e.preventDefault()
                        Router.navigate "catalogs"
                    )
                    prop.className "text-primary mt-2 inline-block"
                    prop.text "Back to Catalogs"
                ]
            ]
        ]
    | false, Some catalog ->
        Html.div [
            prop.className "animate-fade-in"
            prop.children [
                // Header
                Html.div [
                    prop.className "bg-base-200/50 px-4 lg:px-6 py-6"
                    prop.children [
                        Html.div [
                            prop.className "flex items-start justify-between"
                            prop.children [
                                Html.div [
                                    prop.children [
                                        Html.div [
                                            prop.className "flex items-center gap-2 mb-2"
                                            prop.children [
                                                Html.a [
                                                    prop.href (Router.format "catalogs")
                                                    prop.onClick (fun e ->
                                                        e.preventDefault()
                                                        Router.navigate "catalogs"
                                                    )
                                                    prop.className "text-sm text-base-content/50 hover:text-primary transition-colors"
                                                    prop.text "Catalogs"
                                                ]
                                                Html.span [
                                                    prop.className "text-base-content/30 text-sm"
                                                    prop.text "/"
                                                ]
                                            ]
                                        ]
                                        if model.ShowEditCatalog then
                                            Html.div [
                                                prop.className "flex flex-col gap-2"
                                                prop.children [
                                                    Daisy.input [
                                                        prop.className "text-xl font-bold"
                                                        prop.value model.EditName
                                                        prop.onChange (Edit_name_changed >> dispatch)
                                                    ]
                                                    Daisy.textarea [
                                                        prop.className "textarea-sm"
                                                        prop.value model.EditDescription
                                                        prop.onChange (Edit_description_changed >> dispatch)
                                                        prop.placeholder "Description..."
                                                    ]
                                                    Html.div [
                                                        prop.className "flex gap-2"
                                                        prop.children [
                                                            Daisy.button.button [
                                                                button.primary
                                                                button.sm
                                                                prop.onClick (fun _ -> dispatch Submit_edit_catalog)
                                                                prop.text "Save"
                                                            ]
                                                            Daisy.button.button [
                                                                button.ghost
                                                                button.sm
                                                                prop.onClick (fun _ -> dispatch Close_edit_catalog)
                                                                prop.text "Cancel"
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        else
                                            Html.div [
                                                prop.children [
                                                    Html.h1 [
                                                        prop.className "text-2xl lg:text-3xl font-bold font-display text-gradient-primary"
                                                        prop.text catalog.Name
                                                    ]
                                                    if catalog.Description <> "" then
                                                        Html.p [
                                                            prop.className "text-base-content/60 mt-1"
                                                            prop.text catalog.Description
                                                        ]
                                                    Html.div [
                                                        prop.className "flex gap-2 mt-2"
                                                        prop.children [
                                                            Daisy.badge [
                                                                badge.ghost
                                                                prop.text $"{List.length catalog.Entries} movies"
                                                            ]
                                                            if catalog.IsSorted then
                                                                Daisy.badge [
                                                                    badge.info
                                                                    badge.outline
                                                                    prop.text "Sorted"
                                                                ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                    ]
                                ]
                                if not model.ShowEditCatalog then
                                    Html.div [
                                        prop.className "flex gap-2"
                                        prop.children [
                                            Daisy.button.button [
                                                button.ghost
                                                button.sm
                                                prop.onClick (fun _ -> dispatch Open_edit_catalog)
                                                prop.text "Edit"
                                            ]
                                            Daisy.button.button [
                                                button.ghost
                                                button.sm
                                                prop.className "text-error"
                                                prop.onClick (fun _ -> dispatch Remove_catalog)
                                                prop.text "Delete"
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]

                // Entries
                Html.div [
                    prop.className "p-4 lg:p-6"
                    prop.children [
                        Html.div [
                            prop.className "flex items-center justify-between mb-4"
                            prop.children [
                                Html.h2 [
                                    prop.className "text-lg font-bold font-display"
                                    prop.text "Movies"
                                ]
                                Daisy.button.button [
                                    button.primary
                                    button.sm
                                    prop.onClick (fun _ -> dispatch Toggle_add_entry)
                                    prop.text (if model.ShowAddEntry then "Cancel" else "+ Add Movie")
                                ]
                            ]
                        ]

                        if model.ShowAddEntry then
                            addEntryForm model dispatch

                        if List.isEmpty catalog.Entries then
                            Html.div [
                                prop.className "text-center py-12 text-base-content/30"
                                prop.children [
                                    Html.p [ prop.text "No movies in this catalog yet." ]
                                    Html.p [
                                        prop.className "text-sm mt-1"
                                        prop.text "Add movies to build your catalog."
                                    ]
                                ]
                            ]
                        else
                            Daisy.card [
                                prop.className "bg-base-200/50 shadow-md"
                                prop.children [
                                    Daisy.cardBody [
                                        prop.className "p-2 gap-1"
                                        prop.children [
                                            for entry in catalog.Entries do
                                                entryCard entry model.EditingNote dispatch
                                        ]
                                    ]
                                ]
                            ]

                        match model.Error with
                        | Some err ->
                            Daisy.alert [
                                alert.error
                                prop.className "mt-4"
                                prop.text err
                            ]
                        | None -> ()
                    ]
                ]
            ]
        ]
