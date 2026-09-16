/// design-system-fryq7: coverage for `Route.parseUrl`'s post-list-page-removal
/// shape — bare `/movies`, `/series`, `/games` (old bookmarks) now resolve to
/// the Dashboard rather than a dedicated list page or `Not_found`, while the
/// parameterized detail routes are untouched.
///
/// books-f33e2 adds `["books"; slug]` coverage — this task owns the
/// `Router.fs` edit that introduces `Book_detail` (`books-g7g1j` confirms
/// rather than duplicates it, per the refinement note in both tasks).
module Mediatheca.Client.RouteTests

open Fable.Mocha
open Mediatheca.Client.Router

let routeTests =
    testList "design-system-fryq7: Route.parseUrl bare list segments -> Dashboard" [

        testCase "[\"movies\"] resolves to Dashboard, not a list page" <| fun () ->
            Expect.equal (Route.parseUrl [ "movies" ]) Dashboard "bare /movies is an old bookmark — it lands on the Dashboard"

        testCase "[\"series\"] resolves to Dashboard, not a list page" <| fun () ->
            Expect.equal (Route.parseUrl [ "series" ]) Dashboard "bare /series is an old bookmark — it lands on the Dashboard"

        testCase "[\"games\"] resolves to Dashboard, not a list page" <| fun () ->
            Expect.equal (Route.parseUrl [ "games" ]) Dashboard "bare /games is an old bookmark — it lands on the Dashboard"

        testCase "[\"movies\"; slug] still resolves to the Movie_detail page" <| fun () ->
            Expect.equal (Route.parseUrl [ "movies"; "the-matrix" ]) (Movie_detail "the-matrix") "the parameterized detail route is untouched"

        testCase "[\"series\"; slug] still resolves to the Series_detail page" <| fun () ->
            Expect.equal (Route.parseUrl [ "series"; "the-wire" ]) (Series_detail "the-wire") "the parameterized detail route is untouched"

        testCase "[\"games\"; slug] still resolves to the Game_detail page" <| fun () ->
            Expect.equal (Route.parseUrl [ "games"; "outer-wilds" ]) (Game_detail "outer-wilds") "the parameterized detail route is untouched"

        testCase "[\"books\"; slug] resolves to the Book_detail page (books-f33e2)" <| fun () ->
            Expect.equal (Route.parseUrl [ "books"; "project-hail-mary" ]) (Book_detail "project-hail-mary") "the new Books detail route"

        testCase "Route.isDashboardSection covers Dashboard and the four detail pages" <| fun () ->
            Expect.isTrue (Route.isDashboardSection Dashboard) "Dashboard itself"
            Expect.isTrue (Route.isDashboardSection (Movie_detail "x")) "a movie detail page is reached from, and returns to, the Dashboard"
            Expect.isTrue (Route.isDashboardSection (Series_detail "x")) "a series detail page is reached from, and returns to, the Dashboard"
            Expect.isTrue (Route.isDashboardSection (Game_detail "x")) "a game detail page is reached from, and returns to, the Dashboard"
            Expect.isTrue (Route.isDashboardSection (Book_detail "x")) "a book detail page is reached from, and returns to, the Dashboard (books-f33e2)"
            Expect.isFalse (Route.isDashboardSection Catalog_list) "an unrelated page is not part of the Dashboard section"
    ]

Mocha.runTests routeTests |> ignore
