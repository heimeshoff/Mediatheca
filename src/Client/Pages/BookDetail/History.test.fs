/// books-wk67x (amending ADR-0076 §2): client-side coverage for
/// `History.orderNewestFirst` — the History list's pure ordering seam.
/// Proves several same-day, same-source entries all survive (append-only
/// history, never a collapse) and come back newest-first by
/// `(ObservedOn, EntryId)`.
module Mediatheca.Client.Pages.BookDetail.HistoryTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.History

let private row (entryId: int64) (observedOn: string) (percent: int) : ReadingProgressDto =
    { EntryId = entryId
      ObservedOn = observedOn
      Source = ProgressSource.Audible
      Percent = percent
      Position = None
      Kind = Observed }

let historyTests =
    testList "books-wk67x: History.orderNewestFirst" [

        testCase "two same-day same-source rows plus an older-day row all survive, newest first by (ObservedOn, EntryId)" <| fun () ->
            let older = row 1L "2026-09-10" 10
            let sameDayFirst = row 2L "2026-09-18" 20
            let sameDaySecond = row 3L "2026-09-18" 35
            let result = orderNewestFirst [ older; sameDayFirst; sameDaySecond ]
            Expect.equal (List.length result) 3 "no entry is dropped — same-day, same-source rows both survive"
            Expect.equal (result |> List.map (fun r -> r.EntryId)) [ 3L; 2L; 1L ]
                "newest first: the later same-day entry (higher EntryId) comes before its sibling, and both come before the older day"

        testCase "an empty history orders to an empty list" <| fun () ->
            Expect.isEmpty (orderNewestFirst []) "nothing to order"

        testCase "a single entry orders to itself" <| fun () ->
            let only = row 7L "2026-01-01" 50
            Expect.equal (orderNewestFirst [ only ]) [ only ] "one entry, unchanged"
    ]

Mocha.runTests historyTests |> ignore
