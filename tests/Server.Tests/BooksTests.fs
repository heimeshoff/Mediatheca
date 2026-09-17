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

let private givenWhenThen (given: BookEvent list) (command: BookCommand) =
    let state = reconstitute given
    decide state command

let private applyEvents (events: BookEvent list) = reconstitute events

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
                    Expect.isTrue (Map.isEmpty book.Observations) "Observations should be empty"
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
            match givenWhenThen given (Remove_reading_progress_observation ("2026-01-01", Audible)) with
            | Ok events ->
                Expect.equal events [ Reading_progress_observation_removed ("2026-01-01", Audible) ] "Should remove the observation"
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
                Reading_progress_observation_removed ("2026-01-01", Audible)
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
                "Reading_progress_observation_removed"
                "Book_personal_rating_set"
                "Book_recommended_by"
                "Book_recommendation_removed"
            ]
            for t in allTypes do
                Expect.contains Serialization.handledEventTypes t $"handledEventTypes should list {t}"
    ]
