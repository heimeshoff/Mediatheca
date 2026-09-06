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

let searchModalTests =
    testList "design-system-fryq7: SearchModal seeded-from-fetch library snapshot" [

        testCase "a freshly-opened modal (SearchModal.init) has no library items to search" <| fun () ->
            let model = init ()
            Expect.equal model.LibraryMovies [] "opens empty — no list model to seed from anymore"
            Expect.equal model.LibrarySeries [] "opens empty — no list model to seed from anymore"
            Expect.equal model.LibraryGames [] "opens empty — no list model to seed from anymore"
            let results = filterLibrary "dune" model.LibraryMovies model.LibrarySeries model.LibraryGames
            Expect.isEmpty results "nothing to match before the fetch lands"

        testCase "applyLibraryLoaded (the Library_loaded reducer step) populates the snapshot, and search then finds matches" <| fun () ->
            let movies = [ movie "dune-2021" "Dune" ]
            let seriesItems = [ series "dune-prophecy" "Dune: Prophecy" ]
            let games = [ game "dune-spice-wars" "Dune: Spice Wars" ]

            let model = init () |> applyLibraryLoaded movies seriesItems games

            Expect.equal model.LibraryMovies movies "movies land from the fetch"
            Expect.equal model.LibrarySeries seriesItems "series land from the fetch"
            Expect.equal model.LibraryGames games "games land from the fetch"

            let results = filterLibrary "dune" model.LibraryMovies model.LibrarySeries model.LibraryGames
            let names = results |> List.map (fun r -> r.Name) |> Set.ofList
            Expect.isTrue (names.Contains "Dune") "the movie is found post-fetch"
            Expect.isTrue (names.Contains "Dune: Prophecy") "the series is found post-fetch"
            Expect.isTrue (names.Contains "Dune: Spice Wars") "the game is found post-fetch"
    ]

Mocha.runTests searchModalTests |> ignore
