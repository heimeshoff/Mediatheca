/// design-system-fryq7: the search modal used to be seeded synchronously off
/// the three list pages' own already-loaded models. Those pages are gone —
/// the modal now opens empty (`SearchModal.init`) and the caller fetches a
/// fresh library snapshot on every open, landing via `Library_loaded`
/// (`applyLibraryLoaded`). This covers that seeded-from-fetch path: an
/// initially-empty modal finds nothing, then finds matching library items
/// once the fetch lands.
module Mediatheca.Client.Components.SearchModalTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Components.SearchModal

let private movie (slug: string) (name: string) : MovieListItem =
    { Slug = slug; Name = name; Year = 2020; PosterRef = None; Genres = []; TmdbRating = None; InFocus = false }

let private series (slug: string) (name: string) : SeriesListItem =
    { Slug = slug
      Name = name
      Year = 2019
      PosterRef = None
      Genres = []
      TmdbRating = None
      Status = Returning
      SeasonCount = 1
      EpisodeCount = 10
      WatchedEpisodeCount = 0
      NextUp = None
      IsAbandoned = false
      InFocus = false
      NextAirDate = None }

let private game (slug: string) (name: string) : GameListItem =
    { Slug = slug
      Name = name
      Year = 2021
      CoverRef = None
      Genres = []
      Status = Backlog
      TotalPlayTimeMinutes = 0
      HltbHours = None
      PersonalRating = None
      RawgRating = None
      PlayFacets = { Solo = false; CoopCouch = false; CoopOnline = false; VersusCouch = false; VersusOnline = false; RemotePlayTogether = false; Vr = NoVr }
      DeckCompat = Unknown
      ReleaseDate = { Raw = ""; Parsed = None; ComingSoon = false; IsUnreleased = false } }

let private book (slug: string) (title: string) : BookListItem =
    { Slug = slug
      Title = title
      Authors = []
      Year = Some 2022
      CoverRef = None
      Subjects = []
      Format = BookFormat.Unknown
      Status = BookStatus.Backlog
      ProgressPercent = 0
      ProgressSource = None
      ProgressObservedOn = None
      PersonalRating = None
      FinishedAt = None }

let searchModalTests =
    testList "design-system-fryq7: SearchModal seeded-from-fetch library snapshot" [

        testCase "a freshly-opened modal (SearchModal.init) has no library items to search" <| fun () ->
            let model = init ()
            Expect.equal model.LibraryMovies [] "opens empty — no list model to seed from anymore"
            Expect.equal model.LibrarySeries [] "opens empty — no list model to seed from anymore"
            Expect.equal model.LibraryGames [] "opens empty — no list model to seed from anymore"
            Expect.equal model.LibraryBooks [] "opens empty — no list model to seed from anymore"
            let results = filterLibrary "dune" model.LibraryMovies model.LibrarySeries model.LibraryGames model.LibraryBooks
            Expect.isEmpty results "nothing to match before the fetch lands"

        testCase "applyLibraryLoaded (the Library_loaded reducer step) populates the snapshot, and search then finds matches" <| fun () ->
            let movies = [ movie "dune-2021" "Dune" ]
            let seriesItems = [ series "dune-prophecy" "Dune: Prophecy" ]
            let games = [ game "dune-spice-wars" "Dune: Spice Wars" ]
            let books = [ book "dune-messiah" "Dune Messiah" ]

            let model = init () |> applyLibraryLoaded movies seriesItems games books

            Expect.equal model.LibraryMovies movies "movies land from the fetch"
            Expect.equal model.LibrarySeries seriesItems "series land from the fetch"
            Expect.equal model.LibraryGames games "games land from the fetch"
            Expect.equal model.LibraryBooks books "books land from the fetch"

            let results = filterLibrary "dune" model.LibraryMovies model.LibrarySeries model.LibraryGames model.LibraryBooks
            let names = results |> List.map (fun r -> r.Name) |> Set.ofList
            Expect.isTrue (names.Contains "Dune") "the movie is found post-fetch"
            Expect.isTrue (names.Contains "Dune: Prophecy") "the series is found post-fetch"
            Expect.isTrue (names.Contains "Dune: Spice Wars") "the game is found post-fetch"
            Expect.isTrue (names.Contains "Dune Messiah") "the book is found post-fetch"

        testCase "filterLibrary returns a book by fuzzy title tagged MediaType.Book" <| fun () ->
            let books = [ book "project-hail-mary" "Project Hail Mary" ]
            let model = init () |> applyLibraryLoaded [] [] [] books

            let results = filterLibrary "hail mary" model.LibraryMovies model.LibrarySeries model.LibraryGames model.LibraryBooks

            match results |> List.tryFind (fun r -> r.Slug = "project-hail-mary") with
            | Some r -> Expect.equal r.MediaType MediaType.Book "a book match is tagged MediaType.Book, not any other media type"
            | None -> failwith "expected the book to fuzzy-match on its title"
    ]

let private openLibraryResult (workKey: string) (title: string) : OpenLibrarySearchResult =
    { WorkKey = workKey; Title = title; Authors = []; Year = Some 1965; CoverId = None
      Isbn13 = None; EditionKey = None; Subjects = []; PageCount = None; CoverUrl = None }

let private audibleResult (asin: string) (title: string) : AudibleSearchResult =
    { Asin = asin; Title = title; Authors = []; Narrators = [ "Simon Vance" ]
      RuntimeMinutes = Some 660; ReleaseYear = Some 1965; CoverUrl = None; SeriesName = None }

