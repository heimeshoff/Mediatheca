/// Dashboard card expansion: `State.update` is driven directly and asserted
/// on the model, the same pattern `FamilyTokenRejected.test.fs` and
/// `LocalCopyRemovalDialog.test.fs` use. The api stand-in is a plain JS
/// object carrying only the one member `ExpandCard` touches -- a
/// `Unchecked.defaultof<IMediathecaApi>` is `null` in Fable and would throw
/// the moment `update` built the fetch command.
module Mediatheca.Client.Pages.Dashboard.ExpandCardTests

open Fable.Core.JsInterop
open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Dashboard.Types
open Mediatheca.Client.Pages.Dashboard.State

let private fakeApi : IMediathecaApi =
    createObj [
        "getDashboardCardItems" ==> (fun (_: DashboardCardQuery) -> async { return MovieItems [] })
    ]
    |> unbox

let private book (slug: string) (percent: int) : DashboardBookItem = {
    Slug = slug
    Title = slug
    Authors = []
    CoverRef = None
    ProgressPercent = percent
    ProgressSource = Some ProgressSource.Audible
    Finished = false
    FinishedOn = None
}

let private loaded () =
    let model, _ = init ()
    { model with IsLoading = false }

let private movie (slug: string) : MovieListItem = {
    Slug = slug
    Name = slug
    Year = 2024
    PosterRef = None
    Genres = []
    TmdbRating = None
    InFocus = false
}

