module Mediatheca.Client.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Router
open Mediatheca.Client.Types
open Mediatheca.Client.Components

let private debounceCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        Fable.Core.JS.setTimeout (fun () -> dispatch msg) ms |> ignore
    )

/// design-system-fryq7: the search modal's local-library snapshot used to
/// come for free from the three list pages' own already-loaded models. With
/// those pages gone, this fetches a fresh snapshot on every modal open —
/// batched into the modal's open Cmd rather than kept warm client-side.
let private loadSearchLibraryCmd (api: IMediathecaApi) : Cmd<Msg> =
    let loadAll = async {
        let! movies = api.getMovies ()
        let! series = api.getSeries ()
        let! games = api.getGames ()
        let! books = api.getBooks ()
        return movies, series, games, books
    }
    Cmd.OfAsync.perform
        (fun () -> loadAll) ()
        (fun (movies, series, games, books) -> Search_modal_msg (SearchModal.Library_loaded (movies, series, games, books)))

let init (api: IMediathecaApi) (adminApi: IAdminApi) () : Model * Cmd<Msg> =
    let dashboardModel, dashboardCmd = Pages.Dashboard.State.init ()
    let movieDetailModel, movieDetailCmd = Pages.MovieDetail.State.init ""
    let seriesDetailModel, seriesDetailCmd = Pages.SeriesDetail.State.init ""
    let gameDetailModel, gameDetailCmd = Pages.GameDetail.State.init ""
    let bookDetailModel, bookDetailCmd = Pages.BookDetail.State.init ""
    let friendListModel, friendListCmd = Pages.Friends.State.init ()
    let friendDetailModel, friendDetailCmd = Pages.FriendDetail.State.init ""
    let catalogListModel, catalogListCmd = Pages.Catalogs.State.init ()
    let catalogDetailModel, catalogDetailCmd = Pages.CatalogDetail.State.init ""
    let streamDetailModel, streamDetailCmd = Pages.StreamDetail.State.init ""
    let settingsModel, settingsCmd = Pages.Settings.State.init ()
    let styleGuideModel, styleGuideCmd = Pages.StyleGuide.State.init ()

    let model = {
        CurrentPage = Dashboard
        NavigationHistory = []
        SuppressNextHistoryPush = false
        PendingDashboardTab = None
        DashboardModel = dashboardModel
        MovieDetailModel = movieDetailModel
        SeriesDetailModel = seriesDetailModel
        GameDetailModel = gameDetailModel
        BookDetailModel = bookDetailModel
        FriendListModel = friendListModel
        FriendDetailModel = friendDetailModel
        CatalogListModel = catalogListModel
        CatalogDetailModel = catalogDetailModel
        StreamDetailModel = streamDetailModel
        SettingsModel = settingsModel
        StyleGuideModel = styleGuideModel
        SearchModal = None
        JellyfinSyncing = false
        JellyfinSyncResult = None
        ShowJellyfinSyncToast = false
    }

    let cmd = Cmd.batch [
        Cmd.map Dashboard_msg dashboardCmd
        Cmd.OfAsync.either
            api.getDashboardAllTab ()
            (fun data -> Dashboard_msg (Pages.Dashboard.Types.AllTabLoaded data))
            (fun ex -> Dashboard_msg (Pages.Dashboard.Types.TabLoadError ex.Message))
        Cmd.map Settings_msg settingsCmd
        // Trigger Jellyfin auto-sync on app visit
        Cmd.OfAsync.perform api.triggerJellyfinSync () JellyfinSyncTriggered
    ]

    model, cmd

/// games-k3vps: fires whichever of RAWG/Steam are currently checked, in
/// parallel — the shared search-firing shape used by `Tab_changed`,
/// `Debounce_tmdb_expired`, and the two `Toggle_include_*` handlers below.
let private gamesSearchCmds (api: IMediathecaApi) (includeRawg: bool) (includeSteam: bool) (cleanQuery: string) (yearOpt: int option) : Cmd<Msg> =
    Cmd.batch [
        if includeRawg then
            yield Cmd.OfAsync.either
                api.searchRawgGames (cleanQuery, yearOpt)
                (fun results -> Search_modal_msg (SearchModal.Rawg_search_completed results))
                (fun ex -> Search_modal_msg (SearchModal.Rawg_search_failed ex.Message))
        if includeSteam then
            yield Cmd.OfAsync.either
                api.searchSteamGames (cleanQuery, yearOpt)
                (fun results -> Search_modal_msg (SearchModal.Steam_search_completed results))
                (fun ex -> Search_modal_msg (SearchModal.Steam_search_failed ex.Message))
    ]

/// books-g7g1j: fires whichever of Open Library/Audible are currently
/// checked, in parallel, tagging each completion with `version` (the
/// `SearchVersion` this search was fired under) so a stale response is
/// dropped by `SearchModal.applyBooksSearchResults` — see that function's
/// doc comment for why the Books tab needs this and RAWG/Steam don't. Built
/// on the pure `SearchModal.booksSearchPlan` so the "which sources fire" and
/// "how each source's Cmd is built" concerns stay independently testable.
let private booksSearchCmds (api: IMediathecaApi) (includeOpenLibrary: bool) (includeAudible: bool) (version: int) (cleanQuery: string) : Cmd<Msg> =
    SearchModal.booksSearchPlan includeOpenLibrary includeAudible
    |> List.map (function
        | SearchModal.OpenLibrary ->
            Cmd.OfAsync.either
                api.searchOpenLibraryBooks cleanQuery
                (fun results -> Search_modal_msg (SearchModal.OpenLibrary_search_completed (version, results)))
                (fun ex -> Search_modal_msg (SearchModal.OpenLibrary_search_failed ex.Message))
        | SearchModal.Audible ->
            Cmd.OfAsync.either
                api.searchAudibleBooks cleanQuery
                (fun results -> Search_modal_msg (SearchModal.Audible_search_completed (version, results)))
                (fun ex -> Search_modal_msg (SearchModal.Audible_search_failed ex.Message)))
    |> Cmd.batch