/// books-g7g1j: the Books tab's own reducer seams — `applyBooksSearchResults`
/// (merge + stale-drop) and `booksSearchPlan` (which sources fire), plus the
/// "add anyway" duplicate-retry helper `forceDuplicateImport` shared with
/// RAWG/Steam.
let booksSearchModalTests =
    testList "books-g7g1j: SearchModal Books tab reducer seams" [

        testCase "applyBooksSearchResults merges Open Library and Audible responses into the Model independently" <| fun () ->
            let model = init ()
            let withOpenLibrary = model |> applyBooksSearchResults model.SearchVersion (OpenLibraryResponse [ openLibraryResult "OL1W" "Dune" ])
            let withBoth = withOpenLibrary |> applyBooksSearchResults model.SearchVersion (AudibleResponse [ audibleResult "B002V1O1AY" "Dune" ])

            Expect.equal (withBoth.OpenLibraryResults |> List.map (fun r -> r.Title)) [ "Dune" ] "Open Library results land"
            Expect.equal (withBoth.AudibleResults |> List.map (fun r -> r.Title)) [ "Dune" ] "Audible results land alongside them"
            Expect.isFalse withBoth.IsSearchingOpenLibrary "the Open Library in-flight flag clears on arrival"
            Expect.isFalse withBoth.IsSearchingAudible "the Audible in-flight flag clears on arrival"

        testCase "applyBooksSearchResults drops a response whose version has been superseded by further typing" <| fun () ->
            let model = { init () with SearchVersion = 2; IsSearchingOpenLibrary = true }
            let staleVersion = 1

            let result = model |> applyBooksSearchResults staleVersion (OpenLibraryResponse [ openLibraryResult "OL1W" "Dune" ])

            Expect.equal result.OpenLibraryResults [] "a stale-version response never overwrites the (empty) current results"
            Expect.isTrue result.IsSearchingOpenLibrary "the in-flight flag is untouched by a dropped stale response"

        testCase "booksSearchPlan returns both sources when both are checked" <| fun () ->
            Expect.equal (booksSearchPlan true true) [ OpenLibrary; Audible ] "both sources fire when both are checked"

        testCase "booksSearchPlan returns only Open Library when Audible is unchecked" <| fun () ->
            Expect.equal (booksSearchPlan true false) [ OpenLibrary ] "toggling Audible off skips its command entirely"

        testCase "booksSearchPlan returns no sources when both are unchecked" <| fun () ->
            Expect.equal (booksSearchPlan false false) [] "both unchecked means nothing fires"

        testCase "Duplicate_found on a Books import shows the prompt carrying a FromOpenLibrary pending import, and force-add flips SkipDuplicateCheck" <| fun () ->
            let pending = FromOpenLibrary { WorkKey = "OL1W"; EditionKey = Some "OL1M"; Isbn13 = Some "9780000000000"; CoverId = None; Title = "Dune"; SkipDuplicateCheck = false }
            let model = { init () with DuplicatePrompt = Some ("dune-1965", "Dune", pending) }

            match model.DuplicatePrompt with
            | Some (existingSlug, existingName, FromOpenLibrary req) ->
                Expect.equal existingSlug "dune-1965" "the prompt carries the existing library slug"
                Expect.equal existingName "Dune" "the prompt carries the existing library title"
                Expect.isFalse req.SkipDuplicateCheck "the original request never skipped the duplicate check"
            | _ -> failwith "expected a DuplicatePrompt carrying FromOpenLibrary"

            match forceDuplicateImport pending with
            | FromOpenLibrary req -> Expect.isTrue req.SkipDuplicateCheck "\"add anyway\" resubmits the same request with SkipDuplicateCheck = true"
            | _ -> failwith "expected forceDuplicateImport to keep the FromOpenLibrary shape"

        testCase "forceDuplicateImport flips SkipDuplicateCheck for a FromAudible pending import too" <| fun () ->
            let pending = FromAudible { Asin = "B002V1O1AY"; SkipDuplicateCheck = false }
            match forceDuplicateImport pending with
            | FromAudible req -> Expect.isTrue req.SkipDuplicateCheck "\"add anyway\" resubmits with SkipDuplicateCheck = true"
            | _ -> failwith "expected forceDuplicateImport to keep the FromAudible shape"

        testCase "pendingImportMediaType routes Books imports to MediaType.Book and Games imports to MediaType.Game" <| fun () ->
            Expect.equal (pendingImportMediaType (FromOpenLibrary { WorkKey = "OL1W"; EditionKey = None; Isbn13 = None; CoverId = None; Title = "x"; SkipDuplicateCheck = false })) MediaType.Book "Open Library duplicates open a book"
            Expect.equal (pendingImportMediaType (FromAudible { Asin = "B1"; SkipDuplicateCheck = false })) MediaType.Book "Audible duplicates open a book"
            Expect.equal (pendingImportMediaType (FromRawg { Name = "x"; Year = 2020; Genres = []; Description = ""; CoverRef = None; BackdropRef = None; RawgId = None; RawgRating = None; SkipDuplicateCheck = false })) MediaType.Game "RAWG duplicates still open a game"
    ]

Mocha.runTests searchModalTests |> ignore
Mocha.runTests booksSearchModalTests |> ignore
