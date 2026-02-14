module Mediatheca.Client.Components.SearchModal

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared

type Model = {
    Query: string
    LibraryMovies: MovieListItem list
    LibrarySeries: SeriesListItem list
    TmdbResults: TmdbSearchResult list
    IsSearchingTmdb: bool
    IsImporting: bool
    Error: string option
    SearchVersion: int
}

type Msg =
    | Query_changed of string
    | Debounce_tmdb_expired of version: int
    | Tmdb_search_completed of TmdbSearchResult list
    | Tmdb_search_failed of string
    | Import of tmdbId: int * MediaType
    | Import_completed of Result<string * MediaType, string>
    | Navigate_to of slug: string * MediaType
    | Close

let init (movies: MovieListItem list) (series: SeriesListItem list) : Model = {
    Query = ""
    LibraryMovies = movies
    LibrarySeries = series
    TmdbResults = []
    IsSearchingTmdb = false
    IsImporting = false
    Error = None
    SearchVersion = 0
}

let filterLibrary (query: string) (movies: MovieListItem list) (series: SeriesListItem list) : LibrarySearchResult list =
    if query = "" then []
    else
        let q = query.ToLowerInvariant()
        let movieResults =
            movies
            |> List.filter (fun m -> m.Name.ToLowerInvariant().Contains(q))
            |> List.map (fun m ->
                { Slug = m.Slug
                  Name = m.Name
                  Year = m.Year
                  PosterRef = m.PosterRef
                  MediaType = MediaType.Movie })
        let seriesResults =
            series
            |> List.filter (fun s -> s.Name.ToLowerInvariant().Contains(q))
            |> List.map (fun s ->
                { Slug = s.Slug
                  Name = s.Name
                  Year = s.Year
                  PosterRef = s.PosterRef
                  MediaType = MediaType.Series })
        (movieResults @ seriesResults)
        |> List.sortBy (fun r -> r.Name.ToLowerInvariant())
        |> List.truncate 10

let view (model: Model) (dispatch: Msg -> unit) =
    let localResults = filterLibrary model.Query model.LibraryMovies model.LibrarySeries

    let headerExtra = [
        Daisy.input [
            prop.className "w-full mb-4"
            prop.placeholder "Search movies & series..."
            prop.value model.Query
            prop.autoFocus true
            prop.onChange (Query_changed >> dispatch)
            prop.onKeyDown (fun e ->
                if e.key = "Escape" then dispatch Close
            )
        ]
    ]

    let content = [
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
                            if List.isEmpty localResults then
                                Html.p [
                                    prop.className "text-sm text-base-content/40 py-2"
                                    prop.text "No matches in your library."
                                ]
                            else
                                Html.div [
                                    prop.className "space-y-1"
                                    prop.children [
                                        for result in localResults do
                                            Html.div [
                                                prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-200 cursor-pointer transition-colors"
                                                prop.onClick (fun _ -> dispatch (Navigate_to (result.Slug, result.MediaType)))
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
                                                                match result.MediaType with
                                                                | Movie -> Icons.movie ()
                                                                | Series -> Icons.tv ()
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
                                                        prop.text (
                                                            match result.MediaType with
                                                            | Movie -> "Movie"
                                                            | Series -> "Series"
                                                        )
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
                                                prop.onClick (fun _ -> dispatch (Import (result.TmdbId, result.MediaType)))
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
                                                            prop.children [
                                                                match result.MediaType with
                                                                | Movie -> Icons.movie ()
                                                                | Series -> Icons.tv ()
                                                            ]
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
                                                    Daisy.badge [
                                                        badge.sm
                                                        match result.MediaType with
                                                        | Movie -> badge.info
                                                        | Series -> badge.secondary
                                                        prop.text (
                                                            match result.MediaType with
                                                            | Movie -> "Movie"
                                                            | Series -> "Series"
                                                        )
                                                    ]
                                                    Daisy.button.button [
                                                        button.sm
                                                        button.primary
                                                        button.outline
                                                        prop.text "Import"
                                                        prop.onClick (fun e ->
                                                            e.stopPropagation()
                                                            dispatch (Import (result.TmdbId, result.MediaType))
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

    ModalPanel.viewCustom "Search" (fun () -> dispatch Close) headerExtra content []
