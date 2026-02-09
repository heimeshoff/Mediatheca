module Mediatheca.Client.Components.SearchModal

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared

type Model = {
    Query: string
    LocalResults: LibrarySearchResult list
    TmdbResults: TmdbSearchResult list
    IsSearchingLocal: bool
    IsSearchingTmdb: bool
    IsImporting: bool
    Error: string option
    SearchVersion: int
}

type Msg =
    | Query_changed of string
    | Debounce_local_expired of version: int
    | Debounce_tmdb_expired of version: int
    | Local_search_completed of LibrarySearchResult list
    | Local_search_failed of string
    | Tmdb_search_completed of TmdbSearchResult list
    | Tmdb_search_failed of string
    | Import of tmdbId: int
    | Import_completed of Result<string, string>
    | Navigate_to of slug: string
    | Close

let init () : Model = {
    Query = ""
    LocalResults = []
    TmdbResults = []
    IsSearchingLocal = false
    IsSearchingTmdb = false
    IsImporting = false
    Error = None
    SearchVersion = 0
}

let view (model: Model) (dispatch: Msg -> unit) =
    // Full-screen backdrop with blur
    Html.div [
        prop.className "fixed inset-0 z-50 flex justify-center items-start pt-[10vh]"
        prop.children [
            // Backdrop
            Html.div [
                prop.className "absolute inset-0 bg-black/30"
                prop.onClick (fun _ -> dispatch Close)
            ]
            // Modal panel (glass effect)
            Html.div [
                prop.className "relative w-full max-w-2xl mx-4 max-h-[70vh] flex flex-col bg-base-100/70 backdrop-blur-xl rounded-2xl shadow-2xl border border-base-content/10 overflow-hidden animate-fade-in"
                prop.children [
                    // Header + input area (non-scrolling)
                    Html.div [
                        prop.className "p-5 pb-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between mb-4"
                                prop.children [
                                    Html.h3 [
                                        prop.className "font-bold text-lg font-display"
                                        prop.text "Search"
                                    ]
                                    Html.kbd [
                                        prop.className "kbd kbd-sm text-base-content/50"
                                        prop.text "Ctrl+K"
                                    ]
                                ]
                            ]
                            Daisy.input [
                                prop.className "w-full mb-4"
                                prop.placeholder "Search movies..."
                                prop.value model.Query
                                prop.autoFocus true
                                prop.onChange (Query_changed >> dispatch)
                                prop.onKeyDown (fun e ->
                                    if e.key = "Escape" then dispatch Close
                                )
                            ]
                        ]
                    ]
                    // Error
                    match model.Error with
                    | Some err ->
                        Html.div [
                            prop.className "px-5"
                            prop.children [
                                Daisy.alert [
                                    alert.error
                                    prop.className "mb-4"
                                    prop.text err
                                ]
                            ]
                        ]
                    | None -> ()
                    // Scrollable content area
                    Html.div [
                        prop.className "flex-1 overflow-y-auto px-5 pb-5"
                        prop.children [
                            if model.IsImporting then
                                Html.div [
                                    prop.className "flex justify-center py-8"
                                    prop.children [
                                        Daisy.loading [ loading.spinner; loading.lg ]
                                    ]
                                ]
                            elif model.Query = "" then
                                Html.div [
                                    prop.className "text-center py-8 text-base-content/40"
                                    prop.children [
                                        Html.p [ prop.text "Start typing to search your library and TMDB." ]
                                    ]
                                ]
                            else
                                Html.div [
                                    prop.className "space-y-4"
                                    prop.children [
                                        // In Your Library section
                                        Html.div [
                                            prop.children [
                                                Html.h4 [
                                                    prop.className "text-sm font-semibold text-base-content/60 uppercase tracking-wide mb-2"
                                                    prop.text "In Your Library"
                                                ]
                                                if model.IsSearchingLocal then
                                                    Html.div [
                                                        prop.className "flex items-center gap-2 py-3 text-base-content/40"
                                                        prop.children [
                                                            Daisy.loading [ loading.dots; loading.sm ]
                                                            Html.span [ prop.text "Searching..." ]
                                                        ]
                                                    ]
                                                elif List.isEmpty model.LocalResults then
                                                    Html.p [
                                                        prop.className "text-sm text-base-content/40 py-2"
                                                        prop.text "No matches in your library."
                                                    ]
                                                else
                                                    Html.div [
                                                        prop.className "space-y-1"
                                                        prop.children [
                                                            for result in model.LocalResults do
                                                                Html.div [
                                                                    prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-200 cursor-pointer transition-colors"
                                                                    prop.onClick (fun _ -> dispatch (Navigate_to result.Slug))
                                                                    prop.children [
                                                                        match result.PosterRef with
                                                                        | Some ref ->
                                                                            Html.img [
                                                                                prop.src $"/images/{ref}"
                                                                                prop.className "w-10 h-15 rounded object-cover"
                                                                                prop.alt result.Name
                                                                            ]
                                                                        | None ->
                                                                            Html.div [
                                                                                prop.className "w-10 h-15 rounded bg-base-300 flex items-center justify-center"
                                                                                prop.children [
                                                                                    Icons.movie ()
                                                                                ]
                                                                            ]
                                                                        Html.div [
                                                                            prop.className "flex-1"
                                                                            prop.children [
                                                                                Html.p [
                                                                                    prop.className "font-semibold text-sm"
                                                                                    prop.text result.Name
                                                                                ]
                                                                                Html.p [
                                                                                    prop.className "text-xs text-base-content/60"
                                                                                    prop.text (string result.Year)
                                                                                ]
                                                                            ]
                                                                        ]
                                                                        Daisy.badge [
                                                                            badge.success
                                                                            badge.sm
                                                                            prop.text "In Library"
                                                                        ]
                                                                    ]
                                                                ]
                                                        ]
                                                    ]
                                            ]
                                        ]
                                        // TMDB Results section
                                        Html.div [
                                            prop.children [
                                                Html.h4 [
                                                    prop.className "text-sm font-semibold text-base-content/60 uppercase tracking-wide mb-2"
                                                    prop.text "TMDB Results"
                                                ]
                                                if model.IsSearchingTmdb then
                                                    Html.div [
                                                        prop.className "flex items-center gap-2 py-3 text-base-content/40"
                                                        prop.children [
                                                            Daisy.loading [ loading.dots; loading.sm ]
                                                            Html.span [ prop.text "Searching TMDB..." ]
                                                        ]
                                                    ]
                                                elif List.isEmpty model.TmdbResults then
                                                    Html.p [
                                                        prop.className "text-sm text-base-content/40 py-2"
                                                        prop.text "No TMDB results yet."
                                                    ]
                                                else
                                                    Html.div [
                                                        prop.className "space-y-1"
                                                        prop.children [
                                                            for result in model.TmdbResults do
                                                                Html.div [
                                                                    prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-200 cursor-pointer transition-colors"
                                                                    prop.onClick (fun _ -> dispatch (Import result.TmdbId))
                                                                    prop.children [
                                                                        match result.PosterPath with
                                                                        | Some path ->
                                                                            Html.img [
                                                                                prop.src $"https://image.tmdb.org/t/p/w92{path}"
                                                                                prop.className "w-10 h-15 rounded object-cover"
                                                                                prop.alt result.Title
                                                                            ]
                                                                        | None ->
                                                                            Html.div [
                                                                                prop.className "w-10 h-15 rounded bg-base-300 flex items-center justify-center text-xs"
                                                                                prop.text "?"
                                                                            ]
                                                                        Html.div [
                                                                            prop.className "flex-1"
                                                                            prop.children [
                                                                                Html.p [
                                                                                    prop.className "font-semibold text-sm"
                                                                                    prop.text result.Title
                                                                                ]
                                                                                match result.Year with
                                                                                | Some y ->
                                                                                    Html.p [
                                                                                        prop.className "text-xs text-base-content/60"
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
                                            ]
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]
