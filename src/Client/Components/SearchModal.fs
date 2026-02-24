module Mediatheca.Client.Components.SearchModal

open Feliz
open Feliz.DaisyUI
open Fable.Core.JsInterop
open Mediatheca.Shared
open Mediatheca.Client

type SearchTab = Library | Movies | Series | Games

type HoverPreviewState =
    | NotHovering
    | Loading
    | LoadedTmdb of TmdbPreviewData
    | LoadedRawg of RawgPreviewData
    | LoadedLibraryMovie of MovieDetail
    | LoadedLibrarySeries of SeriesDetail
    | LoadedLibraryGame of GameDetail
    | Failed

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
    ActiveTab: SearchTab
    HoverTarget: string option
    HoverPreview: HoverPreviewState
    HoverVersion: int
    PreviewCache: Map<string, HoverPreviewState>
}

type Msg =
    | Query_changed of string
    | Tab_changed of SearchTab
    | Debounce_tmdb_expired of version: int
    | Tmdb_search_completed of TmdbSearchResult list
    | Tmdb_search_failed of string
    | Rawg_search_completed of RawgSearchResult list
    | Rawg_search_failed of string
    | Import of tmdbId: int * MediaType
    | Import_rawg of RawgSearchResult
    | Import_completed of Result<string * MediaType, string>
    | Navigate_to of slug: string * MediaType
    | Hover_start of key: string * version: int
    | Hover_preview_tmdb_loaded of key: string * TmdbPreviewData option
    | Hover_preview_rawg_loaded of key: string * RawgPreviewData option
    | Hover_preview_library_movie_loaded of key: string * MovieDetail option
    | Hover_preview_library_series_loaded of key: string * SeriesDetail option
    | Hover_preview_library_game_loaded of key: string * GameDetail option
    | Hover_clear
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
    ActiveTab = Library
    HoverTarget = None
    HoverPreview = NotHovering
    HoverVersion = 0
    PreviewCache = Map.empty
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
    ActiveTab = Library
    HoverTarget = None
    HoverPreview = NotHovering
    HoverVersion = 0
    PreviewCache = Map.empty
}

let filterLibrary (query: string) (movies: MovieListItem list) (series: SeriesListItem list) (games: GameListItem list) : LibrarySearchResult list =
    if query = "" then []
    else
        let searchQuery, yearFilter = FuzzyMatch.extractYear query
        let movieItems =
            movies
            |> List.map (fun m ->
                (m.Name, { LibrarySearchResult.Slug = m.Slug
                           Name = m.Name
                           Year = m.Year
                           PosterRef = m.PosterRef
                           MediaType = MediaType.Movie }))
        let seriesItems =
            series
            |> List.map (fun s ->
                (s.Name, { LibrarySearchResult.Slug = s.Slug
                           Name = s.Name
                           Year = s.Year
                           PosterRef = s.PosterRef
                           MediaType = MediaType.Series }))
        let gameItems =
            games
            |> List.map (fun g ->
                (g.Name, { LibrarySearchResult.Slug = g.Slug
                           Name = g.Name
                           Year = g.Year
                           PosterRef = g.CoverRef
                           MediaType = MediaType.Game }))
        let allItems = movieItems @ seriesItems @ gameItems
        let results = FuzzyMatch.fuzzyMatch 20 searchQuery allItems
        match yearFilter with
        | Some year ->
            results
            |> List.map (fun (score, item) ->
                let adjustedScore = if item.Year = year then score * 0.5 else score
                (adjustedScore, item))
            |> List.sortBy fst
            |> List.map snd
            |> List.truncate 20
        | None ->
            results |> List.map snd

let private truncateText (maxLen: int) (text: string) =
    if text.Length <= maxLen then text
    else text.[..maxLen-1] + "..."

// ── Poster Grid Item ──