let private updateSearchModal (api: IMediathecaApi) (childMsg: SearchModal.Msg) (model: Model) : Model * Cmd<Msg> =
    match model.SearchModal with
    | None -> model, Cmd.none
    | Some searchModel ->
        match childMsg with
        | SearchModal.Library_loaded (movies, series, games, books) ->
            { model with SearchModal = Some (searchModel |> SearchModal.applyLibraryLoaded movies series games books) },
            Cmd.none

        | SearchModal.Close ->
            { model with SearchModal = None }, Cmd.none

        | SearchModal.Tab_changed tab ->
            let updatedSearch = { searchModel with ActiveTab = tab }
            // Library tab needs no API call; external tabs may need a search.
            // For Games, each checked source is considered independently — a
            // source with no cached results and not already in flight needs
            // its own search, regardless of the other source's state.
            let rawgNeedsSearch = searchModel.IncludeRawg && List.isEmpty searchModel.RawgResults && not searchModel.IsSearchingRawg
            let steamNeedsSearch = searchModel.IncludeSteam && List.isEmpty searchModel.SteamResults && not searchModel.IsSearchingSteam
            let openLibraryNeedsSearch = searchModel.IncludeOpenLibrary && List.isEmpty searchModel.OpenLibraryResults && not searchModel.IsSearchingOpenLibrary
            let audibleNeedsSearch = searchModel.IncludeAudible && List.isEmpty searchModel.AudibleResults && not searchModel.IsSearchingAudible
            let needsSearch =
                searchModel.Query <> "" &&
                (match tab with
                 | SearchModal.Library -> false
                 | SearchModal.Movies | SearchModal.Series ->
                     List.isEmpty searchModel.TmdbResults && not searchModel.IsSearchingTmdb
                 | SearchModal.Games -> rawgNeedsSearch || steamNeedsSearch
                 | SearchModal.Books -> openLibraryNeedsSearch || audibleNeedsSearch)
            if needsSearch then
                let cleanQuery, yearOpt = FuzzyMatch.extractYear searchModel.Query
                match tab with
                | SearchModal.Movies | SearchModal.Series ->
                    let withLoading = { updatedSearch with IsSearchingTmdb = true }
                    let searchBoth = async {
                        let! movieResults = api.searchTmdb (cleanQuery, yearOpt)
                        let! seriesResults = api.searchTvSeries (cleanQuery, yearOpt)
                        return movieResults @ seriesResults
                    }
                    { model with SearchModal = Some withLoading },
                    Cmd.OfAsync.either
                        (fun () -> searchBoth) ()
                        (fun results -> Search_modal_msg (SearchModal.Tmdb_search_completed results))
                        (fun ex -> Search_modal_msg (SearchModal.Tmdb_search_failed ex.Message))
                | SearchModal.Games ->
                    let withLoading =
                        { updatedSearch with
                            IsSearchingRawg = rawgNeedsSearch
                            IsSearchingSteam = steamNeedsSearch }
                    { model with SearchModal = Some withLoading },
                    gamesSearchCmds api rawgNeedsSearch steamNeedsSearch cleanQuery yearOpt
                | SearchModal.Books ->
                    let withLoading =
                        { updatedSearch with
                            IsSearchingOpenLibrary = openLibraryNeedsSearch
                            IsSearchingAudible = audibleNeedsSearch }
                    { model with SearchModal = Some withLoading },
                    booksSearchCmds api openLibraryNeedsSearch audibleNeedsSearch searchModel.SearchVersion cleanQuery
                | SearchModal.Library ->
                    { model with SearchModal = Some updatedSearch }, Cmd.none
            else
                { model with SearchModal = Some updatedSearch }, Cmd.none

        | SearchModal.Query_changed q ->
            let newVersion = searchModel.SearchVersion + 1
            let activeTab = searchModel.ActiveTab
            let updatedSearch = {
                searchModel with
                    Query = q
                    SearchVersion = newVersion
                    IsSearchingTmdb = q <> "" && (activeTab = SearchModal.Movies || activeTab = SearchModal.Series)
                    IsSearchingRawg = q <> "" && activeTab = SearchModal.Games && searchModel.IncludeRawg
                    IsSearchingSteam = q <> "" && activeTab = SearchModal.Games && searchModel.IncludeSteam
                    IsSearchingOpenLibrary = q <> "" && activeTab = SearchModal.Books && searchModel.IncludeOpenLibrary
                    IsSearchingAudible = q <> "" && activeTab = SearchModal.Books && searchModel.IncludeAudible
                    // Keep active tab results for progressive UX; clear inactive tab results (stale query)
                    TmdbResults =
                        if q = "" then []
                        elif activeTab = SearchModal.Movies || activeTab = SearchModal.Series then searchModel.TmdbResults
                        else []
                    RawgResults =
                        if q = "" then []
                        elif activeTab = SearchModal.Games then searchModel.RawgResults
                        else []
                    SteamResults =
                        if q = "" then []
                        elif activeTab = SearchModal.Games then searchModel.SteamResults
                        else []
                    OpenLibraryResults =
                        if q = "" then []
                        elif activeTab = SearchModal.Books then searchModel.OpenLibraryResults
                        else []
                    AudibleResults =
                        if q = "" then []
                        elif activeTab = SearchModal.Books then searchModel.AudibleResults
                        else []
                    Error = None
            }
            let cmds =
                match activeTab with
                | SearchModal.Library -> Cmd.none
                | _ ->
                    if q = "" then Cmd.none
                    else debounceCmd 300 (Search_modal_msg (SearchModal.Debounce_tmdb_expired newVersion))
            { model with SearchModal = Some updatedSearch }, cmds

        | SearchModal.Debounce_tmdb_expired version ->
            if version <> searchModel.SearchVersion || searchModel.Query = "" then
                model, Cmd.none
            else
                let cleanQuery, yearOpt = FuzzyMatch.extractYear searchModel.Query
                match searchModel.ActiveTab with
                | SearchModal.Movies | SearchModal.Series ->
                    let searchBoth = async {
                        let! movieResults = api.searchTmdb (cleanQuery, yearOpt)
                        let! seriesResults = api.searchTvSeries (cleanQuery, yearOpt)
                        return movieResults @ seriesResults
                    }
                    model,
                    Cmd.OfAsync.either
                        (fun () -> searchBoth) ()
                        (fun results -> Search_modal_msg (SearchModal.Tmdb_search_completed results))
                        (fun ex -> Search_modal_msg (SearchModal.Tmdb_search_failed ex.Message))
                | SearchModal.Games ->
                    model, gamesSearchCmds api searchModel.IncludeRawg searchModel.IncludeSteam cleanQuery yearOpt
                | SearchModal.Books ->
                    model, booksSearchCmds api searchModel.IncludeOpenLibrary searchModel.IncludeAudible version cleanQuery
                | SearchModal.Library ->
                    model, Cmd.none

        | SearchModal.Tmdb_search_completed results ->
            { model with SearchModal = Some { searchModel with TmdbResults = results; IsSearchingTmdb = false } }, Cmd.none

        | SearchModal.Tmdb_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingTmdb = false; Error = Some err } }, Cmd.none

        | SearchModal.Rawg_search_completed results ->
            { model with SearchModal = Some { searchModel with RawgResults = results; IsSearchingRawg = false } }, Cmd.none

        | SearchModal.Rawg_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingRawg = false; Error = Some err } }, Cmd.none

        | SearchModal.Steam_search_completed results ->
            { model with SearchModal = Some { searchModel with SteamResults = results; IsSearchingSteam = false } }, Cmd.none

        | SearchModal.Steam_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingSteam = false; Error = Some err } }, Cmd.none

        | SearchModal.OpenLibrary_search_completed (version, results) ->
            { model with SearchModal = Some (searchModel |> SearchModal.applyBooksSearchResults version (SearchModal.OpenLibraryResponse results)) }, Cmd.none

        | SearchModal.OpenLibrary_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingOpenLibrary = false; Error = Some err } }, Cmd.none

        | SearchModal.Audible_search_completed (version, results) ->
            { model with SearchModal = Some (searchModel |> SearchModal.applyBooksSearchResults version (SearchModal.AudibleResponse results)) }, Cmd.none

        | SearchModal.Audible_search_failed err ->
            { model with SearchModal = Some { searchModel with IsSearchingAudible = false; Error = Some err } }, Cmd.none

        | SearchModal.Toggle_include_rawg ->
            let newInclude = not searchModel.IncludeRawg
            let updated = { searchModel with IncludeRawg = newInclude }
            if newInclude && searchModel.Query <> "" then
                let cleanQuery, yearOpt = FuzzyMatch.extractYear searchModel.Query
                { model with SearchModal = Some { updated with IsSearchingRawg = true } },
                Cmd.OfAsync.either
                    api.searchRawgGames (cleanQuery, yearOpt)
                    (fun results -> Search_modal_msg (SearchModal.Rawg_search_completed results))
                    (fun ex -> Search_modal_msg (SearchModal.Rawg_search_failed ex.Message))
            else
                { model with SearchModal = Some updated }, Cmd.none

        | SearchModal.Toggle_include_steam ->
            let newInclude = not searchModel.IncludeSteam
            let updated = { searchModel with IncludeSteam = newInclude }
            if newInclude && searchModel.Query <> "" then
                let cleanQuery, yearOpt = FuzzyMatch.extractYear searchModel.Query
                { model with SearchModal = Some { updated with IsSearchingSteam = true } },
                Cmd.OfAsync.either
                    api.searchSteamGames (cleanQuery, yearOpt)
                    (fun results -> Search_modal_msg (SearchModal.Steam_search_completed results))
                    (fun ex -> Search_modal_msg (SearchModal.Steam_search_failed ex.Message))
            else
                { model with SearchModal = Some updated }, Cmd.none

        | SearchModal.Toggle_include_openlibrary ->
            let newInclude = not searchModel.IncludeOpenLibrary
            let updated = { searchModel with IncludeOpenLibrary = newInclude }
            if newInclude && searchModel.Query <> "" then
                let cleanQuery, _yearOpt = FuzzyMatch.extractYear searchModel.Query
                { model with SearchModal = Some { updated with IsSearchingOpenLibrary = true } },
                Cmd.OfAsync.either
                    api.searchOpenLibraryBooks cleanQuery
                    (fun results -> Search_modal_msg (SearchModal.OpenLibrary_search_completed (searchModel.SearchVersion, results)))
                    (fun ex -> Search_modal_msg (SearchModal.OpenLibrary_search_failed ex.Message))
            else
                { model with SearchModal = Some updated }, Cmd.none

        | SearchModal.Toggle_include_audible ->
            let newInclude = not searchModel.IncludeAudible
            let updated = { searchModel with IncludeAudible = newInclude }
            if newInclude && searchModel.Query <> "" then
                let cleanQuery, _yearOpt = FuzzyMatch.extractYear searchModel.Query
                { model with SearchModal = Some { updated with IsSearchingAudible = true } },
                Cmd.OfAsync.either
                    api.searchAudibleBooks cleanQuery
                    (fun results -> Search_modal_msg (SearchModal.Audible_search_completed (searchModel.SearchVersion, results)))
                    (fun ex -> Search_modal_msg (SearchModal.Audible_search_failed ex.Message))
            else
                { model with SearchModal = Some updated }, Cmd.none

        | SearchModal.Import (tmdbId, mediaType) ->
            let importCmd =
                match mediaType with
                | MediaType.Movie ->
                    Cmd.OfAsync.either
                        api.addMovie tmdbId
                        (fun result -> Search_modal_msg (SearchModal.Import_completed (result |> Result.map (fun slug -> slug, MediaType.Movie))))
                        (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                | MediaType.Series ->
                    Cmd.OfAsync.either
                        api.addSeries tmdbId
                        (fun result -> Search_modal_msg (SearchModal.Import_completed (result |> Result.map (fun slug -> slug, MediaType.Series))))
                        (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                | MediaType.Game ->
                    Cmd.none // Games use Import_rawg instead
                | MediaType.Book ->
                    Cmd.none // Books have no TMDB import path (books-g7g1j owns its own search tab)
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None } }, importCmd

        | SearchModal.Import_rawg rawgResult ->
            let request: AddGameRequest = {
                Name = rawgResult.Name
                Year = rawgResult.Year |> Option.defaultValue 0
                Genres = rawgResult.Genres
                Description = ""
                CoverRef = rawgResult.BackgroundImage
                BackdropRef = rawgResult.BackgroundImage
                RawgId = Some rawgResult.RawgId
                RawgRating = rawgResult.Rating
                SkipDuplicateCheck = false
            }
            let importCmd =
                Cmd.OfAsync.either
                    api.addGame request
                    (fun result ->
                        match result with
                        | Ok (Created slug) ->
                            Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Game)))
                        | Ok (Duplicate_found (existingSlug, existingName)) ->
                            Search_modal_msg (SearchModal.Duplicate_prompt_show (existingSlug, existingName, SearchModal.FromRawg request))
                        | Error e ->
                            Search_modal_msg (SearchModal.Import_completed (Error e)))
                    (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None; DuplicatePrompt = None } }, importCmd

        | SearchModal.Import_steam steamResult ->
            let request: AddGameFromSteamRequest = {
                AppId = steamResult.AppId
                Name = steamResult.Name
                Year = steamResult.ReleaseYear
                SkipDuplicateCheck = false
            }
            let importCmd =
                Cmd.OfAsync.either
                    api.addGameFromSteam request
                    (fun result ->
                        match result with
                        | Ok (Created slug) ->
                            Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Game)))
                        | Ok (Duplicate_found (existingSlug, existingName)) ->
                            Search_modal_msg (SearchModal.Duplicate_prompt_show (existingSlug, existingName, SearchModal.FromSteam request))
                        | Error e ->
                            Search_modal_msg (SearchModal.Import_completed (Error e)))
                    (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None; DuplicatePrompt = None } }, importCmd

        | SearchModal.Import_openlibrary olResult ->
            let request: AddBookFromOpenLibraryRequest = {
                WorkKey = olResult.WorkKey
                EditionKey = olResult.EditionKey
                Isbn13 = olResult.Isbn13
                SkipDuplicateCheck = false
            }
            let importCmd =
                Cmd.OfAsync.either
                    api.addBookFromOpenLibrary request
                    (fun result ->
                        match result with
                        | Ok (Book_added slug) ->
                            Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Book)))
                        | Ok (AddBookOutcome.Duplicate_found (existingSlug, existingTitle)) ->
                            Search_modal_msg (SearchModal.Duplicate_prompt_show (existingSlug, existingTitle, SearchModal.FromOpenLibrary request))
                        | Error e ->
                            Search_modal_msg (SearchModal.Import_completed (Error e)))
                    (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None; DuplicatePrompt = None } }, importCmd

        | SearchModal.Import_audible audibleResult ->
            let request: AddBookFromAudibleRequest = { Asin = audibleResult.Asin; SkipDuplicateCheck = false }
            let importCmd =
                Cmd.OfAsync.either
                    api.addBookFromAudible request
                    (fun result ->
                        match result with
                        | Ok (Book_added slug) ->
                            Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Book)))
                        | Ok (AddBookOutcome.Duplicate_found (existingSlug, existingTitle)) ->
                            Search_modal_msg (SearchModal.Duplicate_prompt_show (existingSlug, existingTitle, SearchModal.FromAudible request))
                        | Error e ->
                            Search_modal_msg (SearchModal.Import_completed (Error e)))
                    (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
            { model with SearchModal = Some { searchModel with IsImporting = true; Error = None; DuplicatePrompt = None } }, importCmd

        | SearchModal.Import_completed result ->
            match result with
            | Ok (slug, mediaType) ->
                // design-system-fryq7: the three list models this used to
                // reload (`Load_movies`/`Load_series`/`Load_games`) are gone
                // — the search modal fetches its own fresh snapshot on every
                // open (`loadSearchLibraryCmd`), so nothing client-side needs
                // refreshing here.
                let navSegments =
                    match mediaType with
                    | MediaType.Movie -> ("movies", slug)
                    | MediaType.Series -> ("series", slug)
                    | MediaType.Game -> ("games", slug)
                    | MediaType.Book -> ("books", slug)
                { model with SearchModal = None },
                Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate (fst navSegments, snd navSegments))
            | Error err ->
                { model with SearchModal = Some { searchModel with Error = Some err; IsImporting = false } }, Cmd.none

        | SearchModal.Duplicate_prompt_show (existingSlug, existingName, request) ->
            { model with SearchModal = Some { searchModel with IsImporting = false; DuplicatePrompt = Some (existingSlug, existingName, request) } }, Cmd.none

        | SearchModal.Duplicate_prompt_cancel ->
            { model with SearchModal = Some { searchModel with DuplicatePrompt = None } }, Cmd.none

        | SearchModal.Duplicate_prompt_force_add ->
            match searchModel.DuplicatePrompt with
            | None -> model, Cmd.none
            | Some (_existingSlug, _existingName, pending) ->
                // books-g7g1j: "add anyway" always resubmits the exact
                // request that triggered `Duplicate_found`, with
                // `SkipDuplicateCheck` forced true — `forceDuplicateImport`
                // is the pure, per-case-tested version of what used to be
                // an inlined `{ originalRequest with SkipDuplicateCheck =
                // true }` per case.
                let importCmd =
                    match SearchModal.forceDuplicateImport pending with
                    | SearchModal.FromRawg forceRequest ->
                        Cmd.OfAsync.either
                            api.addGame forceRequest
                            (fun result ->
                                match result with
                                | Ok (Created slug) ->
                                    Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Game)))
                                | Ok (Duplicate_found _) ->
                                    // Shouldn't happen with SkipDuplicateCheck=true, but treat as a regular error
                                    Search_modal_msg (SearchModal.Import_completed (Error "Unexpected duplicate response"))
                                | Error e ->
                                    Search_modal_msg (SearchModal.Import_completed (Error e)))
                            (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                    | SearchModal.FromSteam forceRequest ->
                        Cmd.OfAsync.either
                            api.addGameFromSteam forceRequest
                            (fun result ->
                                match result with
                                | Ok (Created slug) ->
                                    Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Game)))
                                | Ok (Duplicate_found _) ->
                                    Search_modal_msg (SearchModal.Import_completed (Error "Unexpected duplicate response"))
                                | Error e ->
                                    Search_modal_msg (SearchModal.Import_completed (Error e)))
                            (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                    | SearchModal.FromOpenLibrary forceRequest ->
                        Cmd.OfAsync.either
                            api.addBookFromOpenLibrary forceRequest
                            (fun result ->
                                match result with
                                | Ok (Book_added slug) ->
                                    Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Book)))
                                | Ok (AddBookOutcome.Duplicate_found _) ->
                                    Search_modal_msg (SearchModal.Import_completed (Error "Unexpected duplicate response"))
                                | Error e ->
                                    Search_modal_msg (SearchModal.Import_completed (Error e)))
                            (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                    | SearchModal.FromAudible forceRequest ->
                        Cmd.OfAsync.either
                            api.addBookFromAudible forceRequest
                            (fun result ->
                                match result with
                                | Ok (Book_added slug) ->
                                    Search_modal_msg (SearchModal.Import_completed (Ok (slug, MediaType.Book)))
                                | Ok (AddBookOutcome.Duplicate_found _) ->
                                    Search_modal_msg (SearchModal.Import_completed (Error "Unexpected duplicate response"))
                                | Error e ->
                                    Search_modal_msg (SearchModal.Import_completed (Error e)))
                            (fun ex -> Search_modal_msg (SearchModal.Import_completed (Error ex.Message)))
                { model with SearchModal = Some { searchModel with IsImporting = true; Error = None; DuplicatePrompt = None } }, importCmd

        | SearchModal.Navigate_to (slug, mediaType) ->
            let navSegments =
                match mediaType with
                | MediaType.Movie -> ("movies", slug)
                | MediaType.Series -> ("series", slug)
                | MediaType.Game -> ("games", slug)
                | MediaType.Book -> ("books", slug)
            { model with SearchModal = None },
            Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate (fst navSegments, snd navSegments))

        | SearchModal.Hover_start (key, _version) ->
            // Check if cached
            match searchModel.PreviewCache |> Map.tryFind key with
            | Some cached ->
                { model with SearchModal = Some { searchModel with HoverTarget = Some key; HoverPreview = cached } }, Cmd.none
            | None ->
                let cmd =
                    if key.StartsWith("tmdb:movie:") then
                        let tmdbId = key.Replace("tmdb:movie:", "") |> int
                        Cmd.OfAsync.either
                            api.previewTmdbMovie tmdbId
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_tmdb_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    elif key.StartsWith("tmdb:series:") then
                        let tmdbId = key.Replace("tmdb:series:", "") |> int
                        Cmd.OfAsync.either
                            api.previewTmdbSeries tmdbId
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_tmdb_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    elif key.StartsWith("rawg:") then
                        let rawgId = key.Replace("rawg:", "") |> int
                        Cmd.OfAsync.either
                            api.previewRawgGame rawgId
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_rawg_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    elif key.StartsWith("lib:movie:") then
                        let slug = key.Replace("lib:movie:", "")
                        Cmd.OfAsync.either
                            api.getMovie slug
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_library_movie_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    elif key.StartsWith("lib:series:") then
                        let slug = key.Replace("lib:series:", "")
                        Cmd.OfAsync.either
                            (fun s -> api.getSeriesDetail s None) slug
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_library_series_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    elif key.StartsWith("lib:game:") then
                        let slug = key.Replace("lib:game:", "")
                        Cmd.OfAsync.either
                            api.getGameDetail slug
                            (fun data -> Search_modal_msg (SearchModal.Hover_preview_library_game_loaded (key, data)))
                            (fun _ -> Search_modal_msg SearchModal.Hover_clear)
                    else Cmd.none
                { model with SearchModal = Some { searchModel with HoverTarget = Some key; HoverPreview = SearchModal.Loading } }, cmd

        | SearchModal.Hover_preview_tmdb_loaded (key, data) ->
            match data with
            | Some d ->
                let preview = SearchModal.LoadedTmdb d
                let cache = searchModel.PreviewCache |> Map.add key preview
                { model with SearchModal = Some { searchModel with HoverPreview = preview; PreviewCache = cache } }, Cmd.none
            | None ->
                { model with SearchModal = Some { searchModel with HoverPreview = SearchModal.Failed } }, Cmd.none

        | SearchModal.Hover_preview_rawg_loaded (key, data) ->
            match data with
            | Some d ->
                let preview = SearchModal.LoadedRawg d
                let cache = searchModel.PreviewCache |> Map.add key preview
                { model with SearchModal = Some { searchModel with HoverPreview = preview; PreviewCache = cache } }, Cmd.none
            | None ->
                { model with SearchModal = Some { searchModel with HoverPreview = SearchModal.Failed } }, Cmd.none

        | SearchModal.Hover_preview_library_movie_loaded (key, data) ->
            match data with
            | Some d ->
                let preview = SearchModal.LoadedLibraryMovie d
                let cache = searchModel.PreviewCache |> Map.add key preview
                { model with SearchModal = Some { searchModel with HoverPreview = preview; PreviewCache = cache } }, Cmd.none
            | None ->
                { model with SearchModal = Some { searchModel with HoverPreview = SearchModal.Failed } }, Cmd.none

        | SearchModal.Hover_preview_library_series_loaded (key, data) ->
            match data with
            | Some d ->
                let preview = SearchModal.LoadedLibrarySeries d
                let cache = searchModel.PreviewCache |> Map.add key preview
                { model with SearchModal = Some { searchModel with HoverPreview = preview; PreviewCache = cache } }, Cmd.none
            | None ->
                { model with SearchModal = Some { searchModel with HoverPreview = SearchModal.Failed } }, Cmd.none

        | SearchModal.Hover_preview_library_game_loaded (key, data) ->
            match data with
            | Some d ->
                let preview = SearchModal.LoadedLibraryGame d
                let cache = searchModel.PreviewCache |> Map.add key preview
                { model with SearchModal = Some { searchModel with HoverPreview = preview; PreviewCache = cache } }, Cmd.none
            | None ->
                { model with SearchModal = Some { searchModel with HoverPreview = SearchModal.Failed } }, Cmd.none

        | SearchModal.Hover_clear ->
            { model with SearchModal = Some { searchModel with HoverTarget = None; HoverPreview = SearchModal.NotHovering } }, Cmd.none

