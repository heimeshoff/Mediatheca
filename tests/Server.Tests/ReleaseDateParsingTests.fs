module Mediatheca.Tests.ReleaseDateParsingTests

open System
open Expecto
open Mediatheca.Server

/// games-ev65k (ADR-0043): `ReleaseDateParsing.tryParse`/`tryParseSortable`
/// against the four Steam `release_date.date` shapes the task calls out —
/// exact date, month-year, bare year, and TBA/unparseable — plus the
/// day-of-month convention for the two partial-precision shapes.

[<Tests>]
let tests =
    testList "ReleaseDateParsing (games-ev65k)" [

        testCase "Exact date \"25 Oct, 2026\" parses to 2026-10-25" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "25 Oct, 2026") (Some "2026-10-25") "day-precision exact date"

        testCase "Exact date \"Oct 25, 2026\" (alt month/day order) parses to 2026-10-25" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "Oct 25, 2026") (Some "2026-10-25") "alt order also recognized"

        testCase "Exact date with full month name \"25 October, 2026\" parses to 2026-10-25" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "25 October, 2026") (Some "2026-10-25") "full month name recognized"

        testCase "Month-year \"October 2026\" parses to the first of the month, 2026-10-01" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "October 2026") (Some "2026-10-01") "month-year sorts as the 1st of that month (worker's day-of-month convention)"

        testCase "Month-year abbreviated \"Oct 2026\" also parses to 2026-10-01" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "Oct 2026") (Some "2026-10-01") "abbreviated month-year recognized"

        testCase "Bare year \"2026\" parses to 1 January, 2026-01-01" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "2026") (Some "2026-01-01") "bare year sorts as 1 Jan (worker's day-of-month convention)"

        testCase "\"Coming soon\" is unparseable — None, not an error" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "Coming soon") None "TBA phrasing yields no parsed date"

        testCase "\"To be announced\" is unparseable — None" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "To be announced") None "TBA phrasing yields no parsed date"

        testCase "Empty string is unparseable — None" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "") None "empty raw string yields no parsed date"

        testCase "Whitespace-only string is unparseable — None" <| fun _ ->
            Expect.equal (ReleaseDateParsing.tryParseSortable "   ") None "whitespace-only raw string yields no parsed date"

        testCase "tryParse's DateTime overload agrees with tryParseSortable's ISO string" <| fun _ ->
            match ReleaseDateParsing.tryParse "25 Oct, 2026" with
            | Some d -> Expect.equal d (DateTime(2026, 10, 25)) "underlying DateTime matches"
            | None -> failtest "Expected a parsed date"
    ]
