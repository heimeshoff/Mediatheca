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
    ]
