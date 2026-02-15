module Mediatheca.Client.Components.SearchModal

open Feliz
open Feliz.DaisyUI
open Fable.Core.JsInterop
open Mediatheca.Shared

type Model = {
    Query: string
    LibraryMovies: MovieListItem list
    LibrarySeries: SeriesListItem list
    LibraryGames: GameListItem list
    TmdbResults: TmdbSearchResult list
    RawgResults: RawgSearchResult list
    IsSearchingTmdb: bool
    IsSearchingRawg: bool
    IsImporting: bool
    Error: string option
    SearchVersion: int
}

type Msg =
    | Query_changed of string
    | Debounce_tmdb_expired of version: int
    | Tmdb_search_completed of TmdbSearchResult list
    | Tmdb_search_failed of string
    | Rawg_search_completed of RawgSearchResult list
    | Rawg_search_failed of string
    | Import of tmdbId: int * MediaType
    | Import_rawg of RawgSearchResult
    | Import_completed of Result<string * MediaType, string>
    | Navigate_to of slug: string * MediaType
    | Close

let init (movies: MovieListItem list) (series: SeriesListItem list) : Model = {
    Query = ""
    LibraryMovies = movies
    LibrarySeries = series
    LibraryGames = []
    TmdbResults = []
    RawgResults = []
    IsSearchingTmdb = false
    IsSearchingRawg = false
    IsImporting = false
    Error = None
    SearchVersion = 0
}

let initWithGames (movies: MovieListItem list) (series: SeriesListItem list) (games: GameListItem list) : Model = {
    Query = ""
    LibraryMovies = movies
    LibrarySeries = series
    LibraryGames = games
    TmdbResults = []
    RawgResults = []
    IsSearchingTmdb = false
    IsSearchingRawg = false
    IsImporting = false
    Error = None
    SearchVersion = 0
}

let filterLibrary (query: string) (movies: MovieListItem list) (series: SeriesListItem list) (games: GameListItem list) : LibrarySearchResult list =
    if query = "" then []
    else
        let q = query.ToLowerInvariant()
        let movieResults =
            movies
            |> List.filter (fun m -> m.Name.ToLowerInvariant().Contains(q))
            |> List.map (fun m ->
                { LibrarySearchResult.Slug = m.Slug
                  Name = m.Name
                  Year = m.Year
                  PosterRef = m.PosterRef
                  MediaType = MediaType.Movie })
        let seriesResults =
            series
            |> List.filter (fun s -> s.Name.ToLowerInvariant().Contains(q))
            |> List.map (fun s ->
                { LibrarySearchResult.Slug = s.Slug
                  Name = s.Name
                  Year = s.Year
                  PosterRef = s.PosterRef
                  MediaType = MediaType.Series })
        let gameResults =
            games
            |> List.filter (fun g -> g.Name.ToLowerInvariant().Contains(q))
            |> List.map (fun g ->
                { LibrarySearchResult.Slug = g.Slug
                  Name = g.Name
                  Year = g.Year
                  PosterRef = g.CoverRef
                  MediaType = MediaType.Game })
        (movieResults @ seriesResults @ gameResults)
        |> List.sortBy (fun r -> r.Name.ToLowerInvariant())
        |> List.truncate 10

let private renderTmdbItem (result: TmdbSearchResult) (isSelected: bool) (onImport: unit -> unit) =
    Html.div [
        if isSelected then prop.custom ("data-search-selected", "true")
        prop.className (
            "flex items-center gap-3 p-2 rounded-lg cursor-pointer transition-colors "
            + if isSelected then "bg-primary/15 ring-1 ring-primary/40"
              else "hover:bg-base-200"
        )
        prop.onClick (fun _ -> onImport ())
        prop.children [
            match result.PosterPath with
            | Some path ->
                Html.img [
                    prop.src $"https://image.tmdb.org/t/p/w92{path}"
                    prop.className "w-10 h-15 rounded object-cover flex-shrink-0"
                    prop.alt result.Title
                ]
            | None ->
                Html.div [
                    prop.className "w-10 h-15 rounded bg-base-300 flex items-center justify-center flex-shrink-0"
                    prop.children [
                        match result.MediaType with
                        | Movie -> Icons.movie ()
                        | Series -> Icons.tv ()
                        | Game -> Icons.gamepad ()
                    ]
                ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate"
                        prop.text result.Title
                    ]
                    match result.Year with
                    | Some y ->
                        Html.p [
                            prop.className "text-xs text-base-content/50"
                            prop.text (string y)
                        ]
                    | None -> ()
                ]
            ]
        ]
    ]

