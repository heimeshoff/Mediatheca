module Mediatheca.Client.Pages.Settings.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Components

let view (model: Model) (dispatch: Msg -> unit) =
    PageContainer.view "Settings" [
        // TMDB Integration section
        Html.h2 [
            prop.className "text-lg font-bold font-display mb-2"
            prop.text "TMDB Integration"
        ]
        Html.p [
            prop.className "text-base-content/70 mb-4"
            prop.children [
                Html.text "Enter your TMDB API key to enable movie search and import. Get a free API key at "
                Html.a [
                    prop.href "https://www.themoviedb.org/settings/api"
                    prop.target "_blank"
                    prop.className "link link-primary"
                    prop.text "themoviedb.org/settings/api"
                ]
                Html.text "."
            ]
        ]

        // Current status
        Html.div [
            prop.className "mb-4"
            prop.children [
                if model.TmdbApiKey <> "" then
                    Daisy.badge [
                        badge.success
                        prop.className "gap-1"
                        prop.text ("Configured: " + model.TmdbApiKey)
                    ]
                else
                    Daisy.badge [
                        badge.warning
                        prop.className "gap-1"
                        prop.text "Not configured"
                    ]
            ]
        ]

        // API key input
        Html.div [
            prop.className "form-control mb-4"
            prop.children [
                Daisy.label [
                    prop.className "label"
                    prop.children [
                        Html.span [
                            prop.className "label-text"
                            prop.text "API Key"
                        ]
                    ]
                ]
                Daisy.input [
                    prop.type' "password"
                    prop.className "w-full"
                    prop.placeholder "Enter your TMDB API key..."
                    prop.value model.TmdbKeyInput
                    prop.onChange (Tmdb_key_input_changed >> dispatch)
                ]
            ]
        ]

        // Action buttons
        Html.div [
            prop.className "flex gap-2 mb-4"
            prop.children [
                Daisy.button.button [
                    button.outline
                    if model.IsTesting then button.disabled
                    prop.onClick (fun _ -> dispatch Test_tmdb_key)
                    prop.disabled (model.TmdbKeyInput = "" || model.IsTesting)
                    prop.children [
                        if model.IsTesting then
                            Daisy.loading [ loading.spinner; loading.sm ]
                        Html.text "Test Connection"
                    ]
                ]
                Daisy.button.button [
                    button.primary
                    if model.IsSaving then button.disabled
                    prop.onClick (fun _ -> dispatch Save_tmdb_key)
                    prop.disabled (model.TmdbKeyInput = "" || model.IsSaving)
                    prop.children [
                        if model.IsSaving then
                            Daisy.loading [ loading.spinner; loading.sm ]
                        Html.text "Save"
                    ]
                ]
            ]
        ]

        // Feedback messages
        match model.TestResult with
        | Some (Ok msg) ->
            Daisy.alert [
                alert.success
                prop.className "mb-2"
                prop.text msg
            ]
        | Some (Error msg) ->
            Daisy.alert [
                alert.error
                prop.className "mb-2"
                prop.text msg
            ]
        | None -> ()

        match model.SaveResult with
        | Some (Ok msg) ->
            Daisy.alert [
                alert.success
                prop.className "mb-2"
                prop.text msg
            ]
        | Some (Error msg) ->
            Daisy.alert [
                alert.error
                prop.className "mb-2"
                prop.text msg
            ]
        | None -> ()

        // Divider
        Html.div [ prop.className "divider my-8" ]

        // Cinemarco Import section
        Html.h2 [
            prop.className "text-lg font-bold font-display mb-2"
            prop.text "Import from Cinemarco"
        ]
        Html.p [
            prop.className "text-base-content/70 mb-4"
            prop.text "Import your complete Cinemarco library (movies, series, friends, collections, watch sessions, ratings, notes) into Mediatheca. The Mediatheca database must be empty before importing."
        ]

        Daisy.alert [
            alert.warning
            prop.className "mb-4"
            prop.text "This will only work on a fresh (empty) Mediatheca database. All data from Cinemarco will be migrated as Mediatheca events."
        ]

        // Database path input
        Html.div [
            prop.className "form-control mb-4"
            prop.children [
                Daisy.label [
                    prop.className "label"
                    prop.children [
                        Html.span [
                            prop.className "label-text"
                            prop.text "Cinemarco Database Path"
                        ]
                    ]
                ]
                Daisy.input [
                    prop.className "w-full"
                    prop.placeholder "/path/to/cinemarco/cinemarco.db"
                    prop.value model.CinemarcoDbPath
                    prop.onChange (Cinemarco_db_path_changed >> dispatch)
                    prop.disabled model.IsImporting
                ]
            ]
        ]

        // Images path input
        Html.div [
            prop.className "form-control mb-4"
            prop.children [
                Daisy.label [
                    prop.className "label"
                    prop.children [
                        Html.span [
                            prop.className "label-text"
                            prop.text "Cinemarco Images Path"
                        ]
                    ]
                ]
                Daisy.input [
                    prop.className "w-full"
                    prop.placeholder "/path/to/cinemarco/images/"
                    prop.value model.CinemarcoImagesPath
                    prop.onChange (Cinemarco_images_path_changed >> dispatch)
                    prop.disabled model.IsImporting
                ]
            ]
        ]

        // Import button
        Html.div [
            prop.className "mb-4"
            prop.children [
                Daisy.button.button [
                    button.primary
                    if model.IsImporting then button.disabled
                    prop.onClick (fun _ -> dispatch Start_cinemarco_import)
                    prop.disabled (model.CinemarcoDbPath = "" || model.CinemarcoImagesPath = "" || model.IsImporting)
                    prop.children [
                        if model.IsImporting then
                            Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Importing..."
                        else
                            Html.text "Import"
                    ]
                ]
            ]
        ]

        // Import result
        match model.ImportResult with
        | Some (Ok result) ->
            Daisy.alert [
                alert.success
                prop.className "mb-4"
                prop.children [
                    Html.div [
                        Html.p [ prop.className "font-bold"; prop.text "Import completed successfully!" ]
                        Html.ul [
                            prop.className "mt-2 text-sm space-y-1"
                            prop.children [
                                Html.li [ prop.text (sprintf "Friends: %d" result.FriendsImported) ]
                                Html.li [ prop.text (sprintf "Movies: %d" result.MoviesImported) ]
                                Html.li [ prop.text (sprintf "Series: %d" result.SeriesImported) ]
                                Html.li [ prop.text (sprintf "Episodes watched: %d" result.EpisodesWatched) ]
                                Html.li [ prop.text (sprintf "Catalogs: %d" result.CatalogsImported) ]
                                Html.li [ prop.text (sprintf "Content blocks: %d" result.ContentBlocksImported) ]
                                Html.li [ prop.text (sprintf "Images copied: %d" result.ImagesCopied) ]
                            ]
                        ]
                        if not (List.isEmpty result.Errors) then
                            Html.div [
                                prop.className "mt-2"
                                prop.children [
                                    Html.p [ prop.className "font-bold text-warning"; prop.text (sprintf "Warnings (%d):" result.Errors.Length) ]
                                    Html.ul [
                                        prop.className "text-sm text-warning"
                                        prop.children (
                                            result.Errors |> List.map (fun err ->
                                                Html.li [ prop.text err ])
                                        )
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        | Some (Error msg) ->
            Daisy.alert [
                alert.error
                prop.className "mb-4"
                prop.text msg
            ]
        | None -> ()
    ]
