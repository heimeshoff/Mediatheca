module Mediatheca.Tests.GoodreadsProgressTests

/// integration-y2ak4 (ADR-0075 §4): `Goodreads.parseProgress` -- a pure
/// function over one user-status-feed item's free text, recognizing the four
/// documented shapes (page/total, percent, finished, started) and returning
/// `None` for anything else (quotes, shelvings, reviews). No HTTP, no XML --
/// `GoodreadsSyncTests.fs` covers the feed fetch/join/idempotency end to end.

open Expecto
open Mediatheca.Server

[<Tests>]
let goodreadsProgressTests =
    testList "Goodreads.parseProgress (integration-y2ak4)" [

        testCase "\"is on page N of M of Title\" -> PageProgress" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is on page 137 of 248 of Dune")
                (Some (Goodreads.PageProgress (137, 248, "Dune")))
                "page progress"

        testCase "a title containing \" of \" is captured whole, not truncated at the first \" of \"" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is on page 120 of 300 of Lord of the Rings")
                (Some (Goodreads.PageProgress (120, 300, "Lord of the Rings")))
                "title's own \" of \" survives"

        testCase "\"is N% done with Title\" -> PercentProgress" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is 81% done with Dune")
                (Some (Goodreads.PercentProgress (81, "Dune")))
                "percent progress"

        testCase "a title with a colon is captured in full" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is 45% done with Fellowship: A Novel")
                (Some (Goodreads.PercentProgress (45, "Fellowship: A Novel")))
                "colon subtitle kept in the raw parse (normalization is a separate step)"

        testCase "\"finished reading Title\" -> Finished" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco finished reading Dune")
                (Some (Goodreads.Finished "Dune"))
                "finished reading"

        testCase "\"is finished with Title\" -> Finished" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is finished with Dune")
                (Some (Goodreads.Finished "Dune"))
                "is finished with"

        testCase "\"is starting Title\" -> Started" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is starting Dune")
                (Some (Goodreads.Started "Dune"))
                "is starting"

        testCase "\"started reading Title\" -> Started" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco started reading Dune")
                (Some (Goodreads.Started "Dune"))
                "started reading"

        testCase "a shelving update (\"added Title to shelf\") -> None" <| fun _ ->
            Expect.isNone
                (Goodreads.parseProgress "Marco added Dune to his currently-reading shelf")
                "not one of the four recognized shapes"

        testCase "a review/quote update -> None" <| fun _ ->
            Expect.isNone
                (Goodreads.parseProgress "Marco wrote a review of Dune. 5 of 5 stars")
                "a review is skipped silently, not an error"

        testCase "HTML entities are decoded before matching (&amp;, &#39;)" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco is 50% done with Fish &amp; Chips")
                (Some (Goodreads.PercentProgress (50, "Fish & Chips")))
                "&amp; decoded to &"
            Expect.equal
                (Goodreads.parseProgress "Marco is on page 10 of 20 of Bob&#39;s Diary")
                (Some (Goodreads.PageProgress (10, 20, "Bob's Diary")))
                "&#39; decoded to '"

        testCase "matching is case-insensitive and tolerant of extra whitespace" <| fun _ ->
            Expect.equal
                (Goodreads.parseProgress "Marco   IS ON PAGE   10   OF   20   OF   Dune")
                (Some (Goodreads.PageProgress (10, 20, "Dune")))
                "case/whitespace tolerant"
    ]
