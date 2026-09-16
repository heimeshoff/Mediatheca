/// books-f33e2: client-side coverage for `Format.lengthLine`/`seriesLine` —
/// the book detail hero's pure display-formatting seams.
module Mediatheca.Client.Pages.BookDetail.FormatTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.Format

let formatTests =
    testList "books-f33e2: Format.lengthLine / Format.seriesLine" [

        testCase "an audiobook with runtime and a narrator renders hours, minutes and the narrator" <| fun () ->
            let result = lengthLine Audiobook (Some 552) None [ "X" ]
            Expect.equal result (Some "9 h 12 min · narrated by X") "9h12m, one narrator"

        testCase "an audiobook with no narrators renders just the time" <| fun () ->
            let result = lengthLine Audiobook (Some 90) None []
            Expect.equal result (Some "1 h 30 min") "1h30m, no narrator"

        testCase "a print book renders its page count" <| fun () ->
            let result = lengthLine Print None (Some 384) []
            Expect.equal result (Some "384 pages") "384 pages"

        testCase "an ebook with no page count renders nothing" <| fun () ->
            let result = lengthLine Ebook None None []
            Expect.equal result None "no cache data yet"

        testCase "an audiobook with no runtime renders nothing, even with narrators" <| fun () ->
            let result = lengthLine Audiobook None None [ "X" ]
            Expect.equal result None "no runtime, honest degradation"

        testCase "series line combines position and name" <| fun () ->
            let result = seriesLine (Some "The Expanse") (Some 2)
            Expect.equal result (Some "Book 2 of The Expanse") "position + name"

        testCase "series line is absent without a position" <| fun () ->
            let result = seriesLine (Some "The Expanse") None
            Expect.equal result None "no position, no claim"
    ]

Mocha.runTests formatTests |> ignore
