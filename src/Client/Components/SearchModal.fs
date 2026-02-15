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
    ActiveTab: MediaType
}

type Msg =
    | Query_changed of string
    | Tab_changed of MediaType
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
    ActiveTab = Movie
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
    ActiveTab = Movie
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

let private renderRawgItem (result: RawgSearchResult) (isSelected: bool) (onImport: unit -> unit) =
    Html.div [
        if isSelected then prop.custom ("data-search-selected", "true")
        prop.className (
            "flex items-center gap-3 p-2 rounded-lg cursor-pointer transition-colors "
            + if isSelected then "bg-primary/15 ring-1 ring-primary/40"
              else "hover:bg-base-200"
        )
        prop.onClick (fun _ -> onImport ())
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

[<ReactComponent>]
let view (model: Model) (dispatch: Msg -> unit) =
    let selRow, setSelRow = React.useState(-1)
    let inLibrary, setInLibrary = React.useState(false)
    let activeTab = model.ActiveTab

    let localResults = filterLibrary model.Query model.LibraryMovies model.LibrarySeries model.LibraryGames

    // Build lookup of library items by (lowercased name, year) to exclude from external results
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

    let tabResultCount (tab: MediaType) =
        match tab with
        | Movie -> List.length movieResults
        | Series -> List.length seriesResults
        | Game -> List.length gameResults

    let tabOrder = [| Movie; Series; Game |]

    let firstTabWithResults () =
        tabOrder |> Array.tryFind (fun t -> tabResultCount t > 0)

    // Reset selection when search query changes (but preserve active tab)
    React.useEffect((fun () ->
        setSelRow -1
        setInLibrary false
    ), [| box model.SearchVersion |])

    // Scroll selected item into view
    React.useEffect((fun () ->
        let el = Browser.Dom.document.querySelector("[data-search-selected='true']")
        if not (isNull el) then
            el?scrollIntoView({| block = "nearest" |})
    ), [| box selRow; box activeTab; box inLibrary |])

    let handleKeyDown (e: Browser.Types.KeyboardEvent) =
        match e.key with
        | "Tab" ->
            e.preventDefault()
            let idx = tabOrder |> Array.findIndex ((=) activeTab)
            let nextIdx =
                if e.shiftKey then (idx + tabOrder.Length - 1) % tabOrder.Length
                else (idx + 1) % tabOrder.Length
            let nextTab = tabOrder.[nextIdx]
            dispatch (Tab_changed nextTab)
            if not inLibrary && selRow >= 0 then
                let len = tabResultCount nextTab
                setSelRow (if len > 0 then min selRow (len - 1) else -1)
        | "ArrowDown" ->
            e.preventDefault()
            if selRow < 0 then
                // Nothing selected — enter library first, then active tab
                if not (List.isEmpty localResults) then setSelRow 0; setInLibrary true
                elif tabResultCount activeTab > 0 then setSelRow 0; setInLibrary false
                else
                    match firstTabWithResults () with
                    | Some t -> dispatch (Tab_changed t); setSelRow 0; setInLibrary false
                    | None -> ()
            elif inLibrary then
                if selRow < List.length localResults - 1 then setSelRow (selRow + 1)
                else
                    // Past end of library — enter active tab
                    if tabResultCount activeTab > 0 then setSelRow 0; setInLibrary false
                    else
                        match firstTabWithResults () with
                        | Some t -> dispatch (Tab_changed t); setSelRow 0; setInLibrary false
                        | None -> ()
            else
                let len = tabResultCount activeTab
                if selRow < len - 1 then setSelRow (selRow + 1)
        | "ArrowUp" ->
            e.preventDefault()
            if inLibrary then
                if selRow > 0 then setSelRow (selRow - 1)
                elif selRow = 0 then setSelRow -1; setInLibrary false
            else
                if selRow > 0 then setSelRow (selRow - 1)
                elif selRow = 0 then
                    if not (List.isEmpty localResults) then
                        setSelRow (List.length localResults - 1); setInLibrary true
                    else setSelRow -1
        | "Enter" ->
            if selRow >= 0 then
                e.preventDefault()
                if inLibrary then
                    match localResults |> List.tryItem selRow with
                    | Some r -> dispatch (Navigate_to (r.Slug, r.MediaType))
                    | None -> ()
                else
                    match activeTab with
                    | Game ->
                        match gameResults |> List.tryItem selRow with
                        | Some r -> dispatch (Import_rawg r)
                        | None -> ()
                    | _ ->
                        let results = if activeTab = Movie then movieResults else seriesResults
                        match results |> List.tryItem selRow with
                        | Some r -> dispatch (Import (r.TmdbId, r.MediaType))
                        | None -> ()
        | "Escape" -> dispatch Close
        | _ -> ()

    let tabLabel (t: MediaType) =
        match t with Movie -> "Movies" | Series -> "Series" | Game -> "Games"

    let tabIcon (t: MediaType) =
        match t with Movie -> Icons.movie () | Series -> Icons.tv () | Game -> Icons.gamepad ()

    let isTabLoading (t: MediaType) =
        match t with
        | Movie | Series -> model.IsSearchingTmdb
        | Game -> model.IsSearchingRawg

    let renderTabContent () =
        if isTabLoading activeTab then
            Html.div [
                prop.className "flex items-center gap-2 py-3 text-base-content/40"
                prop.children [
                    Daisy.loading [ loading.dots; loading.sm ]
                    Html.span [
                        prop.text (match activeTab with Game -> "Searching RAWG..." | _ -> "Searching TMDB...")
                    ]
                ]
            ]
        else
            match activeTab with
            | Movie | Series ->
                let results = if activeTab = Movie then movieResults else seriesResults
                if List.isEmpty results then
                    Html.p [
                        prop.className "text-sm text-base-content/40 py-4 text-center"
                        prop.text "No results"
                    ]
                else
                    Html.div [
                        prop.className "space-y-1"
                        prop.children [
                            for (idx, result) in results |> List.mapi (fun i r -> (i, r)) do
                                let isSelected = not inLibrary && selRow = idx
                                renderTmdbItem result isSelected (fun () ->
                                    dispatch (Import (result.TmdbId, result.MediaType)))
                        ]
                    ]
            | Game ->
                if List.isEmpty gameResults then
                    Html.p [
                        prop.className "text-sm text-base-content/40 py-4 text-center"
                        prop.text "No results"
                    ]
                else
                    Html.div [
                        prop.className "space-y-1"
                        prop.children [
                            for (idx, result) in gameResults |> List.mapi (fun i r -> (i, r)) do
                                let isSelected = not inLibrary && selRow = idx
                                renderRawgItem result isSelected (fun () -> dispatch (Import_rawg result))
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
                prop.className ("relative w-full max-w-2xl mx-4 max-h-[70vh] flex flex-col " + Mediatheca.Client.DesignSystem.modalPanel)
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
                                        // Discover — tabbed external results
                                        Html.div [
                                            prop.children [
                                                Html.h4 [
                                                    prop.className (Mediatheca.Client.DesignSystem.subtitle + " text-base-content/50 mb-3")
                                                    prop.text "Discover"
                                                ]
                                                // Tab bar
                                                Html.div [
                                                    prop.className "flex gap-1 mb-3"
                                                    prop.children [
                                                        for tab in tabOrder do
                                                            let isActive = tab = activeTab
                                                            Html.button [
                                                                prop.className (
                                                                    "flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors "
                                                                    + if isActive then "bg-primary/15 text-primary"
                                                                      else "text-base-content/50 hover:text-base-content/70 hover:bg-base-200/50"
                                                                )
                                                                prop.onClick (fun _ ->
                                                                    dispatch (Tab_changed tab)
                                                                    if not inLibrary && selRow >= 0 then
                                                                        let len = tabResultCount tab
                                                                        setSelRow (if len > 0 then min selRow (len - 1) else -1)
                                                                )
                                                                prop.children [
                                                                    Html.span [
                                                                        prop.className "w-5 h-5 [&>svg]:w-5 [&>svg]:h-5"
                                                                        prop.children [ tabIcon tab ]
                                                                    ]
                                                                    Html.span [ prop.text (tabLabel tab) ]
                                                                ]
                                                            ]
                                                    ]
                                                ]
                                                // Active tab content
                                                renderTabContent ()
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
                            Html.span [ prop.text "tab switch" ]
                            Html.span [ prop.text "↵ select" ]
                            Html.span [ prop.text "esc close" ]
                        ]
                    ]
                ]
            ]
        ]
    ]
