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

        testCase "meta line joins publisher and language" <| fun () ->
            let result = metaLine (Some "Penguin") (Some "English")
            Expect.equal result (Some "Penguin · English") "publisher + language"

        testCase "meta line renders just the publisher when language is absent" <| fun () ->
            let result = metaLine (Some "Penguin") None
            Expect.equal result (Some "Penguin") "publisher only"

        testCase "meta line is absent when publisher and language are both absent" <| fun () ->
            let result = metaLine None None
            Expect.equal result None "books-depwh: a published date alone renders no line"

        // integration-qqpq9 (ADR-0043/ADR-0089): the Links panel's Companion
        // PDF entry only appears when the book carries one -- proxy for the
        // page's own "a Companion PDF link renders only when the DTO field
        // is Some" acceptance criterion, since every entry this function
        // returns renders through the SAME target="_blank"/rel="noopener"
        // markup (Views.fs's `linksCard`).
        testCase "bookLinks omits Companion PDF when the book has none" <| fun () ->
            let result = bookLinks (Some "ASIN123") None None
            Expect.equal result [ "Audible", "https://www.audible.de/pd/ASIN123" ] "no Companion PDF entry"

        testCase "bookLinks includes Companion PDF, in order, when the book has one" <| fun () ->
            let result = bookLinks (Some "ASIN123") (Some "/works/OL123W") (Some "/pdfs/ASIN123.pdf")
            Expect.equal
                result
                [ "Audible", "https://www.audible.de/pd/ASIN123"
                  "Open Library", "https://openlibrary.org/works/OL123W"
                  "Companion PDF", "/pdfs/ASIN123.pdf" ]
                "all three links, Companion PDF last"

        testCase "bookLinks is empty when the book has none of the three" <| fun () ->
            let result = bookLinks None None None
            Expect.equal result [] "no links at all"
    ]

Mocha.runTests formatTests |> ignore
