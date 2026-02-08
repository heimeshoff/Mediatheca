module Mediatheca.Client.Components.TmdbSearchModal

open Feliz
open Feliz.DaisyUI
open Mediatheca.Shared

type Model = {
    Query: string
    Results: TmdbSearchResult list
    IsSearching: bool
    IsImporting: bool
    Error: string option
}

type Msg =
    | Query_changed of string
    | Search
    | Search_completed of TmdbSearchResult list
    | Search_failed of string
    | Import of tmdbId: int
    | Import_completed of Result<string, string>
    | Close

let init () : Model = {
    Query = ""
    Results = []
    IsSearching = false
    IsImporting = false
    Error = None
}

let view (model: Model) (dispatch: Msg -> unit) =
    Daisy.modal.dialog [
        modal.open'
        prop.children [
            Daisy.modalBackdrop [ prop.onClick (fun _ -> dispatch Close) ]
            Daisy.modalBox.div [
                prop.className "max-w-2xl w-full"
                prop.children [
                    Html.h3 [
                        prop.className "font-bold text-lg font-display mb-4"
                        prop.text "Search TMDB"
                    ]
                    Html.div [
                        prop.className "flex gap-2 mb-4"
                        prop.children [
                            Daisy.input [
                                prop.className "flex-1"
                                prop.placeholder "Search for a movie..."
                                prop.value model.Query
                                prop.onChange (Query_changed >> dispatch)
                                prop.onKeyDown (fun e ->
                                    if e.key = "Enter" then dispatch Search
                                )
                            ]
                            Daisy.button.button [
                                button.primary
                                prop.onClick (fun _ -> dispatch Search)
                                prop.disabled model.IsSearching
                                prop.text (if model.IsSearching then "Searching..." else "Search")
                            ]
                        ]
                    ]
                    match model.Error with
                    | Some err ->
                        Daisy.alert [
                            alert.error
                            prop.className "mb-4"
                            prop.text err
                        ]
                    | None -> ()
                    if model.IsImporting then
                        Html.div [
                            prop.className "flex justify-center py-8"
                            prop.children [
                                Daisy.loading [ loading.spinner; loading.lg ]
                            ]
                        ]
                    else
                        Html.div [
                            prop.className "space-y-2 max-h-96 overflow-y-auto"
                            prop.children [
                                for result in model.Results do
                                    Html.div [
                                        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-200 cursor-pointer"
                                        prop.onClick (fun _ -> dispatch (Import result.TmdbId))
                                        prop.children [
                                            match result.PosterPath with
                                            | Some path ->
                                                Html.img [
                                                    prop.src $"https://image.tmdb.org/t/p/w92{path}"
                                                    prop.className "w-12 h-18 rounded object-cover"
                                                    prop.alt result.Title
                                                ]
                                            | None ->
                                                Html.div [
                                                    prop.className "w-12 h-18 rounded bg-base-300 flex items-center justify-center text-xs"
                                                    prop.text "?"
                                                ]
                                            Html.div [
                                                prop.className "flex-1"
                                                prop.children [
                                                    Html.p [
                                                        prop.className "font-semibold"
                                                        prop.text result.Title
                                                    ]
                                                    match result.Year with
                                                    | Some y ->
                                                        Html.p [
                                                            prop.className "text-sm text-base-content/60"
                                                            prop.text (string y)
                                                        ]
                                                    | None -> ()
                                                ]
                                            ]
                                            Daisy.button.button [
                                                button.sm
                                                button.primary
                                                button.outline
                                                prop.text "Import"
                                                prop.onClick (fun e ->
                                                    e.stopPropagation()
                                                    dispatch (Import result.TmdbId)
                                                )
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                    Html.div [
                        prop.className "modal-action"
                        prop.children [
                            Daisy.button.button [
                                prop.onClick (fun _ -> dispatch Close)
                                prop.text "Cancel"
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