[<ReactComponent>]
let view (model: Model) (dispatch: Msg -> unit) =
    let selRow, setSelRow = React.useState(-1)
    let selCol, setSelCol = React.useState(Movie : MediaType)
    let inLibrary, setInLibrary = React.useState(false)

    let localResults = filterLibrary model.Query model.LibraryMovies model.LibrarySeries model.LibraryGames

    // Build lookup of library items by (lowercased name, year) to exclude from TMDB results
    let libraryMovieKeys =
        model.LibraryMovies
        |> List.map (fun m -> m.Name.ToLowerInvariant(), m.Year)
        |> Set.ofList
    let librarySeriesKeys =
        model.LibrarySeries
        |> List.map (fun s -> s.Name.ToLowerInvariant(), s.Year)
        |> Set.ofList
    let libraryGameKeys =
        model.LibraryGames
        |> List.map (fun g -> g.Name.ToLowerInvariant(), g.Year)
        |> Set.ofList
    let isInLibrary (r: TmdbSearchResult) =
        let key = r.Title.ToLowerInvariant(), r.Year |> Option.defaultValue 0
        match r.MediaType with
        | Movie -> libraryMovieKeys |> Set.contains key
        | Series -> librarySeriesKeys |> Set.contains key
        | Game -> false
    let isGameInLibrary (r: RawgSearchResult) =
        let key = r.Name.ToLowerInvariant(), r.Year |> Option.defaultValue 0
        libraryGameKeys |> Set.contains key

    let movieResults = model.TmdbResults |> List.filter (fun r -> r.MediaType = Movie && not (isInLibrary r))
    let seriesResults = model.TmdbResults |> List.filter (fun r -> r.MediaType = Series && not (isInLibrary r))
    let gameResults = model.RawgResults |> List.filter (fun r -> not (isGameInLibrary r))

    // Reset selection when search query changes
    React.useEffect((fun () ->
        setSelRow -1
        setSelCol Movie
        setInLibrary false
    ), [| box model.SearchVersion |])

    // Scroll selected item into view
    React.useEffect((fun () ->
        let el = Browser.Dom.document.querySelector("[data-search-selected='true']")
        if not (isNull el) then
            el?scrollIntoView({| block = "nearest" |})
    ), [| box selRow; box selCol; box inLibrary |])

    let handleKeyDown (e: Browser.Types.KeyboardEvent) =
        match e.key with
        | "ArrowDown" ->
            e.preventDefault()
            if selRow < 0 then
                // Nothing selected — enter library section first, then TMDB
                if not (List.isEmpty localResults) then setSelRow 0; setInLibrary true
                elif not (List.isEmpty movieResults) then setSelRow 0; setSelCol Movie; setInLibrary false
                elif not (List.isEmpty seriesResults) then setSelRow 0; setSelCol Series; setInLibrary false
            elif inLibrary then
                if selRow < List.length localResults - 1 then setSelRow (selRow + 1)
                else
                    // Past end of library — move to TMDB
                    if not (List.isEmpty movieResults) then setSelRow 0; setSelCol Movie; setInLibrary false
                    elif not (List.isEmpty seriesResults) then setSelRow 0; setSelCol Series; setInLibrary false
            else
                let colLen = (if selCol = Movie then movieResults else seriesResults) |> List.length
                if selRow < colLen - 1 then setSelRow (selRow + 1)
        | "ArrowUp" ->
            e.preventDefault()
            if inLibrary then
                if selRow > 0 then setSelRow (selRow - 1)
                elif selRow = 0 then setSelRow -1; setInLibrary false
            else
                if selRow > 0 then setSelRow (selRow - 1)
                elif selRow = 0 then
                    // At top of TMDB — move back to library if it exists
                    if not (List.isEmpty localResults) then
                        setSelRow (List.length localResults - 1); setInLibrary true
                    else setSelRow -1
        | "ArrowLeft" ->
            if selRow >= 0 && not inLibrary && not (List.isEmpty movieResults) then
                e.preventDefault()
                setSelCol Movie
                setSelRow (min selRow (List.length movieResults - 1))
        | "ArrowRight" ->
            if selRow >= 0 && not inLibrary && not (List.isEmpty seriesResults) then
                e.preventDefault()
                setSelCol Series
                setSelRow (min selRow (List.length seriesResults - 1))
        | "Enter" ->
            if selRow >= 0 then
                e.preventDefault()
                if inLibrary then
                    match localResults |> List.tryItem selRow with
                    | Some r -> dispatch (Navigate_to (r.Slug, r.MediaType))
                    | None -> ()
                else
                    let results = if selCol = Movie then movieResults else seriesResults
                    match results |> List.tryItem selRow with
                    | Some r -> dispatch (Import (r.TmdbId, r.MediaType))
                    | None -> ()
        | "Escape" -> dispatch Close
        | _ -> ()

    let renderColumn (title: string) (icon: ReactElement) (results: TmdbSearchResult list) (colType: MediaType) =
        Html.div [
            prop.className "flex-1 min-w-0"
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-2 mb-2"
                    prop.children [
                        Html.span [ prop.className "w-5 h-5 text-base-content/50"; prop.children [ icon ] ]
                        Html.h5 [
                            prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50")
                            prop.text title
                        ]
                        if not (List.isEmpty results) then
                            Daisy.badge [
                                badge.sm
                                badge.ghost
                                prop.text (string (List.length results))
                            ]
                    ]
                ]
                if List.isEmpty results then
                    Html.p [
                        prop.className "text-xs text-base-content/40 py-4 text-center"
                        prop.text "No results"
                    ]
                else
                    Html.div [
                        prop.className "space-y-1"
                        prop.children [
                            for (idx, result) in results |> List.mapi (fun i r -> (i, r)) do
                                let isSelected = selRow = idx && selCol = colType && selRow >= 0
                                renderTmdbItem result isSelected (fun () -> dispatch (Import (result.TmdbId, result.MediaType)))
                        ]
                    ]
            ]
        ]

    Html.div [
        prop.className Mediatheca.Client.DesignSystem.modalContainer
        prop.children [
            Html.div [
                prop.className Mediatheca.Client.DesignSystem.modalBackdrop
                prop.onClick (fun _ -> dispatch Close)
            ]
            Html.div [
                prop.className ("relative w-full max-w-4xl mx-4 max-h-[70vh] flex flex-col " + Mediatheca.Client.DesignSystem.modalPanel)
                prop.children [
                    // Header with search input
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
                                ]
                            ]
                            Daisy.input [
                                prop.className "w-full mb-4 border-transparent focus:border-transparent focus:outline-0 focus:bg-base-200/60 transition-colors"
                                prop.placeholder "Search movies, series & games..."
                                prop.value model.Query
                                prop.autoFocus true
                                prop.onChange (Query_changed >> dispatch)
                                prop.onKeyDown handleKeyDown
                            ]
                        ]
                    ]
                    // Scrollable content
                    Html.div [
                        prop.className "flex-1 overflow-y-auto px-5 pb-5"
                        prop.children [
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
                                        Html.p [ prop.text "Start typing to search your library, TMDB, and RAWG." ]
                                    ]
                                ]
                            else
                                Html.div [
                                    prop.className "space-y-4"
                                    prop.children [
                                        // In Your Library
                                        if not (List.isEmpty localResults) then
                                            Html.div [
                                                prop.children [
                                                    Html.h4 [
                                                        prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50 mb-2")
                                                        prop.text "In Your Library"
                                                    ]
                                                    Html.div [
                                                        prop.className "space-y-1"
                                                        prop.children [
                                                            for (idx, result) in localResults |> List.mapi (fun i r -> (i, r)) do
                                                                let isSelected = inLibrary && selRow = idx
                                                                Html.div [
                                                                    if isSelected then prop.custom ("data-search-selected", "true")
                                                                    prop.className (
                                                                        "flex items-center gap-3 p-2 rounded-lg cursor-pointer transition-colors "
                                                                        + if isSelected then "bg-primary/15 ring-1 ring-primary/40"
                                                                          else "hover:bg-base-200"
                                                                    )
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
                                                                                    | Game -> Icons.gamepad ()
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
                                                                                    prop.className "text-xs text-base-content/50"
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
                                                                                | Game -> "Game"
                                                                            )
                                                                        ]
                                                                    ]
                                                                ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        // TMDB Results — two columns
                                        Html.div [
                                            prop.children [
                                                Html.h4 [
                                                    prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50 mb-3")
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
                                                elif List.isEmpty movieResults && List.isEmpty seriesResults then
                                                    Html.p [
                                                        prop.className "text-sm text-base-content/40 py-2"
                                                        prop.text "No TMDB results yet."
                                                    ]
                                                else
                                                    Html.div [
                                                        prop.className "flex gap-4"
                                                        prop.children [
                                                            renderColumn "Movies" (Icons.movie ()) movieResults Movie
                                                            Html.div [ prop.className "w-px bg-base-content/10 self-stretch" ]
                                                            renderColumn "Series" (Icons.tv ()) seriesResults Series
                                                        ]
                                                    ]
                                            ]
                                        ]
                                        // RAWG Results — games
                                        Html.div [
                                            prop.children [
                                                Html.h4 [
                                                    prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50 mb-3")
                                                    prop.text "RAWG Results"
                                                ]
                                                if model.IsSearchingRawg then
                                                    Html.div [
                                                        prop.className "flex items-center gap-2 py-3 text-base-content/40"
                                                        prop.children [
                                                            Daisy.loading [ loading.dots; loading.sm ]
                                                            Html.span [ prop.text "Searching RAWG..." ]
                                                        ]
                                                    ]
                                                elif List.isEmpty gameResults then
                                                    Html.p [
                                                        prop.className "text-sm text-base-content/40 py-2"
                                                        prop.text "No RAWG results yet."
                                                    ]
                                                else
                                                    Html.div [
                                                        prop.className "space-y-1"
                                                        prop.children [
                                                            Html.div [
                                                                prop.className "flex items-center gap-2 mb-2"
                                                                prop.children [
                                                                    Html.span [ prop.className "w-5 h-5 text-base-content/50"; prop.children [ Icons.gamepad () ] ]
                                                                    Html.h5 [
                                                                        prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50")
                                                                        prop.text "Games"
                                                                    ]
                                                                    Daisy.badge [
                                                                        badge.sm
                                                                        badge.ghost
                                                                        prop.text (string (List.length gameResults))
                                                                    ]
                                                                ]
                                                            ]
                                                            for result in gameResults do
                                                                Html.div [
                                                                    prop.className "flex items-center gap-3 p-2 rounded-lg cursor-pointer transition-colors hover:bg-base-200"
                                                                    prop.onClick (fun _ -> dispatch (Import_rawg result))
                                                                    prop.children [
                                                                        match result.BackgroundImage with
                                                                        | Some img ->
                                                                            Html.img [
                                                                                prop.src img
                                                                                prop.className "w-10 h-15 rounded object-cover flex-shrink-0"
                                                                                prop.alt result.Name
                                                                            ]
                                                                        | None ->
                                                                            Html.div [
                                                                                prop.className "w-10 h-15 rounded bg-base-300 flex items-center justify-center flex-shrink-0"
                                                                                prop.children [ Icons.gamepad () ]
                                                                            ]
                                                                        Html.div [
                                                                            prop.className "flex-1 min-w-0"
                                                                            prop.children [
                                                                                Html.p [
                                                                                    prop.className "font-semibold text-sm truncate"
                                                                                    prop.text result.Name
                                                                                ]
                                                                                match result.Year with
                                                                                | Some y ->
                                                                                    Html.p [
                                                                                        prop.className "text-xs text-base-content/50"
                                                                                        prop.text (string y)
                                                                                    ]
                                                                                | None -> ()
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
                    // Keyboard hints footer
                    Html.div [
                        prop.className "flex items-center gap-4 px-5 py-3 border-t border-base-content/10 text-xs text-base-content/40"
                        prop.children [
                            Html.span [ prop.text "↑↓ navigate" ]
                            Html.span [ prop.text "←→ switch" ]
                            Html.span [ prop.text "↵ select" ]
                            Html.span [ prop.text "esc close" ]
                        ]
                    ]
                ]
            ]
        ]
    ]
