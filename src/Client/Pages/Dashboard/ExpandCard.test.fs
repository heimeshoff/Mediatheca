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

        testCase "two cards over one query share it, with the abandoned-filter left to the view" <| fun () ->
            Expect.equal (DashboardCard.query AllNextEpisode) SeriesNextUpQuery "All tab's Next episode"
            Expect.equal (DashboardCard.query SeriesNextUp) SeriesNextUpQuery "Series tab's Next Up"
            Expect.equal (DashboardCard.query AllMoviesToWatch) (DashboardCard.query MoviesToWatch) "Movies to Watch on both tabs"
    ]

Mocha.runTests expandCardTests |> ignore
