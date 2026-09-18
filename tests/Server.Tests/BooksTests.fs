module Mediatheca.Tests.BooksTests

open Expecto
open Mediatheca.Server.Books
open Mediatheca.Shared

let private sampleBookData: BookAddedData = {
    Title = "Project Hail Mary"
    Authors = [ "Andy Weir" ]
    Year = Some 2021
    CoverRef = None
    Subjects = [ "Science Fiction" ]
    Format = Audiobook
    ExternalIds = [ AudibleAsin "B08GB43BXN" ]
}

let private observation (percent: int) (source: ProgressSource) (observedOn: string) (finished: bool) : ReadingProgressObservedData =
    { Percent = percent; Position = None; Source = source; ObservedOn = observedOn; Finished = finished }

// books-wk67x: `reconstituteEvents` (a plain `BookEvent list`, auto-numbered
// entry ids) is the right reconstitution path for these tests — none of
// them round-trip an entry id through a real store, only through ORDER,
// which auto-numbering preserves exactly.
let private givenWhenThen (given: BookEvent list) (command: BookCommand) =
    let state = reconstituteEvents given
    decide state command

let private applyEvents (events: BookEvent list) = reconstituteEvents events

[<Tests>]
let booksTests =
    testList "Books" [

        testCase "Adding a book creates it with correct state" <| fun _ ->
            match givenWhenThen [] (Add_book_to_library sampleBookData) with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should produce one event"
                match applyEvents events with
                | Active book ->
                    Expect.equal book.Title "Project Hail Mary" "Title should match"
                    Expect.equal book.Authors [ "Andy Weir" ] "Authors should match"
                    Expect.equal book.Format Audiobook "Format should match"
                    Expect.equal book.Status BookStatus.Backlog "Status should default to Backlog"
                    Expect.equal book.PersonalRating None "PersonalRating should default to None"
                    Expect.equal book.FinishedOn None "FinishedOn should default to None"
                    Expect.isTrue (Set.isEmpty book.RecommendedBy) "RecommendedBy should be empty"
                    Expect.isEmpty book.Observations "Observations should be empty"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Cannot add a book that already exists" <| fun _ ->
            match givenWhenThen [ Book_added_to_library sampleBookData ] (Add_book_to_library sampleBookData) with
            | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
            | Ok _ -> failtest "Expected error"

        testCase "Cannot add to a removed book" <| fun _ ->
            match givenWhenThen [ Book_added_to_library sampleBookData; Book_removed_from_library ] (Add_book_to_library sampleBookData) with
            | Error msg -> Expect.stringContains msg "removed" "Should say removed"
            | Ok _ -> failtest "Expected error"

        testCase "An Add_book_to_library with two external ids of the same kind is refused" <| fun _ ->
            let dup = { sampleBookData with ExternalIds = [ AudibleAsin "B01"; AudibleAsin "B02" ] }
            match givenWhenThen [] (Add_book_to_library dup) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error"

        testCase "Commands on a removed book are refused" <| fun _ ->
            match givenWhenThen [ Book_added_to_library sampleBookData; Book_removed_from_library ] (Set_personal_rating (Some 5)) with
            | Error msg -> Expect.stringContains msg "removed" "Should say removed"
            | Ok _ -> failtest "Expected error"

        testCase "Commands on a book that doesn't exist are refused" <| fun _ ->
            match givenWhenThen [] Remove_book_from_library with
            | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
            | Ok _ -> failtest "Expected error"

        testCase "A percent of 101 is refused, not clamped" <| fun _ ->
            match givenWhenThen [ Book_added_to_library sampleBookData ] (Observe_reading_progress (observation 101 Audible "2026-01-01" false)) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error"

        testCase "An observation with the same percent as the same source's prior observation emits nothing" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 20 Audible "2026-01-01" false)
            ]
            match givenWhenThen given (Observe_reading_progress (observation 20 Audible "2026-01-02" false)) with
            | Ok events -> Expect.isEmpty events "Same-source same-percent observation should be a no-op"
            | Error e -> failtest $"Expected success but got: {e}"

        // books-wk67x (amending ADR-0076 §2): the exact incident this task
        // exists to fix — a same-day, same-source observation must never
        // overwrite a prior.
        testCase "A prior at 14%%/75min, then a same-day same-source observation at 11%%/60min, yields two entries and leaves the prior unchanged" <| fun _ ->
            let priorData = { observation 14 Audible "2026-09-18" false with Position = Some (Minutes (75, Some 539)) }
            let given = [
                Book_added_to_library sampleBookData
                Prior_reading_progress_recorded priorData
            ]
            let obsData = { observation 11 Audible "2026-09-18" false with Position = Some (Minutes (60, Some 539)) }
            match givenWhenThen given (Observe_reading_progress obsData) with
            | Ok events ->
                Expect.equal events [ Reading_progress_observed obsData ]
                    "the same-day observation is a new entry, not a no-op or an overwrite"
                match applyEvents (given @ events) with
                | Active book ->
                    Expect.equal (List.length book.Observations) 2 "two entries should exist"
                    let prior = book.Observations |> List.find (fun e -> e.Kind = Prior)
                    Expect.equal prior.Percent 14 "the prior's own percent is untouched"
                    Expect.equal prior.Position (Some (Minutes (75, Some 539))) "the prior's own position is untouched"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Two same-day observations with different minutes but the same whole percent both produce Reading_progress_observed" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            let first = { observation 50 Audible "2026-01-01" false with Position = Some (Minutes (300, Some 600)) }
            match givenWhenThen given (Observe_reading_progress first) with
            | Ok firstEvents ->
                Expect.equal firstEvents [
                    Reading_progress_observed first
                    Book_status_changed (BookStatus.InFocus, Some "2026-01-01")
                ] "first observation records and promotes"
                let afterFirst = given @ firstEvents
                let second = { observation 50 Audible "2026-01-01" false with Position = Some (Minutes (305, Some 600)) }
                match givenWhenThen afterFirst (Observe_reading_progress second) with
                | Ok secondEvents ->
                    Expect.equal secondEvents [ Reading_progress_observed second ]
                        "different minutes at the same whole percent is a new entry, not a no-op"
                | Error e -> failtest $"Expected success but got: {e}"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "An observation whose percent AND position both equal the source's latest entry is a no-op; an untouched daily sync appends nothing" <| fun _ ->
            let entryData = { observation 50 Audible "2026-01-01" false with Position = Some (Minutes (300, Some 600)) }
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed entryData
            ]
            let sameAgain = { entryData with ObservedOn = "2026-01-02" }
            match givenWhenThen given (Observe_reading_progress sameAgain) with
            | Ok events -> Expect.isEmpty events "same percent AND same position — a genuine no-op, an untouched library's daily sync"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress on a source that already has a prior never emits a second prior and never alters the first" <| fun _ ->
            let priorData = observation 30 Audible "2025-01-01" false
            let given = [
                Book_added_to_library sampleBookData
                Prior_reading_progress_recorded priorData
            ]
            let secondPriorAttempt = observation 45 Audible "2026-02-01" false
            match givenWhenThen given (Record_prior_reading_progress secondPriorAttempt) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed secondPriorAttempt
                    Book_status_changed (BookStatus.InFocus, Some "2026-02-01")
                ] "behaves exactly like Observe_reading_progress — never a second Prior_reading_progress_recorded"
                match applyEvents (given @ events) with
                | Active book ->
                    let priors = book.Observations |> List.filter (fun e -> e.Kind = Prior)
                    Expect.equal (List.length priors) 1 "still exactly one prior"
                    Expect.equal priors.[0].Percent 30 "the original prior is untouched"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing one entry by id among two same-source same-day entries leaves the other, and the per-source baseline falls back to it" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 20 Audible "2026-01-01" false)
                Reading_progress_observed (observation 35 Audible "2026-01-01" false)
            ]
            // `reconstituteEvents` numbers `given` 1-based by list position:
            // entry 2 = 20%%, entry 3 = 35%%.
            match givenWhenThen given (Remove_reading_progress_entry 3L) with
            | Ok events ->
                Expect.equal events [ Reading_progress_entry_removed 3L ] "should remove the entry named by id"
                match applyEvents (given @ events) with
                | Active book ->
                    Expect.equal (List.length book.Observations) 1 "one entry remains"
                    Expect.equal book.Observations.[0].Percent 20 "the remaining entry is the one that survived"
                | _ -> failtest "Expected Active state"
                match givenWhenThen (given @ events) (Observe_reading_progress (observation 20 Audible "2026-01-02" false)) with
                | Ok noOpEvents -> Expect.isEmpty noOpEvents "the remaining entry (20%%) is now the baseline — observing it again is a no-op"
                | Error e -> failtest $"Expected success but got: {e}"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "A higher observation from Backlog emits the observation and promotes to InFocus" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Observe_reading_progress (observation 20 Audible "2026-01-01" false)) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed (observation 20 Audible "2026-01-01" false)
                    Book_status_changed (BookStatus.InFocus, Some "2026-01-01")
                ] "Should observe and promote to InFocus"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "100 percent emits the observation and finishes the book" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Observe_reading_progress (observation 100 Audible "2026-01-01" false)) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed (observation 100 Audible "2026-01-01" false)
                    Book_status_changed (BookStatus.Finished, Some "2026-01-01")
                ] "Should observe and finish"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Finished = true at 60 percent finishes the book" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Observe_reading_progress (observation 60 Audible "2026-01-01" true)) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed (observation 60 Audible "2026-01-01" true)
                    Book_status_changed (BookStatus.Finished, Some "2026-01-01")
                ] "Should observe and finish via the Finished flag"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "A lower observation on a Finished book records only the observation and status stays Finished" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 100 Audible "2026-01-01" false)
                Book_status_changed (BookStatus.Finished, Some "2026-01-01")
            ]
            match givenWhenThen given (Observe_reading_progress (observation 80 Audible "2026-01-05" false)) with
            | Ok events ->
                Expect.equal events [ Reading_progress_observed (observation 80 Audible "2026-01-05" false) ]
                    "Should record only the observation"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.Status BookStatus.Finished "Status should stay Finished"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing the observation that finished a book leaves it Finished" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 100 Audible "2026-01-01" false)
                Book_status_changed (BookStatus.Finished, Some "2026-01-01")
            ]
            // books-wk67x: `reconstituteEvents` numbers `given` 1-based by
            // list position — the observation is entry 2.
            match givenWhenThen given (Remove_reading_progress_entry 2L) with
            | Ok events ->
                Expect.equal events [ Reading_progress_entry_removed 2L ] "Should remove the observation"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.Status BookStatus.Finished "Status should stay Finished (no auto-revert)"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Two sources at different percents on the same day each record independently without ping-ponging a status event" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Observe_reading_progress (observation 42 Audible "2026-01-01" false)) with
            | Error e -> failtest $"Expected success but got: {e}"
            | Ok firstEvents ->
                let afterFirst = given @ firstEvents
                match givenWhenThen afterFirst (Observe_reading_progress (observation 40 ProgressSource.Manual "2026-01-01" false)) with
                | Ok secondEvents ->
                    Expect.equal secondEvents [ Reading_progress_observed (observation 40 ProgressSource.Manual "2026-01-01" false) ]
                        "Second source's observation should record without re-promoting"
                | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress with zero entries for its source records a prior, never Ok []" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Record_prior_reading_progress (observation 0 Audible "2026-01-01" false)) with
            | Ok events ->
                Expect.equal events [ Prior_reading_progress_recorded (observation 0 Audible "2026-01-01" false) ]
                    "A first-ever 0%% prior should still be recorded"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress at 100 percent records the prior and finishes the book with its own date" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Record_prior_reading_progress (observation 100 Audible "2025-06-01" false)) with
            | Ok events ->
                Expect.equal events [
                    Prior_reading_progress_recorded (observation 100 Audible "2025-06-01" false)
                    Book_status_changed (BookStatus.Finished, Some "2025-06-01")
                ] "Should record the prior and finish"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress with the Finished flag also finishes the book" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Record_prior_reading_progress (observation 60 Audible "2025-06-01" true)) with
            | Ok events ->
                Expect.equal events [
                    Prior_reading_progress_recorded (observation 60 Audible "2025-06-01" true)
                    Book_status_changed (BookStatus.Finished, Some "2025-06-01")
                ] "Should record the prior and finish via the Finished flag"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress never promotes to InFocus, regardless of percent" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Record_prior_reading_progress (observation 42 Audible "2025-06-01" false)) with
            | Ok events ->
                Expect.equal events [ Prior_reading_progress_recorded (observation 42 Audible "2025-06-01" false) ]
                    "A prior at any non-finishing percent should record only itself, never InFocus"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.Status BookStatus.Backlog "Status should stay Backlog"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Record_prior_reading_progress on a source that already has an entry behaves exactly like Observe_reading_progress" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 20 Audible "2026-01-01" false)
            ]
            // Same percent as the existing entry: no-op, never a second prior.
            match givenWhenThen given (Record_prior_reading_progress (observation 20 Audible "2026-01-05" false)) with
            | Ok events -> Expect.isEmpty events "Same-percent should be a no-op, like Observe_reading_progress"
            | Error e -> failtest $"Expected success but got: {e}"
            // A raise: ordinary observation + InFocus promotion, never a prior.
            match givenWhenThen given (Record_prior_reading_progress (observation 55 Audible "2026-01-05" false)) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed (observation 55 Audible "2026-01-05" false)
                    Book_status_changed (BookStatus.InFocus, Some "2026-01-05")
                ] "A raise should behave exactly like Observe_reading_progress, never emitting a second prior"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Observe_reading_progress with zero entries for its source emits an ordinary observation and promotes, never a prior" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Observe_reading_progress (observation 15 ProgressSource.Manual "2026-01-01" false)) with
            | Ok events ->
                Expect.equal events [
                    Reading_progress_observed (observation 15 ProgressSource.Manual "2026-01-01" false)
                    Book_status_changed (BookStatus.InFocus, Some "2026-01-01")
                ] "A raise from the 0 baseline should observe and promote, never emit a prior"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Removing a prior empties that source's entries, so the next prior is fresh and the next observation is ordinary" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Prior_reading_progress_recorded (observation 30 Audible "2025-01-01" false)
            ]
            // books-wk67x: the prior is entry 2 (1-based list position).
            match givenWhenThen given (Remove_reading_progress_entry 2L) with
            | Ok events ->
                Expect.equal events [ Reading_progress_entry_removed 2L ] "Should remove the prior"
                let afterRemoval = given @ events
                match givenWhenThen afterRemoval (Record_prior_reading_progress (observation 10 Audible "2026-02-01" false)) with
                | Ok priorEvents ->
                    Expect.equal priorEvents [ Prior_reading_progress_recorded (observation 10 Audible "2026-02-01" false) ]
                        "A following Record_prior_reading_progress should record a fresh prior"
                | Error e -> failtest $"Expected success but got: {e}"
                match givenWhenThen afterRemoval (Observe_reading_progress (observation 10 Audible "2026-02-01" false)) with
                | Ok obsEvents ->
                    Expect.equal obsEvents [
                        Reading_progress_observed (observation 10 Audible "2026-02-01" false)
                        Book_status_changed (BookStatus.InFocus, Some "2026-02-01")
                    ] "A following Observe_reading_progress should record an ordinary observation (and promote, from the emptied baseline)"
                | Error e -> failtest $"Expected success but got: {e}"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Re-linking a different ASIN is refused; re-linking the same ASIN is a no-op" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Link_external_id (AudibleAsin "B99DIFFERENT")) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for a different ASIN"
            match givenWhenThen given (Link_external_id (AudibleAsin "B08GB43BXN")) with
            | Ok events -> Expect.isEmpty events "Re-linking the same value should be a no-op"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Change_status(Finished, Some date) on a Backlog book emits one event" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Change_status (BookStatus.Finished, Some "2026-09-02")) with
            | Ok events ->
                Expect.equal events [ Book_status_changed (BookStatus.Finished, Some "2026-09-02") ] "Should emit one status change"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.FinishedOn (Some "2026-09-02") "FinishedOn should be set"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Repeating the identical Change_status command appends zero events" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Book_status_changed (BookStatus.Finished, Some "2026-09-02")
            ]
            match givenWhenThen given (Change_status (BookStatus.Finished, Some "2026-09-02")) with
            | Ok events -> Expect.isEmpty events "Identical repeat should be a no-op"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Repeating Change_status with a different date appends exactly one event and updates the date" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Book_status_changed (BookStatus.Finished, Some "2026-09-02")
            ]
            match givenWhenThen given (Change_status (BookStatus.Finished, Some "2026-08-15")) with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should append exactly one event"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.FinishedOn (Some "2026-08-15") "FinishedOn should update to the new date"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "A book Finished by an observation today, then re-dated by a manual Change_status, converges to the past date after exactly one more event" <| fun _ ->
            let given = [
                Book_added_to_library sampleBookData
                Reading_progress_observed (observation 100 Audible "2026-09-16" false)
                Book_status_changed (BookStatus.Finished, Some "2026-09-16")
            ]
            match givenWhenThen given (Change_status (BookStatus.Finished, Some "2026-03-01")) with
            | Ok events ->
                Expect.equal (List.length events) 1 "Should append exactly one more event"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.FinishedOn (Some "2026-03-01") "FinishedOn should converge to the past date"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Change_status(Finished, None) is not a no-op from Backlog" <| fun _ ->
            let given = [ Book_added_to_library sampleBookData ]
            match givenWhenThen given (Change_status (BookStatus.Finished, None)) with
            | Ok events ->
                Expect.equal events [ Book_status_changed (BookStatus.Finished, None) ] "Should emit the status change"
                match applyEvents (given @ events) with
                | Active book -> Expect.equal book.FinishedOn None "Aggregate carries the raw None — the projection defaults the date"
                | _ -> failtest "Expected Active state"
            | Error e -> failtest $"Expected success but got: {e}"

        testCase "Round-trip serialization for every BookEvent case" <| fun _ ->
            let events = [
                Book_added_to_library sampleBookData
                Book_removed_from_library
                Book_cover_replaced "posters/book-project-hail-mary.jpg"
                Book_external_id_linked (Isbn13 "9780593135204")
                Book_format_set Print
                Book_status_changed (BookStatus.Finished, Some "2026-09-02")
                Book_status_changed (BookStatus.Backlog, None)
                Reading_progress_observed (observation 42 Audible "2026-01-01" false)
                Reading_progress_observed { observation 42 ProgressSource.Manual "2026-01-02" false with Position = Some (Page (120, Some 300)) }
                Prior_reading_progress_recorded (observation 30 Audible "2025-01-01" false)
                Reading_progress_observation_removed ("2026-01-01", Audible)
                Reading_progress_entry_removed 42L
                Book_personal_rating_set (Some 4)
                Book_personal_rating_set None
                Book_recommended_by "marco"
                Book_recommendation_removed "marco"
            ]
            for event in events do
                let eventType, data = Serialization.serialize event
                match Serialization.deserialize eventType data with
                | Some roundTripped -> Expect.equal roundTripped event $"Round-trip should preserve {eventType}"
                | None -> failtest $"Failed to deserialize {eventType}"

        testCase "handledEventTypes lists every BookEvent case" <| fun _ ->
            let allTypes = [
                "Book_added_to_library"
                "Book_removed_from_library"
                "Book_cover_replaced"
                "Book_external_id_linked"
                "Book_format_set"
                "Book_status_changed"
                "Reading_progress_observed"
                "Prior_reading_progress_recorded"
                "Reading_progress_observation_removed"
                "Reading_progress_entry_removed"
                "Book_personal_rating_set"
                "Book_recommended_by"
                "Book_recommendation_removed"
            ]
            for t in allTypes do
                Expect.contains Serialization.handledEventTypes t $"handledEventTypes should list {t}"
    ]
