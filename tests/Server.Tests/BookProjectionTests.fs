module Mediatheca.Tests.BookProjectionTests

open System
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

let private bookMetadataWithRuntime (minutes: int) : MetadataCache.BookMetadata =
    { Description = None
      PageCount = None
      RuntimeMinutes = Some minutes
      Narrators = []
      SeriesName = None
      SeriesPosition = None
      Publisher = None
      PublishedDate = None
      AverageRating = None
      Language = None
      Source = None }

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

        // books-wk67x (amending ADR-0076 §2): same-day same-source
        // observations no longer collapse — each is its own append-only
        // row, and the denormalized percent follows whichever was
        // recorded LAST (append order), not a source-priority tie-break.
        testCase "Two same-day same-source observations leave two rows; the denormalized percent follows the one recorded last" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 35 Audible "2026-01-01"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 2 "Same-day same-source observations should both be kept as history entries"
            Expect.equal (history |> List.map (fun r -> r.Percent)) [ 20; 35 ] "Both percents survive, oldest first"
            Expect.isFalse (history.[0].EntryId = history.[1].EntryId) "the two entries have distinct ids"

            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.ProgressPercent 35 "book_detail's denormalized percent should follow the entry recorded last"
            | None -> failtest "Expected the book to be found"

        testCase "Same-day observations from two sources leave two rows; the one recorded last wins the denormalized percent" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 42 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 50 ProgressSource.Manual "2026-01-01"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 2 "Two distinct rows should exist"

            match BookProjection.getBySlug conn slug with
            | Some detail ->
                // books-wk67x: the old Manual > Audible tie-break is gone —
                // Manual wins here only because it was appended SECOND
                // (later entry_id), not because of its source.
                Expect.equal detail.ProgressPercent 50 "The entry recorded last should win"
                Expect.equal detail.ProgressSource (Some ProgressSource.Manual) "Manual, recorded last, should be the winning source"
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

        testCase "Reading_progress_observed rows carry kind = 'observation'; Prior_reading_progress_recorded rows carry kind = 'prior'" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Prior_reading_progress_recorded (observation 60 Audible "2025-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 75 Audible "2026-01-05"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 2 "Both rows should be present"
            let prior = history |> List.find (fun r -> r.ObservedOn = "2025-01-01")
            let observed = history |> List.find (fun r -> r.ObservedOn = "2026-01-05")
            Expect.equal prior.Kind Prior "The prior row should carry Kind = Prior"
            Expect.equal observed.Kind Observed "The observation row should carry Kind = Observed"

            match BookProjection.getBySlug conn slug with
            | Some detail ->
                Expect.equal detail.ProgressPercent 75 "book_detail should reflect the latest (observation) row"
                let priorViaDetail = detail.ProgressHistory |> List.find (fun r -> r.ObservedOn = "2025-01-01")
                let observedViaDetail = detail.ProgressHistory |> List.find (fun r -> r.ObservedOn = "2026-01-05")
                Expect.equal priorViaDetail.Kind Prior "ReadingProgressDto.Kind should round-trip through getBySlug for the prior row"
                Expect.equal observedViaDetail.Kind Observed "ReadingProgressDto.Kind should round-trip through getBySlug for the observation row"
            | None -> failtest "Expected the book to be found"

        testCase "book_list.progress_kind follows the latest row, including back to 'prior' after the observation is removed" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Prior_reading_progress_recorded (observation 60 Audible "2025-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 75 Audible "2026-01-05"))
            appendBookEvent conn slug (Books.Reading_progress_observation_removed ("2026-01-05", Audible))

            let latestKind =
                conn
                |> Db.newCommand "SELECT progress_kind FROM book_list WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun rd -> rd.ReadString "progress_kind")
            Expect.equal latestKind (Some "prior") "book_list.progress_kind should fall back to the remaining prior row"

        // books-wk67x (amending ADR-0076 §2): the exact incident this task
        // exists to fix — a same-day, same-source observation must never
        // overwrite a prior.
        testCase "A prior plus a same-day same-source observation leaves two rows; the prior keeps its own percent/position/date and book_detail follows the later entry" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            let priorData : Books.ReadingProgressObservedData =
                { Percent = 14; Position = Some (Minutes (75, Some 539)); Source = Audible; ObservedOn = "2026-09-18"; Finished = false }
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Prior_reading_progress_recorded priorData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-09-18"))

            let history = BookProjection.getProgressHistory conn slug
            Expect.equal (List.length history) 2 "the prior and the same-day observation both survive as separate rows"
            let prior = history |> List.find (fun r -> r.Kind = Prior)
            let observed = history |> List.find (fun r -> r.Kind = Observed)
            Expect.equal prior.Percent 14 "the prior's own percent is untouched"
            Expect.equal prior.Position (Some (Minutes (75, Some 539))) "the prior's own position is untouched"
            Expect.equal prior.ObservedOn "2026-09-18" "the prior's own date is untouched"
            Expect.equal observed.Percent 20 "the new observation's own percent"

            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.ProgressPercent 20 "book_detail.progress_percent should reflect the later (observation) entry, not the prior"
            | None -> failtest "Expected the book to be found"

        testCase "Rebuilding from a log holding only legacy Reading_progress_observed events produces zero kind = 'prior' rows" <| fun _ ->
            use liveConn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent liveConn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent liveConn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent liveConn slug (Books.Reading_progress_observed (observation 55 Audible "2026-01-05"))

            let priorRowCount =
                liveConn
                |> Db.newCommand "SELECT COUNT(*) as cnt FROM book_progress WHERE kind = 'prior'"
                |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                |> Option.defaultValue -1
            Expect.equal priorRowCount 0 "No upcast of historical Reading_progress_observed events into priors"

        // books-wk67x, point 6: the legacy `Reading_progress_observation_removed
        // (observedOn, source)` event keeps its ORIGINAL replay meaning —
        // deletes every entry of that day/source that exists at that point
        // in the stream. Before this task, at most one row could ever match
        // (same-day/source always collapsed), so the removal always left
        // zero rows for that book; after this task, the SAME event, replayed
        // in a FULL REBUILD, still leaves zero rows for this exact
        // single-observation-then-remove shape — the surviving-row count a
        // pre-books-wk67x database would have produced.
        testCase "A full rebuild over a stream containing a legacy Reading_progress_observation_removed event leaves the same surviving rows it produced before this task" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 20 Audible "2026-01-01"))
            appendBookEvent conn slug (Books.Reading_progress_observation_removed ("2026-01-01", Audible))

            Expect.isEmpty (BookProjection.getProgressHistory conn slug) "the single row is gone after a live catch-up"

            Projection.rebuildProjection conn BookProjection.handler
            Expect.isEmpty (BookProjection.getProgressHistory conn slug) "a full rebuild replays the legacy removal identically — still zero surviving rows"
            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.ProgressPercent 0 "the denormalized percent clears, exactly as it did before this task"
            | None -> failtest "Expected the book to be found"

        testCase "getReadingStats().HoursListenedThisYear excludes a book whose latest Audible row is kind = 'prior'" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            let today = DateTime.UtcNow.ToString("yyyy-MM-dd")
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Prior_reading_progress_recorded (observation 100 Audible today))
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some today))
            MetadataCache.upsertBookMetadata conn slug (bookMetadataWithRuntime 600)

            let stats = BookProjection.getReadingStats conn
            Expect.equal stats.HoursListenedThisYear None
                "A prior-only latest Audible row should never count as hours listened this year"

        testCase "getReadingStats().HoursListenedThisYear counts a book whose latest Audible row is an ordinary observation" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            let today = DateTime.UtcNow.ToString("yyyy-MM-dd")
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 100 Audible today))
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some today))
            MetadataCache.upsertBookMetadata conn slug (bookMetadataWithRuntime 600)

            let stats = BookProjection.getReadingStats conn
            Expect.equal stats.HoursListenedThisYear (Some 10.0)
                "A book finished via an ordinary observation should count its full runtime"

        // books-wk67x, point 8: several same-day entries must count as ONE
        // book's runtime, not one per entry — `HoursListenedThisYear` reads
        // `book_list`'s single denormalized `progress_percent`, so several
        // same-day Audible rows before the finish can never inflate it.
        testCase "getReadingStats().HoursListenedThisYear is not inflated by several same-day entries — the book counts once" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            let today = DateTime.UtcNow.ToString("yyyy-MM-dd")
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 40 Audible today))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 70 Audible today))
            appendBookEvent conn slug (Books.Reading_progress_observed (observation 100 Audible today))
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some today))
            MetadataCache.upsertBookMetadata conn slug (bookMetadataWithRuntime 600)

            Expect.equal (List.length (BookProjection.getProgressHistory conn slug)) 3 "sanity: three separate same-day entries exist"
            let stats = BookProjection.getReadingStats conn
            Expect.equal stats.HoursListenedThisYear (Some 10.0)
                "The book's runtime counts once, at the LATEST entry's percent (100%%), never summed across same-day entries"

        // books-wk67x: `PagesReadThisYear`'s correlated subquery must name
        // book_progress's own LATEST row per book (observed_on, then
        // entry_id) — a plain join on (observed_on, source) would now fan
        // out across every same-day entry a finished book carries and
        // double-count its pages.
        testCase "getReadingStats().PagesReadThisYear is not inflated by several same-day entries for the same finished book" <| fun _ ->
            use conn = createConnection ()
            let slug = "project-hail-mary-2021"
            let today = DateTime.UtcNow.ToString("yyyy-MM-dd")
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)
            let observeAtPage (page: int) =
                Books.Reading_progress_observed { Percent = page * 100 / 248; Position = Some (Page (page, Some 248)); Source = Audible; ObservedOn = today; Finished = false }
            appendBookEvent conn slug (observeAtPage 100)
            appendBookEvent conn slug (observeAtPage 200)
            appendBookEvent conn slug (observeAtPage 248)
            appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some today))

            Expect.equal (List.length (BookProjection.getProgressHistory conn slug)) 3 "sanity: three separate same-day entries exist"
            let stats = BookProjection.getReadingStats conn
            Expect.equal stats.PagesReadThisYear (Some 248)
                "Pages count once, at the book's own latest entry — never summed across same-day entries"

        /// books-d4wtc iteration 2 (verifier fix): a real, already-populated
        /// database predates this task's `progress_kind`/`kind` columns.
        /// `ALTER TABLE ... ADD COLUMN progress_kind TEXT` (no DEFAULT) adds
        /// it as NULL for every pre-existing `book_list`/`book_detail` row,
        /// so this test pre-creates the pre-task schema directly (mirroring
        /// the legacy-goodreads-column test above), inserts a row the way
        /// it would have existed before this task, then runs `handler.Init`
        /// so the ALTER (and the new backfill) actually fire against
        /// pre-existing data -- proving a migrated row is neither NULL
        /// forever nor silently dropped from `HoursListenedThisYear`.
        testCase "Init against a pre-migration book_list/book_detail/book_progress schema backfills progress_kind and keeps counting hours" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            FriendProjection.handler.Init conn
            NotesProjection.handler.Init conn
            MetadataCache.initialize conn

            let slug = "project-hail-mary-2021"
            let today = DateTime.UtcNow.ToString("yyyy-MM-dd")

            // The pre-task shape: no `progress_kind` on book_list/book_detail,
            // no `kind` on book_progress.
            use cmd = conn.CreateCommand()
            cmd.CommandText <- """
                CREATE TABLE book_list (
                    slug                    TEXT PRIMARY KEY,
                    title                   TEXT NOT NULL,
                    authors                 TEXT NOT NULL DEFAULT '[]',
                    year                    INTEGER,
                    cover_ref               TEXT,
                    subjects                TEXT NOT NULL DEFAULT '[]',
                    format                  TEXT NOT NULL DEFAULT 'Unknown',
                    status                  TEXT NOT NULL DEFAULT 'Backlog',
                    progress_percent        INTEGER NOT NULL DEFAULT 0,
                    progress_source         TEXT,
                    progress_observed_on    TEXT,
                    personal_rating         INTEGER,
                    isbn13                  TEXT,
                    openlibrary_work_key    TEXT,
                    openlibrary_edition_key TEXT,
                    audible_asin            TEXT,
                    finished_at             TEXT,
                    added_at                TEXT
                );

                CREATE TABLE book_detail (
                    slug                    TEXT PRIMARY KEY,
                    title                   TEXT NOT NULL,
                    authors                 TEXT NOT NULL DEFAULT '[]',
                    year                    INTEGER,
                    cover_ref               TEXT,
                    subjects                TEXT NOT NULL DEFAULT '[]',
                    format                  TEXT NOT NULL DEFAULT 'Unknown',
                    status                  TEXT NOT NULL DEFAULT 'Backlog',
                    progress_percent        INTEGER NOT NULL DEFAULT 0,
                    progress_source         TEXT,
                    progress_observed_on    TEXT,
                    personal_rating         INTEGER,
                    isbn13                  TEXT,
                    openlibrary_work_key    TEXT,
                    openlibrary_edition_key TEXT,
                    audible_asin            TEXT,
                    finished_at             TEXT,
                    added_at                TEXT,
                    recommended_by          TEXT NOT NULL DEFAULT '[]'
                );

                CREATE TABLE book_progress (
                    book_slug    TEXT NOT NULL,
                    observed_on  TEXT NOT NULL,
                    source       TEXT NOT NULL,
                    percent      INTEGER NOT NULL,
                    position_json TEXT,
                    PRIMARY KEY (book_slug, observed_on, source)
                );
            """
            cmd.ExecuteNonQuery() |> ignore

            conn
            |> Db.newCommand "INSERT INTO book_list (slug, title, status, progress_percent, progress_source, progress_observed_on, finished_at, added_at) VALUES (@slug, @title, 'Finished', 100, 'Audible', @observed_on, @finished_at, @added_at)"
            |> Db.setParams [ "slug", SqlType.String slug; "title", SqlType.String "Project Hail Mary"; "observed_on", SqlType.String today; "finished_at", SqlType.String today; "added_at", SqlType.String today ]
            |> Db.exec
            conn
            |> Db.newCommand "INSERT INTO book_detail (slug, title, status, progress_percent, progress_source, progress_observed_on, finished_at, added_at) VALUES (@slug, @title, 'Finished', 100, 'Audible', @observed_on, @finished_at, @added_at)"
            |> Db.setParams [ "slug", SqlType.String slug; "title", SqlType.String "Project Hail Mary"; "observed_on", SqlType.String today; "finished_at", SqlType.String today; "added_at", SqlType.String today ]
            |> Db.exec
            conn
            |> Db.newCommand "INSERT INTO book_progress (book_slug, observed_on, source, percent, position_json) VALUES (@slug, @observed_on, 'Audible', 100, NULL)"
            |> Db.setParams [ "slug", SqlType.String slug; "observed_on", SqlType.String today ]
            |> Db.exec

            MetadataCache.upsertBookMetadata conn slug (bookMetadataWithRuntime 600)

            // Should not throw, and must retrofit the pre-existing rows --
            // not merely add the columns and leave them NULL.
            BookProjection.handler.Init conn

            let listKind =
                conn
                |> Db.newCommand "SELECT progress_kind FROM book_list WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("progress_kind")) then None else Some (rd.ReadString "progress_kind"))
                |> Option.flatten
            Expect.equal listKind (Some "observation")
                "A pre-existing book_list row's progress_kind should be backfilled from its book_progress history, not left NULL"

            let stats = BookProjection.getReadingStats conn
            Expect.equal stats.HoursListenedThisYear (Some 10.0)
                "A migrated (pre-existing) Audible observation should still count toward hours listened this year"

            conn.Dispose()

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

        /// integration-sfmxg: an existing projection DB may still carry the
        /// now-orphan, nullable `book_detail.goodreads_book_id` column from
        /// before the Goodreads integration was removed. `handler.Init`
        /// (`CREATE TABLE IF NOT EXISTS`) never touches an already-existing
        /// table's columns, and this handler's own INSERTs no longer name
        /// that column -- so the column just sits there, unread and
        /// unwritten, while projection init and a normal `Book_added_to_library`
        /// both succeed exactly as they would against a column-free schema.
        testCase "Init against a book_detail table with the legacy goodreads_book_id column still projects Book_added" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            FriendProjection.handler.Init conn
            NotesProjection.handler.Init conn
            MetadataCache.initialize conn

            // The full pre-removal `book_detail` shape, PLUS the orphan
            // `goodreads_book_id` column current code never selects or
            // writes -- `book_list` is left to the current handler's own
            // `CREATE TABLE IF NOT EXISTS` (it never carried that column).
            use cmd = conn.CreateCommand()
            cmd.CommandText <- """
                CREATE TABLE book_detail (
                    slug                    TEXT PRIMARY KEY,
                    title                   TEXT NOT NULL,
                    authors                 TEXT NOT NULL DEFAULT '[]',
                    year                    INTEGER,
                    cover_ref               TEXT,
                    subjects                TEXT NOT NULL DEFAULT '[]',
                    format                  TEXT NOT NULL DEFAULT 'Unknown',
                    status                  TEXT NOT NULL DEFAULT 'Backlog',
                    progress_percent        INTEGER NOT NULL DEFAULT 0,
                    progress_source         TEXT,
                    progress_observed_on    TEXT,
                    personal_rating         INTEGER,
                    isbn13                  TEXT,
                    openlibrary_work_key    TEXT,
                    openlibrary_edition_key TEXT,
                    audible_asin            TEXT,
                    goodreads_book_id       TEXT,
                    finished_at             TEXT,
                    added_at                TEXT,
                    recommended_by          TEXT NOT NULL DEFAULT '[]'
                );
            """
            cmd.ExecuteNonQuery() |> ignore

            // Should not throw -- CREATE TABLE IF NOT EXISTS is a no-op
            // against the pre-existing (legacy-shaped) table.
            BookProjection.handler.Init conn

            let slug = "project-hail-mary-2021"
            appendBookEvent conn slug (Books.Book_added_to_library sampleBookData)

            match BookProjection.getBySlug conn slug with
            | Some detail -> Expect.equal detail.Title "Project Hail Mary" "Book_added_to_library should still project despite the orphan column"
            | None -> failtest "Expected the book to be found"

            conn.Dispose()
    ]