let expandCardTests =
    testList "Dashboard.State card expansion" [

        testCase "ExpandCard marks the card expanded and its items loading" <| fun () ->
            let updated, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            Expect.equal
                updated.Expanded
                (Some { Card = MoviesRecentlyAdded; Items = ExpandedLoading })
                "the card is expanded while its unlimited items are fetched"

        testCase "a reply for the expanded card lands as ExpandedReady" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let items = MovieItems [ movie "dune-2021"; movie "heat-1995" ]
            let updated, _ = update fakeApi (ExpandedItemsLoaded (MoviesRecentlyAdded, Ok items)) expanded
            Expect.equal
                updated.Expanded
                (Some { Card = MoviesRecentlyAdded; Items = ExpandedReady items })
                "the fetched items replace the loading marker"

        testCase "a failed reply lands as ExpandedFailed and keeps the card expanded" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let updated, _ = update fakeApi (ExpandedItemsLoaded (MoviesRecentlyAdded, Error "HTTP 500")) expanded
            Expect.equal
                updated.Expanded
                (Some { Card = MoviesRecentlyAdded; Items = ExpandedFailed "HTTP 500" })
                "the view keeps the tab's own items and shows the failure inline"

        testCase "CollapseCard clears the expansion" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let updated, _ = update fakeApi CollapseCard expanded
            Expect.isNone updated.Expanded "nothing is expanded after collapsing"

        testCase "a reply that lands after a collapse is dropped" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let collapsed, _ = update fakeApi CollapseCard expanded
            let updated, _ = update fakeApi (ExpandedItemsLoaded (MoviesRecentlyAdded, Ok (MovieItems []))) collapsed
            Expect.isNone updated.Expanded "a stale reply must not re-open the card"

        testCase "a reply for a different card than the one expanded is dropped" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let updated, _ = update fakeApi (ExpandedItemsLoaded (MoviesTopActors, Ok (PersonItems []))) expanded
            Expect.equal
                updated.Expanded
                (Some { Card = MoviesRecentlyAdded; Items = ExpandedLoading })
                "only the expanded card's own reply is applied"

        testCase "expanding another card takes over from the current one" <| fun () ->
            let first, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let second, _ = update fakeApi (ExpandCard MoviesTopActors) first
            Expect.equal
                second.Expanded
                (Some { Card = MoviesTopActors; Items = ExpandedLoading })
                "one card at a time"

        testCase "switching tabs collapses the expanded card" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            let updated, _ = update fakeApi (SwitchTab SeriesTab) expanded
            Expect.isNone updated.Expanded "an expansion belongs to the tab it was opened on"

        testCase "ExpandCard's command is the fetch alone, no scroll batch (ADR-0073)" <| fun () ->
            let _, cmd = update fakeApi (ExpandCard MoviesRecentlyAdded) (loaded ())
            Expect.equal (List.length cmd) 1 "the 50ms scroll guess is retired; the layout effect owns the scroll"

        testCase "two cards over one query share it, with the abandoned-filter left to the view" <| fun () ->
            Expect.equal (DashboardCard.query AllNextEpisode) SeriesNextUpQuery "All tab's Next episode"
            Expect.equal (DashboardCard.query SeriesNextUp) SeriesNextUpQuery "Series tab's Next Up"

        // intelligence-b1nz5: the All tab's "Movies to Watch" card lingers on
        // recently-watched movies; the Movies tab's own card stays strictly
        // unwatched-only. Sharing one query would have forced that choice on
        // both tabs at once, so each now runs its own query.
        testCase "the All tab's Movies to Watch card has its own query, separate from the Movies tab's strict one" <| fun () ->
            Expect.equal (DashboardCard.query AllMoviesToWatch) AllMoviesToWatchQuery "All tab lingers on recently-watched movies"
            Expect.equal (DashboardCard.query MoviesToWatch) MoviesToWatchQuery "Movies tab stays strictly unwatched-only"
            Expect.notEqual (DashboardCard.query AllMoviesToWatch) (DashboardCard.query MoviesToWatch) "the two cards no longer share a query"

        // intelligence-dnv2y: the All tab's "Reading" card lingers on the
        // 7-day finished window; the Books tab's own three rails each stay
        // strict, mirroring the Movies split above — every card gets its own
        // query so a book slug can never resolve to the wrong query's shape.
        testCase "the All tab's Reading card and the Books tab's three rails each have their own distinct query" <| fun () ->
            Expect.equal (DashboardCard.query AllReading) AllCurrentlyReading "All tab lingers on the 7-day finished window"
            Expect.equal (DashboardCard.query BooksReading) BooksCurrentlyReading "Books tab's Currently Reading stays strict"
            Expect.equal (DashboardCard.query BooksFinished) BooksRecentlyFinished "Books tab's Recently Finished rail"
            Expect.equal (DashboardCard.query BooksAdded) BooksRecentlyAdded "Books tab's Recently Added rail"
            let queries = [ AllCurrentlyReading; BooksCurrentlyReading; BooksRecentlyFinished; BooksRecentlyAdded ]
            Expect.equal (queries |> List.distinct |> List.length) (List.length queries) "no two Reading cards share a query"

        // design-system-btmdx / ADR-0073 §1: a surviving item only ever
        // translates (no fade) across expand/collapse — that guarantee comes
        // from the collapsed and expanded faces of the SAME card deriving an
        // identical `data-flip-key` for the same item, which in turn depends
        // on `ExpandCard`/`ExpandedItemsLoaded` preserving the same `Card`
        // identity end to end. Exercised here for the Reading card's own
        // item shape (`BookReadingItems`), the same way the Movies-shaped
        // cases above exercise `MovieItems`/`PersonItems`.
        testCase "expanding the Reading card keeps the same card identity through to the loaded reply, so every item's FLIP key stays keyed on its own slug" <| fun () ->
            let expanded, _ = update fakeApi (ExpandCard AllReading) (loaded ())
            Expect.equal expanded.Expanded (Some { Card = AllReading; Items = ExpandedLoading }) "the Reading card is expanded while its unlimited items are fetched"

            let items = BookReadingItems [ book "dune-1965" 40; book "project-hail-mary-2021" 100 ]
            let updated, _ = update fakeApi (ExpandedItemsLoaded (AllReading, Ok items)) expanded
            match updated.Expanded with
            | Some { Card = AllReading; Items = ExpandedReady (BookReadingItems loadedItems) } ->
                Expect.equal (loadedItems |> List.map (fun b -> b.Slug)) [ "dune-1965"; "project-hail-mary-2021" ]
                    "each item keeps its own slug — the identity `cardItemKey`/`Motion.flipKey` key off of"
            | other -> failtestf "Expected the Reading card's own BookReadingItems reply, got %A" other
    ]

Mocha.runTests expandCardTests |> ignore