let private renderPosterCard
    (imgSrc: string option)
    (fallbackIcon: unit -> ReactElement)
    (name: string)
    (year: string)
    (badge: ReactElement option)
    (isSelected: bool)
    (onClick: unit -> unit)
    (onMouseEnter: unit -> unit)
    (onMouseLeave: unit -> unit) =
    Html.div [
        if isSelected then prop.custom ("data-search-selected", "true")
        prop.className (
            "relative cursor-pointer group transition-all duration-200 "
            + if isSelected then "ring-2 ring-primary rounded-lg scale-[1.02]"
              else "hover:scale-[1.02]"
        )
        prop.onClick (fun _ -> onClick ())
        prop.onMouseEnter (fun _ -> onMouseEnter ())
        prop.onMouseLeave (fun _ -> onMouseLeave ())
        prop.children [
            // Poster image
            Html.div [
                prop.className "aspect-[2/3] rounded-lg overflow-hidden bg-base-300 relative"
                prop.children [
                    match imgSrc with
                    | Some src ->
                        Html.img [
                            prop.src src
                            prop.className "w-full h-full object-cover"
                            prop.alt name
                            prop.custom ("loading", "lazy")
                        ]
                    | None ->
                        Html.div [
                            prop.className "w-full h-full flex items-center justify-center text-base-content/30"
                            prop.children [
                                Html.span [
                                    prop.className "w-10 h-10 [&>svg]:w-10 [&>svg]:h-10"
                                    prop.children [ fallbackIcon () ]
                                ]
                            ]
                        ]
                    // Name overlay at bottom
                    Html.div [
                        prop.className "absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/70 via-black/30 to-transparent p-2 pt-6"
                        prop.children [
                            Html.p [
                                prop.className "text-white text-xs font-medium line-clamp-2 leading-tight"
                                prop.text name
                            ]
                            Html.p [
                                prop.className "text-white/60 text-[10px]"
                                prop.text year
                            ]
                        ]
                    ]
                    // Badge in corner
                    match badge with
                    | Some b ->
                        Html.div [
                            prop.className "absolute top-1.5 right-1.5"
                            prop.children [ b ]
                        ]
                    | None -> ()
                ]
            ]
        ]
    ]

// ── Hover Preview Popover ──

