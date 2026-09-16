module Mediatheca.Tests.BookProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// books-y9kxy: `BookProjection`'s schema, `handleEvent`, and the
/// query-time cache join, mirroring the "ProjectionRebuildTests/
/// ProjectionDriftTests style rebuild yields identical rows" acceptance
/// criterion via a dedicated one-projection drift check.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    FriendProjection.handler.Init conn
    NotesProjection.handler.Init conn
    MetadataCache.initialize conn
    BookProjection.handler.Init conn
    conn

let private sampleBookData: Books.BookAddedData = {
    Title = "Project Hail Mary"
    Authors = [ "Andy Weir" ]
    Year = Some 2021
    CoverRef = None
    Subjects = [ "Science Fiction" ]
    Format = Audiobook
    ExternalIds = [ AudibleAsin "B08GB43BXN" ]
}

let private appendBookEvent (conn: SqliteConnection) (slug: string) (event: Books.BookEvent) =
    let streamId = Books.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Books.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn BookProjection.handler

let private observation (percent: int) (source: ProgressSource) (observedOn: string) : Books.ReadingProgressObservedData =
    { Percent = percent; Position = None; Source = source; ObservedOn = observedOn; Finished = false }

let private latestBookStatusChangedTimestamp (conn: SqliteConnection) (slug: string) : string =
    conn
    |> Db.newCommand "SELECT timestamp FROM events WHERE stream_id = @stream_id AND event_type = 'Book_status_changed' ORDER BY stream_position DESC LIMIT 1"
    |> Db.setParams [ "stream_id", SqlType.String (Books.streamId slug) ]
    |> Db.querySingle (fun rd -> rd.ReadString "timestamp")
    |> Option.get

[<Tests>]
let bookProjectionTests =
    testList "BookProjection" [

        testCase "Book_added_to_library projects into book_list and book_detail" <| fun _ ->
            use conn = createConnection ()
            appendBookEvent conn "project-hail-mary-2021" (Books.Book_added_to_library sampleBookData)

            let listed = BookProjection.getAll conn
            Expect.equal (List.length listed) 1 "One book should be listed"
            Expect.equal listed.[0].Title "Project Hail Mary" "Title should match"
            Expect.equal listed.[0].Status BookStatus.Backlog "Status should default to Backlog"

            match BookProjection.getBySlug conn "project-hail-mary-2021" with
            | Some detail ->
                Expect.equal detail.AudibleAsin (Some "B08GB43BXN") "AudibleAsin should be projected"
                Expect.equal detail.ProgressPercent 0 "ProgressPercent should default to 0"
                Expect.isEmpty detail.ProgressHistory "No observations yet"
            | None -> failtest "Expected the book to be found"

        testCase "Two same-day same-source observations leave one row with the later percent" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 35 Audible "2026-01-01"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 1 "Same-day same-source observations should collapse to one row"
            Expect.equal history.[0].Percent 35 "The later percent should win"

            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.ProgressPercent 35 "book_detail's denormalized percent should reflect the collapsed row"
            | None -> failtest "Expected the book to be found"

        testCase "Same-day observations from two sources leave two rows; Manual wins the tie" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 42 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 40 Goodreads "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 50 ProgressSource.Manual "2026-01-01"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 3 "Three distinct (day, source) rows should exist"

            match BookProjection.getBySlug conn slug with
            | Some detail ->
                Expect.equal detail.ProgressPercent 50 "Manual should win the same-day tie"
                Expect.equal detail.ProgressSource (Some ProgressSource.Manual) "Manual should be the winning source"
            | None -> failtest "Expected the book to be found"

        testCase "finished_at is a date string, set from effectiveOn, and cleared on leaving Finished" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some "2026-03-01"))

            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.FinishedAt (Some "2026-03-01") "finished_at should be the given effectiveOn date"
            | None -> failtest "Expected the book to be found"

            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Backlog, None))
            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.FinishedAt None "finished_at should clear on leaving Finished"
            | None -> failtest "Expected the book to be found"

        testCase "Book_status_changed (Finished, None) sets finished_at to the event's own local date" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, None))

            let eventTimestamp = latestBookStatusChangedTimestamp conn slug
            let expectedDate = System.DateTimeOffset.Parse(eventTimestamp).ToString("yyyy-MM-dd")
            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.FinishedAt (Some expectedDate) "finished_at should default to the event's own local date when effectiveOn is None"
            | None -> failtest "Expected the book to be found"

        testCase "Removing an observation deletes its row and recomputes the denormalized progress" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observation_removed ("2026-01-01", Audible))

            Expect.isEmpty (BookProjection.getProgressHistory conn slug) "The row should be gone"
            match BookProjection.getBySlug conn slug with
            | Some detail ->
                Expect.equal detail.ProgressPercent 0 "Progress should reset once no observations remain"
                Expect.equal detail.ProgressSource None "Progress source should clear"
            | None -> failtest "Expected the book to be found"

        testCase "Book_removed_from_library deletes book_list, book_detail and book_progress rows" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent conn slug Books.Book_removed_from_library

            Expect.isEmpty (BookProjection.getAll conn) "book_list should be empty"
            Expect.equal (BookProjection.getBySlug conn slug) None "book_detail should be gone"
            Expect.isEmpty (BookProjection.getProgressHistory conn slug) "book_progress rows should be gone"

        testCase "A rebuild from a stream with add + 3 observations + finish yields identical rows (drift zero)" <| fun _ ->
            use liveConn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent liveConn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent liveConn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent liveConn slug (Books.Reading_progress_observed (observation 55 Audible "2026-01-05"))
            appendBookEvent liveConn slug (Books.Reading_progress_observed (observation 100 Audible "2026-01-09"))
            appendBookEvent liveConn slug (Books.Book_status_changed (BookStatus.Finished, Some "2026-01-09"))

            use shadowConn = new SqliteConnection("Data Source=:memory:")
            shadowConn.Open()

            let drift = Administration.checkProjectionDrift liveConn shadowConn [ BookProjection.handler ] (fun _ -> ())
            let discrepancies = drift |> List.collect (fun p -> p.Discrepancies)
            Expect.isEmpty discrepancies "A shadow replay of BookProjection should match the live tables exactly"
    ]