let private maxHistory = 20

let private pushHistory (prev: Page) (history: Page list) : Page list =
    // Skip pushing duplicates (e.g., re-renders or query-only changes)
    match history with
    | head :: _ when head = prev -> history |> List.truncate maxHistory
    | _ -> (prev :: history) |> List.truncate maxHistory

let update (api: IMediathecaApi) (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Url_changed segments ->
        let page = Route.parseUrl segments
        let prevPage = model.CurrentPage
        // Skip pushing onto the stack when the new page equals the current page,
        // when the previous page was Not_found, or when Go_back triggered this nav.
        let newHistory =
            if model.SuppressNextHistoryPush then model.NavigationHistory
            elif page = prevPage then model.NavigationHistory
            else
                match prevPage with
                | Not_found -> model.NavigationHistory
                | _ -> pushHistory prevPage model.NavigationHistory
        let model = { model with CurrentPage = page; NavigationHistory = newHistory; SuppressNextHistoryPush = false }
        // Leaving the Settings page entirely stops any live Follow poll in
        // the Events section's Event Browser. Every other page branch below
        // replaces only its own child model and leaves SettingsModel
        // untouched, so without this a Follow session started before
        // navigating away would keep polling indefinitely
        // (administration-mtf1f iteration 2 — see ADR-0023). Re-keyed from
        // "leaving Admin _" to "leaving Settings" by administration-k3vmt,
        // which dissolved the /admin console into Settings; collapsing the
        // Events section without navigating stops it too, via the same
        // idempotent `stopFollowing` call (Settings.State's
        // Toggle_events_section).
        let model =
            match prevPage, page with
            | Settings, Settings -> model
            | Settings, _ ->
                { model with
                    SettingsModel =
                        { model.SettingsModel with
                            AdminModel = Pages.Admin.State.stopFollowing model.SettingsModel.AdminModel } }
            | _ -> model
        // design-system-fryq7: bare /movies, /series, /games (old bookmarks —
        // Route.parseUrl resolves them to Dashboard, not a list page anymore)
        // pre-select the matching Dashboard tab the same way the detail pages'
        // empty-history Go_back fallback does.
        let model =
            match segments with
            | [ "movies" ] -> { model with PendingDashboardTab = Some Pages.Dashboard.Types.MoviesTab }
            | [ "series" ] -> { model with PendingDashboardTab = Some Pages.Dashboard.Types.SeriesTab }
            | [ "games" ] -> { model with PendingDashboardTab = Some Pages.Dashboard.Types.GamesTab }
            | _ -> model
        match page with
        | Movie_detail slug ->
            let childModel, childCmd = Pages.MovieDetail.State.init slug
            { model with MovieDetailModel = childModel },
            Cmd.map Movie_detail_msg childCmd
        | Series_detail slug ->
            let childModel, childCmd = Pages.SeriesDetail.State.init slug
            { model with SeriesDetailModel = childModel },
            Cmd.map Series_detail_msg childCmd
        | Game_detail slug ->
            let childModel, childCmd = Pages.GameDetail.State.init slug
            { model with GameDetailModel = childModel },
            Cmd.map Game_detail_msg childCmd
        | Book_detail slug ->
            let childModel, childCmd = Pages.BookDetail.State.init slug
            { model with BookDetailModel = childModel },
            Cmd.map Book_detail_msg childCmd
        | Friend_list ->
            let childModel, childCmd = Pages.Friends.State.init ()
            { model with FriendListModel = childModel },
            Cmd.map Friend_list_msg childCmd
        | Friend_detail slug ->
            let childModel, childCmd = Pages.FriendDetail.State.init slug
            { model with FriendDetailModel = childModel },
            Cmd.map Friend_detail_msg childCmd
        | Catalog_list ->
            let childModel, childCmd = Pages.Catalogs.State.init ()
            { model with CatalogListModel = childModel },
            Cmd.map Catalog_list_msg childCmd
        | Catalog_detail slug ->
            let childModel, childCmd = Pages.CatalogDetail.State.init slug
            { model with CatalogDetailModel = childModel },
            Cmd.map Catalog_detail_msg childCmd
        | Stream_detail streamId ->
            let childModel, childCmd = Pages.StreamDetail.State.init streamId
            { model with StreamDetailModel = childModel },
            Cmd.map Stream_detail_msg childCmd
        | Settings ->
            let childModel, childCmd = Pages.Settings.State.init ()
            { model with SettingsModel = childModel },
            Cmd.batch [
                Cmd.map Settings_msg childCmd
                // Fires on every /settings VISIT (this branch), never from
                // Settings.State.init itself — root init batches that Cmd
                // unconditionally on every page load, and the six admin
                // sections must stay silent at cold start (administration-
                // k3vmt). getProjectionStats is the one section load that
                // isn't gated behind its section being expanded: the
                // ADR-0034 dirty banner is client-derived from it and must
                // react even if the operator never opens Projections.
                Cmd.map Settings_msg Pages.Settings.State.loadProjectionStatsCmd
            ]
        | Styleguide ->
            let childModel, childCmd = Pages.StyleGuide.State.init ()
            { model with StyleGuideModel = childModel },
            Cmd.map Styleguide_msg childCmd
        | Dashboard ->
            let childModel, childCmd = Pages.Dashboard.State.init ()
            // Determine the active tab on Dashboard re-entry:
            //   PendingDashboardTab takes priority (Go_back empty-stack fallback).
            //   Otherwise preserve the previously active tab so the back button
            //   from a detail page returns to the same tab the item was opened from.
            let activeTab =
                match model.PendingDashboardTab with
                | Some tab -> tab
                | None -> model.DashboardModel.ActiveTab
            let childModel = { childModel with ActiveTab = activeTab }
            let tabCmd =
                match activeTab with
                | Pages.Dashboard.Types.All -> Cmd.none
                | _ -> Cmd.ofMsg (Dashboard_msg (Pages.Dashboard.Types.SwitchTab activeTab))
            { model with DashboardModel = childModel; PendingDashboardTab = None },
            Cmd.batch [
                Cmd.map Dashboard_msg childCmd
                Cmd.OfAsync.either
                    api.getDashboardAllTab ()
                    (fun data -> Dashboard_msg (Pages.Dashboard.Types.AllTabLoaded data))
                    (fun ex -> Dashboard_msg (Pages.Dashboard.Types.TabLoadError ex.Message))
                // Re-trigger Jellyfin auto-sync on dashboard visit (server enforces 5-min cooldown)
                Cmd.OfAsync.perform api.triggerJellyfinSync () JellyfinSyncTriggered
                tabCmd
            ]
        | _ -> model, Cmd.none

    | Go_back ->
        match model.NavigationHistory with
        | head :: tail ->
            // Pop head and navigate there. Suppress the next push so we don't
            // re-add the page we're leaving onto the stack.
            { model with NavigationHistory = tail; SuppressNextHistoryPush = true },
            Cmd.ofEffect (fun _ -> Route.navigateTo head)
        | [] ->
            // Empty stack fallback based on the page we're leaving
            match model.CurrentPage with
            | Movie_detail _ ->
                { model with PendingDashboardTab = Some Pages.Dashboard.Types.MoviesTab; SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Dashboard)
            | Series_detail _ ->
                { model with PendingDashboardTab = Some Pages.Dashboard.Types.SeriesTab; SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Dashboard)
            | Game_detail _ ->
                { model with PendingDashboardTab = Some Pages.Dashboard.Types.GamesTab; SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Dashboard)
            | Book_detail _ ->
                // No Books dashboard tab exists yet (books-g7g1j) — land on
                // the Dashboard without pre-selecting a tab, same as any
                // other page with no dedicated tab of its own.
                { model with SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Dashboard)
            | Friend_detail _ ->
                { model with SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Friend_list)
            | _ ->
                { model with SuppressNextHistoryPush = true },
                Cmd.ofEffect (fun _ -> Route.navigateTo Dashboard)

    | Open_search_modal ->
        { model with SearchModal = Some (SearchModal.init ()) }, loadSearchLibraryCmd api

    | Search_modal_msg childMsg ->
        updateSearchModal api childMsg model

    | Escape_pressed ->
        match model.SearchModal, model.CurrentPage with
        | None, Dashboard ->
            model, Cmd.ofMsg (Dashboard_msg Pages.Dashboard.Types.CollapseCard)
        | _ ->
            // An open search modal owns Escape itself; other pages have no
            // expanded card to collapse.
            model, Cmd.none

    | Dashboard_msg childMsg ->
        match childMsg with
        | Pages.Dashboard.Types.Open_search_modal ->
            { model with SearchModal = Some (SearchModal.init ()) }, loadSearchLibraryCmd api
        | _ ->
            let childModel, childCmd = Pages.Dashboard.State.update api childMsg model.DashboardModel
            let extraCmd =
                match childMsg with
                | Pages.Dashboard.Types.SwitchTab _ ->
                    // Re-trigger Jellyfin sync on tab switch (server enforces 5-min cooldown)
                    Cmd.OfAsync.perform api.triggerJellyfinSync () JellyfinSyncTriggered
                | _ -> Cmd.none
            { model with DashboardModel = childModel },
            Cmd.batch [ Cmd.map Dashboard_msg childCmd; extraCmd ]

    | Movie_detail_msg childMsg ->
        let childModel, childCmd = Pages.MovieDetail.State.update api childMsg model.MovieDetailModel
        { model with MovieDetailModel = childModel }, Cmd.map Movie_detail_msg childCmd

    | Series_detail_msg childMsg ->
        let childModel, childCmd = Pages.SeriesDetail.State.update api childMsg model.SeriesDetailModel
        { model with SeriesDetailModel = childModel }, Cmd.map Series_detail_msg childCmd

    | Game_detail_msg childMsg ->
        let childModel, childCmd = Pages.GameDetail.State.update api childMsg model.GameDetailModel
        { model with GameDetailModel = childModel }, Cmd.map Game_detail_msg childCmd

    | Book_detail_msg childMsg ->
        let childModel, childCmd = Pages.BookDetail.State.update api childMsg model.BookDetailModel
        { model with BookDetailModel = childModel }, Cmd.map Book_detail_msg childCmd

    | Friend_list_msg childMsg ->
        let childModel, childCmd = Pages.Friends.State.update api childMsg model.FriendListModel
        { model with FriendListModel = childModel }, Cmd.map Friend_list_msg childCmd

    | Friend_detail_msg childMsg ->
        let childModel, childCmd = Pages.FriendDetail.State.update api childMsg model.FriendDetailModel
        { model with FriendDetailModel = childModel }, Cmd.map Friend_detail_msg childCmd

    | Catalog_list_msg childMsg ->
        let childModel, childCmd = Pages.Catalogs.State.update api childMsg model.CatalogListModel
        { model with CatalogListModel = childModel }, Cmd.map Catalog_list_msg childCmd

    | Catalog_detail_msg childMsg ->
        let childModel, childCmd = Pages.CatalogDetail.State.update api childMsg model.CatalogDetailModel
        { model with CatalogDetailModel = childModel }, Cmd.map Catalog_detail_msg childCmd

    | Stream_detail_msg childMsg ->
        let childModel, childCmd = Pages.StreamDetail.State.update adminApi childMsg model.StreamDetailModel
        { model with StreamDetailModel = childModel }, Cmd.map Stream_detail_msg childCmd

    | Settings_msg childMsg ->
        let childModel, childCmd = Pages.Settings.State.update api adminApi childMsg model.SettingsModel
        { model with SettingsModel = childModel }, Cmd.map Settings_msg childCmd

    | Styleguide_msg childMsg ->
        let childModel, childCmd = Pages.StyleGuide.State.update childMsg model.StyleGuideModel
        { model with StyleGuideModel = childModel }, Cmd.map Styleguide_msg childCmd

    // Jellyfin Auto-Sync
    | TriggerJellyfinSync ->
        model, Cmd.OfAsync.perform api.triggerJellyfinSync () JellyfinSyncTriggered

    | JellyfinSyncTriggered result ->
        match result with
        | Mediatheca.Shared.SyncStarted ->
            let pollCmd =
                Cmd.OfAsync.perform
                    (fun () -> async {
                        do! Async.Sleep 3000
                        return! api.getJellyfinSyncStatus ()
                    }) () JellyfinSyncStatusReceived
            { model with JellyfinSyncing = true }, pollCmd
        | _ ->
            // CooldownActive, AlreadyInProgress, NotConfigured — do nothing
            model, Cmd.none

    | JellyfinSyncStatusReceived status ->
        match status with
        | Mediatheca.Shared.SyncInProgress ->
            // Continue polling
            let pollCmd =
                Cmd.OfAsync.perform
                    (fun () -> async {
                        do! Async.Sleep 3000
                        return! api.getJellyfinSyncStatus ()
                    }) () JellyfinSyncStatusReceived
            model, pollCmd
        | Mediatheca.Shared.SyncCompleted (result, _lastSyncTime) ->
            let hasChanges =
                result.MoviesAdded > 0 || result.EpisodesAdded > 0 ||
                result.MoviesAutoAdded > 0 || result.SeriesAutoAdded > 0
            let refreshCmd =
                Cmd.OfAsync.either
                    api.getDashboardAllTab ()
                    (fun data -> Dashboard_msg (Pages.Dashboard.Types.AllTabLoaded data))
                    (fun ex -> Dashboard_msg (Pages.Dashboard.Types.TabLoadError ex.Message))
            let autoDismissCmd =
                if hasChanges then
                    Cmd.ofEffect (fun dispatch ->
                        Fable.Core.JS.setTimeout (fun () -> dispatch DismissJellyfinSyncToast) 5000 |> ignore
                    )
                else Cmd.none
            { model with
                JellyfinSyncing = false
                JellyfinSyncResult = if hasChanges then Some result else None
                ShowJellyfinSyncToast = hasChanges },
            Cmd.batch [ refreshCmd; autoDismissCmd ]
        | Mediatheca.Shared.SyncFailed (_error, _lastSyncTime) ->
            { model with JellyfinSyncing = false }, Cmd.none
        | Mediatheca.Shared.SyncIdle _ ->
            { model with JellyfinSyncing = false }, Cmd.none

    | DismissJellyfinSyncToast ->
        { model with ShowJellyfinSyncToast = false; JellyfinSyncResult = None }, Cmd.none