let private renderPreviewPopover (preview: HoverPreviewState) =
    match preview with
    | NotHovering | Failed -> Html.none
    | Loading ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.div [
                    prop.className "flex justify-center py-8"
                    prop.children [ Daisy.loading [ loading.dots; loading.sm ] ]
                ]
            ]
        ]
    | LoadedTmdb data ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 max-h-[50vh] overflow-y-auto p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.h4 [
                    prop.className "font-display text-sm font-bold uppercase tracking-wider mb-1"
                    prop.text data.Title
                ]
                match data.Year with
                | Some y ->
                    Html.p [
                        prop.className "text-xs text-base-content/50 mb-2"
                        prop.children [
                            Html.span [ prop.text (string y) ]
                            match data.Runtime with
                            | Some rt ->
                                Html.span [ prop.text $" \u00B7 {rt} min" ]
                            | None -> ()
                            match data.SeasonCount with
                            | Some sc ->
                                let suffix = if sc > 1 then "s" else ""
                                Html.span [ prop.text $" \u00B7 {sc} season{suffix}" ]
                            | None -> ()
                        ]
                    ]
                | None -> ()
                match data.Rating with
                | Some r when r > 0.0 ->
                    Html.div [
                        prop.className "flex items-center gap-1 mb-2"
                        prop.children [
                            Html.span [ prop.className "text-yellow-400 text-xs"; prop.text "\u2605" ]
                            Html.span [ prop.className "text-xs text-base-content/70"; prop.text (sprintf "%.1f" r) ]
                        ]
                    ]
                | _ -> ()
                if not (List.isEmpty data.Genres) then
                    Html.div [
                        prop.className "flex flex-wrap gap-1 mb-2"
                        prop.children [
                            for g in data.Genres |> List.truncate 4 do
                                Daisy.badge [
                                    badge.ghost
                                    badge.xs
                                    prop.text g
                                ]
                        ]
                    ]
                if data.Overview <> "" then
                    Html.p [
                        prop.className "text-xs text-base-content/70 mb-2 line-clamp-3"
                        prop.text data.Overview
                    ]
                if not (List.isEmpty data.Cast) then
                    Html.div [
                        prop.className "text-xs text-base-content/50"
                        prop.children [
                            Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Cast: " ]
                            Html.span [ prop.text (data.Cast |> String.concat ", ") ]
                        ]
                    ]
            ]
        ]
    | LoadedRawg data ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 max-h-[50vh] overflow-y-auto p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.h4 [
                    prop.className "font-display text-sm font-bold uppercase tracking-wider mb-1"
                    prop.text data.Name
                ]
                Html.p [
                    prop.className "text-xs text-base-content/50 mb-2"
                    prop.children [
                        match data.Year with
                        | Some y -> Html.span [ prop.text (string y) ]
                        | None -> ()
                        match data.Metacritic with
                        | Some mc ->
                            Html.span [ prop.text $" \u00B7 Metacritic: {mc}" ]
                        | None -> ()
                    ]
                ]
                match data.Rating with
                | Some r when r > 0.0 ->
                    Html.div [
                        prop.className "flex items-center gap-1 mb-2"
                        prop.children [
                            Html.span [ prop.className "text-yellow-400 text-xs"; prop.text "\u2605" ]
                            Html.span [ prop.className "text-xs text-base-content/70"; prop.text (sprintf "%.1f" r) ]
                        ]
                    ]
                | _ -> ()
                if not (List.isEmpty data.Genres) then
                    Html.div [
                        prop.className "flex flex-wrap gap-1 mb-2"
                        prop.children [
                            for g in data.Genres |> List.truncate 4 do
                                Daisy.badge [
                                    badge.ghost
                                    badge.xs
                                    prop.text g
                                ]
                        ]
                    ]
                if data.Description <> "" then
                    Html.p [
                        prop.className "text-xs text-base-content/70 mb-2 line-clamp-3"
                        prop.text (truncateText 300 data.Description)
                    ]
                if not (List.isEmpty data.Platforms) then
                    Html.div [
                        prop.className "text-xs text-base-content/50 mb-2"
                        prop.children [
                            Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Platforms: " ]
                            Html.span [ prop.text (data.Platforms |> List.truncate 5 |> String.concat ", ") ]
                        ]
                    ]
                if not (List.isEmpty data.Screenshots) then
                    Html.div [
                        prop.className "flex gap-1 mt-2"
                        prop.children [
                            for ss in data.Screenshots |> List.truncate 2 do
                                Html.img [
                                    prop.src ss
                                    prop.className "w-1/2 rounded object-cover aspect-video"
                                    prop.custom ("loading", "lazy")
                                ]
                        ]
                    ]
            ]
        ]
    | LoadedLibraryMovie data ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 max-h-[50vh] overflow-y-auto p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.h4 [
                    prop.className "font-display text-sm font-bold uppercase tracking-wider mb-1"
                    prop.text data.Name
                ]
                Html.p [
                    prop.className "text-xs text-base-content/50 mb-2"
                    prop.children [
                        Html.span [ prop.text (string data.Year) ]
                        match data.Runtime with
                        | Some rt -> Html.span [ prop.text $" \u00B7 {rt} min" ]
                        | None -> ()
                    ]
                ]
                match data.TmdbRating with
                | Some r when r > 0.0 ->
                    Html.div [
                        prop.className "flex items-center gap-1 mb-2"
                        prop.children [
                            Html.span [ prop.className "text-yellow-400 text-xs"; prop.text "\u2605" ]
                            Html.span [ prop.className "text-xs text-base-content/70"; prop.text (sprintf "%.1f" r) ]
                        ]
                    ]
                | _ -> ()
                if not (List.isEmpty data.Genres) then
                    Html.div [
                        prop.className "flex flex-wrap gap-1 mb-2"
                        prop.children [
                            for g in data.Genres |> List.truncate 4 do
                                Daisy.badge [ badge.ghost; badge.xs; prop.text g ]
                        ]
                    ]
                if data.Overview <> "" then
                    Html.p [
                        prop.className "text-xs text-base-content/70 mb-2 line-clamp-3"
                        prop.text data.Overview
                    ]
                if not (List.isEmpty data.Cast) then
                    Html.div [
                        prop.className "text-xs text-base-content/50"
                        prop.children [
                            Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Cast: " ]
                            Html.span [ prop.text (data.Cast |> List.truncate 5 |> List.map (fun c -> c.Name) |> String.concat ", ") ]
                        ]
                    ]
            ]
        ]
    | LoadedLibrarySeries data ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 max-h-[50vh] overflow-y-auto p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.h4 [
                    prop.className "font-display text-sm font-bold uppercase tracking-wider mb-1"
                    prop.text data.Name
                ]
                Html.p [
                    prop.className "text-xs text-base-content/50 mb-2"
                    prop.children [
                        Html.span [ prop.text (string data.Year) ]
                        let suffix = if data.Seasons.Length > 1 then "s" else ""
                        Html.span [ prop.text $" \u00B7 {data.Seasons.Length} season{suffix}" ]
                    ]
                ]
                match data.TmdbRating with
                | Some r when r > 0.0 ->
                    Html.div [
                        prop.className "flex items-center gap-1 mb-2"
                        prop.children [
                            Html.span [ prop.className "text-yellow-400 text-xs"; prop.text "\u2605" ]
                            Html.span [ prop.className "text-xs text-base-content/70"; prop.text (sprintf "%.1f" r) ]
                        ]
                    ]
                | _ -> ()
                if not (List.isEmpty data.Genres) then
                    Html.div [
                        prop.className "flex flex-wrap gap-1 mb-2"
                        prop.children [
                            for g in data.Genres |> List.truncate 4 do
                                Daisy.badge [ badge.ghost; badge.xs; prop.text g ]
                        ]
                    ]
                if data.Overview <> "" then
                    Html.p [
                        prop.className "text-xs text-base-content/70 mb-2 line-clamp-3"
                        prop.text data.Overview
                    ]
                if not (List.isEmpty data.Cast) then
                    Html.div [
                        prop.className "text-xs text-base-content/50"
                        prop.children [
                            Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Cast: " ]
                            Html.span [ prop.text (data.Cast |> List.truncate 5 |> List.map (fun c -> c.Name) |> String.concat ", ") ]
                        ]
                    ]
            ]
        ]
    | LoadedLibraryGame data ->
        Html.div [
            prop.className "absolute right-0 top-0 z-[60] w-80 max-h-[50vh] overflow-y-auto p-4 bg-base-100/65 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 shadow-xl rounded-xl animate-fade-in"
            prop.style [ style.custom ("boxShadow", "inset 0 1px 0 0 oklch(100% 0 0 / 0.08), 0 20px 25px -5px rgb(0 0 0 / 0.1)") ]
            prop.children [
                Html.h4 [
                    prop.className "font-display text-sm font-bold uppercase tracking-wider mb-1"
                    prop.text data.Name
                ]
                Html.p [
                    prop.className "text-xs text-base-content/50 mb-2"
                    prop.children [
                        Html.span [ prop.text (string data.Year) ]
                        match data.HltbHours with
                        | Some h -> Html.span [ prop.text $" \u00B7 ~{h}h" ]
                        | None -> ()
                    ]
                ]
                match data.RawgRating with
                | Some r when r > 0.0 ->
                    Html.div [
                        prop.className "flex items-center gap-1 mb-2"
                        prop.children [
                            Html.span [ prop.className "text-yellow-400 text-xs"; prop.text "\u2605" ]
                            Html.span [ prop.className "text-xs text-base-content/70"; prop.text (sprintf "%.1f" r) ]
                        ]
                    ]
                | _ -> ()
                if not (List.isEmpty data.Genres) then
                    Html.div [
                        prop.className "flex flex-wrap gap-1 mb-2"
                        prop.children [
                            for g in data.Genres |> List.truncate 4 do
                                Daisy.badge [ badge.ghost; badge.xs; prop.text g ]
                        ]
                    ]
                if data.Description <> "" then
                    Html.p [
                        prop.className "text-xs text-base-content/70 mb-2 line-clamp-3"
                        prop.text (truncateText 300 data.Description)
                    ]
            ]
        ]

[<ReactComponent>]
let view (model: Model) (dispatch: Msg -> unit) =
    let selIdx, setSelIdx = React.useState(-1)
    let hoverTimerRef = React.useRef(None : int option)
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
        | MediaType.Movie -> libraryMovieKeys |> Set.contains key
        | MediaType.Series -> librarySeriesKeys |> Set.contains key
        | MediaType.Game -> false
    let isGameInLibrary (r: RawgSearchResult) =
        let key = r.Name.ToLowerInvariant(), r.Year |> Option.defaultValue 0
        libraryGameKeys |> Set.contains key

    let movieResults = model.TmdbResults |> List.filter (fun r -> r.MediaType = MediaType.Movie && not (isInLibrary r))
    let seriesResults = model.TmdbResults |> List.filter (fun r -> r.MediaType = MediaType.Series && not (isInLibrary r))
    let gameResults = model.RawgResults |> List.filter (fun r -> not (isGameInLibrary r))

    let tabResultCount (tab: SearchTab) =
        match tab with
        | Library -> List.length localResults
        | Movies -> List.length movieResults
        | Series -> List.length seriesResults
        | Games -> List.length gameResults

    let cols = 4
    let tabOrder = [| Library; Movies; Series; Games |]

    // Reset selection when search query changes
    React.useEffect((fun () ->
        setSelIdx -1
        dispatch Hover_clear
    ), [| box model.SearchVersion |])

    // Scroll selected item into view
    React.useEffect((fun () ->
        let el = Browser.Dom.document.querySelector("[data-search-selected='true']")
        if not (isNull el) then
            el?scrollIntoView({| block = "nearest" |})
    ), [| box selIdx; box activeTab |])

    // Cleanup hover timer on unmount
    React.useEffectOnce(fun () ->
        { new System.IDisposable with
            member _.Dispose() =
                match hoverTimerRef.current with
                | Some tid -> emitJsExpr tid "clearTimeout($0)"
                | None -> () }
    )

    let startHover (key: string) =
        // Cancel any existing timer
        match hoverTimerRef.current with
        | Some tid -> emitJsExpr tid "clearTimeout($0)"
        | None -> ()
        // Check cache first
        match model.PreviewCache |> Map.tryFind key with
        | Some cached ->
            // Instantly show cached preview
            let newVersion = model.HoverVersion + 1
            dispatch (Hover_start (key, newVersion))
        | None ->
            // Start 500ms timer
            let newVersion = model.HoverVersion + 1
            let tid: int = emitJsExpr (fun () -> dispatch (Hover_start (key, newVersion))) "setTimeout($0, 500)"
            hoverTimerRef.current <- Some tid

    let cancelHover () =
        match hoverTimerRef.current with
        | Some tid -> emitJsExpr tid "clearTimeout($0)"
        | None -> ()
        hoverTimerRef.current <- None
        dispatch Hover_clear

    // Trigger hover preview for keyboard-selected items
    React.useEffect((fun () ->
        if selIdx >= 0 then
            let key =
                match activeTab with
                | Library ->
                    localResults |> List.tryItem selIdx |> Option.map (fun r ->
                        match r.MediaType with
                        | MediaType.Movie -> $"lib:movie:{r.Slug}"
                        | MediaType.Series -> $"lib:series:{r.Slug}"
                        | MediaType.Game -> $"lib:game:{r.Slug}")
                | Movies ->
                    movieResults |> List.tryItem selIdx |> Option.map (fun r -> $"tmdb:movie:{r.TmdbId}")
                | Series ->
                    seriesResults |> List.tryItem selIdx |> Option.map (fun r -> $"tmdb:series:{r.TmdbId}")
                | Games ->
                    gameResults |> List.tryItem selIdx |> Option.map (fun r -> $"rawg:{r.RawgId}")
            match key with
            | Some k -> startHover k
            | None -> ()
        else
            cancelHover ()
    ), [| box selIdx; box activeTab |])

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
            let len = tabResultCount nextTab
            if selIdx >= 0 then
                setSelIdx (if len > 0 then min selIdx (len - 1) else -1)
        | "ArrowDown" ->
            e.preventDefault()
            let len = tabResultCount activeTab
            if selIdx < 0 then
                if len > 0 then setSelIdx 0
            else
                let newIdx = selIdx + cols
                if newIdx < len then setSelIdx newIdx
        | "ArrowUp" ->
            e.preventDefault()
            if selIdx >= cols then setSelIdx (selIdx - cols)
            elif selIdx >= 0 && selIdx < cols then setSelIdx -1
        | "ArrowRight" ->
            e.preventDefault()
            let len = tabResultCount activeTab
            if selIdx >= 0 && selIdx < len - 1 then setSelIdx (selIdx + 1)
        | "ArrowLeft" ->
            e.preventDefault()
            if selIdx > 0 then setSelIdx (selIdx - 1)
        | "Enter" ->
            if selIdx >= 0 then
                e.preventDefault()
                match activeTab with
                | Library ->
                    match localResults |> List.tryItem selIdx with
                    | Some r -> dispatch (Navigate_to (r.Slug, r.MediaType))
                    | None -> ()
                | Movies | Series ->
                    let results = if activeTab = Movies then movieResults else seriesResults
                    match results |> List.tryItem selIdx with
                    | Some r -> dispatch (Import (r.TmdbId, r.MediaType))
                    | None -> ()
                | Games ->
                    match gameResults |> List.tryItem selIdx with
                    | Some r -> dispatch (Import_rawg r)
                    | None -> ()
        | "Escape" -> dispatch Close
        | _ -> ()

    let tabLabel (t: SearchTab) =
        match t with Library -> "Library" | Movies -> "Movies" | Series -> "Series" | Games -> "Games"

    let tabIcon (t: SearchTab) =
        match t with
        | Library -> Icons.catalog ()
        | Movies -> Icons.movie ()
        | Series -> Icons.tv ()
        | Games -> Icons.gamepad ()

    let isTabLoading (t: SearchTab) =
        match t with
        | Library -> false
        | Movies | Series -> model.IsSearchingTmdb
        | Games -> model.IsSearchingRawg

    let renderGrid (children: ReactElement list) =
        Html.div [
            prop.className "grid grid-cols-4 gap-3"
            prop.children children
        ]

    let renderTabContent () =
        if isTabLoading activeTab then
            Html.div [
                prop.className "flex items-center gap-2 py-3 text-base-content/40"
                prop.children [
                    Daisy.loading [ loading.dots; loading.sm ]
                    Html.span [
                        prop.text (match activeTab with Games -> "Searching RAWG..." | _ -> "Searching TMDB...")
                    ]
                ]
            ]
        else
            match activeTab with
            | Library ->
                if List.isEmpty localResults then
                    Html.p [
                        prop.className "text-sm text-base-content/40 py-4 text-center"
                        prop.text (if model.Query = "" then "Start typing to search" else "No matches in your library")
                    ]
                else
                    renderGrid [
                        for (idx, result) in localResults |> List.mapi (fun i r -> (i, r)) do
                            let isSelected = selIdx = idx
                            let imgSrc =
                                match result.PosterRef with
                                | Some ref -> Some $"/images/{ref}"
                                | None -> None
                            let fallbackIcon () =
                                match result.MediaType with
                                | MediaType.Movie -> Icons.movie ()
                                | MediaType.Series -> Icons.tv ()
                                | MediaType.Game -> Icons.gamepad ()
                            let mediaBadge =
                                Some (
                                    Daisy.badge [
                                        badge.xs
                                        prop.className (
                                            match result.MediaType with
                                            | MediaType.Movie -> "bg-blue-500/80 text-white border-0"
                                            | MediaType.Series -> "bg-purple-500/80 text-white border-0"
                                            | MediaType.Game -> "bg-green-500/80 text-white border-0"
                                        )
                                        prop.text (
                                            match result.MediaType with
                                            | MediaType.Movie -> "Movie"
                                            | MediaType.Series -> "Series"
                                            | MediaType.Game -> "Game"
                                        )
                                    ]
                                )
                            let hoverKey =
                                match result.MediaType with
                                | MediaType.Movie -> $"lib:movie:{result.Slug}"
                                | MediaType.Series -> $"lib:series:{result.Slug}"
                                | MediaType.Game -> $"lib:game:{result.Slug}"
                            renderPosterCard
                                imgSrc
                                fallbackIcon
                                result.Name
                                (string result.Year)
                                mediaBadge
                                isSelected
                                (fun () -> dispatch (Navigate_to (result.Slug, result.MediaType)))
                                (fun () -> startHover hoverKey)
                                (fun () -> cancelHover ())
                    ]
            | Movies | Series ->
                let results = if activeTab = Movies then movieResults else seriesResults
                if List.isEmpty results then
                    Html.p [
                        prop.className "text-sm text-base-content/40 py-4 text-center"
                        prop.text "No results"
                    ]
                else
                    renderGrid [
                        for (idx, result) in results |> List.mapi (fun i r -> (i, r)) do
                            let isSelected = selIdx = idx
                            let imgSrc =
                                match result.PosterPath with
                                | Some path -> Some $"https://image.tmdb.org/t/p/w185{path}"
                                | None -> None
                            let fallbackIcon () =
                                match result.MediaType with
                                | MediaType.Movie -> Icons.movie ()
                                | MediaType.Series -> Icons.tv ()
                                | _ -> Icons.movie ()
                            let hoverKey =
                                match result.MediaType with
                                | MediaType.Movie -> $"tmdb:movie:{result.TmdbId}"
                                | _ -> $"tmdb:series:{result.TmdbId}"
                            renderPosterCard
                                imgSrc
                                fallbackIcon
                                result.Title
                                (result.Year |> Option.map string |> Option.defaultValue "")
                                None
                                isSelected
                                (fun () -> dispatch (Import (result.TmdbId, result.MediaType)))
                                (fun () -> startHover hoverKey)
                                (fun () -> cancelHover ())
                    ]
            | Games ->
                if List.isEmpty gameResults then
                    Html.p [
                        prop.className "text-sm text-base-content/40 py-4 text-center"
                        prop.text "No results"
                    ]
                else
                    renderGrid [
                        for (idx, result) in gameResults |> List.mapi (fun i r -> (i, r)) do
                            let isSelected = selIdx = idx
                            let imgSrc = result.BackgroundImage
                            let hoverKey = $"rawg:{result.RawgId}"
                            renderPosterCard
                                imgSrc
                                (fun () -> Icons.gamepad ())
                                result.Name
                                (result.Year |> Option.map string |> Option.defaultValue "")
                                None
                                isSelected
                                (fun () -> dispatch (Import_rawg result))
                                (fun () -> startHover hoverKey)
                                (fun () -> cancelHover ())
                    ]

    // Main modal
    Html.div [
        prop.className Mediatheca.Client.DesignSystem.modalContainer
        prop.children [
            Html.div [
                prop.className Mediatheca.Client.DesignSystem.modalBackdrop
                prop.onClick (fun _ -> dispatch Close)
            ]
            // Wrapper for modal + popover (siblings, no backdrop-filter on wrapper)
            Html.div [
                prop.className "relative w-full max-w-4xl mx-4 flex"
                prop.children [
                    // Modal panel
                    Html.div [
                        prop.className ("flex-1 max-h-[70vh] flex flex-col " + Mediatheca.Client.DesignSystem.modalPanel)
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
                                                        let len = tabResultCount tab
                                                        if selIdx >= 0 then
                                                            setSelIdx (if len > 0 then min selIdx (len - 1) else -1)
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
                                    else
                                        renderTabContent ()
                                ]
                            ]
                            // Keyboard hints footer
                            Html.div [
                                prop.className "flex items-center gap-4 px-5 py-3 border-t border-base-content/10 text-xs text-base-content/40"
                                prop.children [
                                    Html.span [ prop.text "\u2190\u2191\u2192\u2193 navigate" ]
                                    Html.span [ prop.text "tab switch" ]
                                    Html.span [ prop.text "\u21B5 select" ]
                                    Html.span [ prop.text "esc close" ]
                                ]
                            ]
                        ]
                    ]
                    // Preview popover (sibling to modal panel, not child)
                    if model.HoverPreview <> NotHovering then
                        Html.div [
                            prop.className "hidden lg:block ml-3 w-80 flex-shrink-0"
                            prop.onMouseEnter (fun _ -> ()) // keep popover visible
                            prop.onMouseLeave (fun _ -> cancelHover ())
                            prop.children [
                                renderPreviewPopover model.HoverPreview
                            ]
                        ]
                ]
            ]
        ]
    ]
