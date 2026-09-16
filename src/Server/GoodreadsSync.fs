namespace Mediatheca.Server

open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Mediatheca.Shared

/// The Goodreads shelf sync (integration-wmqn3, ADR-0075/ADR-0076): folds
/// the three shelf feeds into the library. Compiled BEFORE `Api.fs`
/// (`Server.fsproj`), so -- same as `PlaytimeTracker.fs`/`GameFacetBackfill.fs`
/// above it -- this module carries its own local command-execution and
/// slug-generation helpers rather than reaching into `Api.fs`'s private ones.
module GoodreadsSync =

    /// All three shelves are always fetched, so an existing library book's
    /// status/rating/links stay current from `read`/`to-read` even when the
    /// user hasn't opted those shelves into *importing new* books --
    /// `importShelves` (default `["currently-reading"]`) gates ONLY whether
    /// an unmatched item on that shelf gets created (the acceptance
    /// criterion's "shelf not configured for import" `Skipped` case).
    let private allShelves = [ "currently-reading"; "read"; "to-read" ]

    /// ADR-0028/administration-tj8n2: acquired only around a brief DB
    /// moment, never across an awaited HTTP call -- the same discipline
    /// `PlaytimeTracker.withLock`/`GameFacetBackfill.withLock` establish.
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    // ── Local command execution (same pattern as PlaytimeTracker's
    // `executeGameCommandWithEvents` -- needed because Api.fs compiles later) ──

    let private executeBookCommand
        (conn: SqliteConnection)
        (slug: string)
        (command: Books.BookCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<Books.BookEvent list, string> =
        let streamId = Books.streamId slug
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose Books.Serialization.fromStoredEvent
        let state = Books.reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId
        match Books.decide state command with
        | Error e -> Error e
        | Ok [] -> Ok []
        | Ok newEvents ->
            let eventDataList = newEvents |> List.map Books.Serialization.toEventData
            match EventStore.appendToStream conn streamId currentPosition eventDataList with
            | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
            | EventStore.Success _ ->
                for handler in projectionHandlers do
                    Projection.runProjection conn handler
                Ok newEvents

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    // ── ISBN-10 -> ISBN-13 (for matching only; OpenLibrary lookups pass the
    // feed's own isbn10 value through unconverted, since /isbn/{isbn}.json
    // accepts either form) ──

    let isbn10ToIsbn13 (isbn10: string) : string option =
        let digits = isbn10.Trim()
        if digits.Length <> 10 || not (digits.Substring(0, 9) |> Seq.forall System.Char.IsDigit) then
            None
        else
            let core = "978" + digits.Substring(0, 9)
            let sum =
                core
                |> Seq.mapi (fun i c -> (if i % 2 = 0 then 1 else 3) * (int c - int '0'))
                |> Seq.sum
            let check = (10 - (sum % 10)) % 10
            Some (core + string check)

    // ── Matching an item to an existing book (ADR-0075 §3: GoodreadsBookId,
    // then Isbn13, then Isbn10-converted-to-13) ──

    let private resolveExistingSlug (conn: SqliteConnection) (item: Goodreads.GoodreadsShelfItem) : string option =
        match BookProjection.findByExternalId conn (GoodreadsBookId item.BookId) with
        | Some slug -> Some slug
        | None ->
            let byIsbn13 =
                item.Isbn13 |> Option.bind (fun isbn13 -> BookProjection.findByExternalId conn (Isbn13 isbn13))
            match byIsbn13 with
            | Some slug -> Some slug
            | None ->
                item.Isbn
                |> Option.bind isbn10ToIsbn13
                |> Option.bind (fun converted -> BookProjection.findByExternalId conn (Isbn13 converted))

    /// Links the Goodreads id (and Isbn13, when known and not yet linked) to
    /// an already-resolved book. Returns whether any event was actually
    /// appended (a book already correctly linked is not counted).
    let private linkFoundBook
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        (item: Goodreads.GoodreadsShelfItem)
        : bool =
        match BookProjection.getBySlug conn slug with
        | None -> false
        | Some book ->
            let mutable linked = false
            if book.GoodreadsBookId <> Some item.BookId then
                match executeBookCommand conn slug (Books.Link_external_id (GoodreadsBookId item.BookId)) projectionHandlers with
                | Ok events when not (List.isEmpty events) -> linked <- true
                | _ -> ()
            match item.Isbn13, book.Isbn13 with
            | Some isbn13, None ->
                match executeBookCommand conn slug (Books.Link_external_id (Isbn13 isbn13)) projectionHandlers with
                | Ok events when not (List.isEmpty events) -> linked <- true
                | _ -> ()
            | _ -> ()
            linked

    /// `user_read_at` is RFC-822-shaped ("Tue, 02 Sep 2026 00:00:00 -0800").
    /// The leading day-of-week token is dropped before parsing rather than
    /// trusted: `DateTimeOffset.TryParse` rejects the whole string outright
    /// when that token doesn't match the date it precedes (verified against
    /// .NET's own parser), and this adapter has no way to guarantee
    /// Goodreads' generated feed always gets that token right -- only the
    /// day/month/year/offset actually matter for `finished_at`.
    let private parseReadAtDate (s: string) : string option =
        let withoutDayName =
            match s.IndexOf(',') with
            | -1 -> s
            | idx -> s.Substring(idx + 1).Trim()
        match System.DateTimeOffset.TryParse(withoutDayName, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None) with
        | true, dto -> Some (dto.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
        | false, _ -> None

    /// ADR-0075 §3: the adapter, not the aggregate, enforces "never demote" --
    /// `Books.Change_status` is a manual override that obeys whatever it's
    /// told, so each shelf's own guard decides whether to call it at all.
    /// Returns whether a real status-change event was appended.
    let private applyStatus
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        (shelf: string)
        (item: Goodreads.GoodreadsShelfItem)
        (book: BookDetail)
        : bool =
        match shelf with
        | "to-read" ->
            if book.Status = BookStatus.Backlog then
                match executeBookCommand conn slug (Books.Change_status (BookStatus.Backlog, None)) projectionHandlers with
                | Ok events -> not (List.isEmpty events)
                | Error _ -> false
            else
                false
        | "currently-reading" ->
            if book.Status <> BookStatus.Finished then
                match executeBookCommand conn slug (Books.Change_status (BookStatus.InFocus, None)) projectionHandlers with
                | Ok events -> not (List.isEmpty events)
                | Error _ -> false
            else
                false
        | "read" ->
            match item.ReadAt |> Option.bind parseReadAtDate with
            | Some readAtDate ->
                match executeBookCommand conn slug (Books.Change_status (BookStatus.Finished, Some readAtDate)) projectionHandlers with
                | Ok events -> not (List.isEmpty events)
                | Error _ -> false
            | None -> false
        | _ -> false

    /// `UserRating` seeds an unset personal rating once; never overwrites a
    /// rating the user already set in Mediatheca (ADR-0075 §3).
    let private applyRating
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        (item: Goodreads.GoodreadsShelfItem)
        (book: BookDetail)
        : unit =
        match item.UserRating, book.PersonalRating with
        | Some rating, None ->
            executeBookCommand conn slug (Books.Set_personal_rating (Some rating)) projectionHandlers |> ignore
        | _ -> ()

    let private applyStatusAndRating
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        (shelf: string)
        (item: Goodreads.GoodreadsShelfItem)
        : bool =
        match BookProjection.getBySlug conn slug with
        | None -> false
        | Some book ->
            let statusChanged = applyStatus conn projectionHandlers slug shelf item book
            applyRating conn projectionHandlers slug item book
            statusChanged

    // ── Import (unmatched item, shelf configured for import) ────────────

    type private ImportOutcome =
        | ImportCreated of slug: string
        | ImportDuplicate of slug: string
        | ImportFailed of string

    let private findExistingBookForImport (conn: SqliteConnection) (title: string) (authors: string list) (externalIds: BookExternalId list) : (string * string) option =
        let byExternalId = externalIds |> List.tryPick (fun eid -> BookProjection.findByExternalId conn eid)
        match byExternalId with
        | Some slug ->
            match BookProjection.getBySlug conn slug with
            | Some b -> Some (slug, b.Title)
            | None -> Some (slug, title)
        | None ->
            let firstAuthor = authors |> List.tryHead |> Option.map (fun a -> a.ToLowerInvariant())
            BookProjection.findByTitle conn title
            |> List.tryFind (fun (candidateSlug, _) ->
                match BookProjection.getBySlug conn candidateSlug with
                | Some b -> (b.Authors |> List.tryHead |> Option.map (fun a -> a.ToLowerInvariant())) = firstAuthor
                | None -> false)

    let private importBook
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (title: string)
        (authors: string list)
        (year: int option)
        (fallbackCoverUrl: string option)
        (coverDownloader: (string -> Async<string option>) option)
        (subjects: string list)
        (externalIds: BookExternalId list)
        : Async<ImportOutcome> =
        async {
            let existing = withLock jobLock (fun () -> findExistingBookForImport conn title authors externalIds)
            match existing with
            | Some (existingSlug, _) -> return ImportDuplicate existingSlug
            | None ->
                try
                    let yr = year |> Option.defaultValue 0
                    let baseSlug = Slug.bookSlug title yr
                    let slug = withLock jobLock (fun () -> generateUniqueSlug conn Books.streamId baseSlug)
                    let! coverRef =
                        match coverDownloader with
                        | Some download -> download slug
                        | None ->
                            async {
                                match fallbackCoverUrl with
                                | None -> return None
                                | Some url ->
                                    try
                                        let! bytes = httpClient.GetByteArrayAsync(url: string) |> Async.AwaitTask
                                        let relativePath = sprintf "posters/book-%s.jpg" slug
                                        ImageStore.saveImage imageBasePath relativePath bytes
                                        return Some relativePath
                                    with _ -> return None
                            }
                    let bookData: Books.BookAddedData = {
                        Title = title
                        Authors = authors
                        Year = year
                        CoverRef = coverRef
                        Subjects = subjects
                        Format = BookFormat.Print
                        ExternalIds = externalIds
                    }
                    let result = withLock jobLock (fun () -> executeBookCommand conn slug (Books.Add_book_to_library bookData) projectionHandlers)
                    match result with
                    | Ok _ -> return ImportCreated slug
                    | Error e -> return ImportFailed e
                with ex ->
                    return ImportFailed ex.Message
        }

    // ── Per-item processing ──────────────────────────────────────────────

    type private ItemResult = {
        Created: bool
        Linked: bool
        StatusChanged: bool
        Skipped: bool
    }

    let private processItem
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getOpenLibraryConfig: unit -> OpenLibrary.OpenLibraryConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (shelf: string)
        (importShelves: string list)
        (item: Goodreads.GoodreadsShelfItem)
        : Async<ItemResult> =
        async {
            let existingSlug = withLock jobLock (fun () -> resolveExistingSlug conn item)
            match existingSlug with
            | Some slug ->
                let linked, statusChanged =
                    withLock jobLock (fun () ->
                        let linked = linkFoundBook conn projectionHandlers slug item
                        let statusChanged = applyStatusAndRating conn projectionHandlers slug shelf item
                        linked, statusChanged)
                return { Created = false; Linked = linked; StatusChanged = statusChanged; Skipped = false }
            | None ->
                if not (List.contains shelf importShelves) then
                    return { Created = false; Linked = false; StatusChanged = false; Skipped = true }
                else
                    let config = getOpenLibraryConfig ()
                    let! editionOpt =
                        match item.Isbn13 with
                        | Some isbn13 -> OpenLibrary.getEditionByIsbn httpClient config isbn13
                        | None ->
                            match item.Isbn with
                            | Some isbn10 -> OpenLibrary.getEditionByIsbn httpClient config isbn10
                            | None -> async { return None }
                    let! workDescription =
                        match editionOpt |> Option.bind (fun e -> e.WorkKey) with
                        | Some workKey ->
                            async {
                                let! w = OpenLibrary.getWork httpClient config workKey
                                return w.Description
                            }
                        | None -> async { return None }

                    let title = editionOpt |> Option.map (fun e -> e.Title) |> Option.defaultValue item.Title
                    let authors =
                        match editionOpt with
                        | Some e when not (List.isEmpty e.Authors) -> e.Authors
                        | _ -> if System.String.IsNullOrWhiteSpace(item.Author) then [] else [ item.Author ]
                    let year =
                        editionOpt
                        |> Option.bind (fun e -> e.PublishDate)
                        |> Option.bind (fun d ->
                            if d.Length >= 4 then
                                match System.Int32.TryParse(d.[d.Length - 4 ..]) with
                                | true, y -> Some y
                                | _ -> None
                            else None)
                        |> Option.orElse item.Published
                    let coverId = editionOpt |> Option.bind (fun e -> e.CoverId)
                    let externalIds =
                        [ Some (GoodreadsBookId item.BookId)
                          item.Isbn13 |> Option.map Isbn13
                          editionOpt |> Option.map (fun e -> OpenLibraryEdition e.EditionKey)
                          editionOpt |> Option.bind (fun e -> e.WorkKey) |> Option.map OpenLibraryWork ]
                        |> List.choose id
                    let coverDownloader: (string -> Async<string option>) option =
                        coverId
                        |> Option.map (fun id -> fun (slug: string) -> OpenLibrary.downloadCover httpClient config id slug imageBasePath)

                    let! outcome =
                        importBook conn jobLock httpClient imageBasePath projectionHandlers title authors year item.LargeImageUrl coverDownloader [] externalIds

                    match outcome with
                    | ImportCreated slug ->
                        withLock jobLock (fun () ->
                            let metadata: MetadataCache.BookMetadata = {
                                Description = workDescription
                                PageCount = item.NumPages
                                RuntimeMinutes = None
                                Narrators = []
                                SeriesName = None
                                SeriesPosition = None
                                Publisher = None
                                PublishedDate = editionOpt |> Option.bind (fun e -> e.PublishDate)
                                AverageRating = item.AverageRating
                                Language = None
                                Source = Some "goodreads"
                            }
                            MetadataCache.upsertBookMetadata conn slug metadata)
                        let statusChanged = withLock jobLock (fun () -> applyStatusAndRating conn projectionHandlers slug shelf item)
                        return { Created = true; Linked = false; StatusChanged = statusChanged; Skipped = false }
                    | ImportDuplicate slug ->
                        let linked, statusChanged =
                            withLock jobLock (fun () ->
                                let linked = linkFoundBook conn projectionHandlers slug item
                                let statusChanged = applyStatusAndRating conn projectionHandlers slug shelf item
                                linked, statusChanged)
                        return { Created = false; Linked = linked; StatusChanged = statusChanged; Skipped = false }
                    | ImportFailed msg ->
                        return failwith msg
        }

    // ── Result persistence (a plain one-line human summary -- the counts
    // the body already formats, same convention every other job's
    // `JobRunOutcome.Summary` uses) ──

    let formatResult (r: GoodreadsSyncResult) : string =
        let shelfText =
            r.Shelves
            |> List.map (fun s ->
                sprintf "%s: %d fetched, %d created, %d linked, %d status changes, %d skipped"
                    s.Shelf s.Fetched s.Created s.Linked s.StatusChanged s.Skipped)
            |> String.concat "; "
        if List.isEmpty r.Errors then shelfText
        else sprintf "%s (%d item error(s))" shelfText (List.length r.Errors)

    // ── The sync itself ──────────────────────────────────────────────────

    /// Folds all three shelf feeds into the library. `jobLock` follows the
    /// "acquire only around the brief DB moment" discipline `PlaytimeTracker`/
    /// `GameFacetBackfill` establish. On any shelf's fetch failure, the run
    /// ends immediately (ADR-0010: a per-ITEM failure never aborts the run,
    /// but a per-FEED failure -- profile private/unknown, network error --
    /// has nothing left to fold and is reported as the run's own failure).
    let runSync
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getGoodreadsConfig: unit -> Goodreads.GoodreadsConfig)
        (getOpenLibraryConfig: unit -> OpenLibrary.OpenLibraryConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<GoodreadsSyncResult, string>> =
        async {
            let config = getGoodreadsConfig ()
            match config.UserId with
            | None ->
                return Error "Goodreads is not configured -- enter your user id in Settings"
            | Some userId ->
                let mutable shelfSummaries = []
                let mutable allErrors = []
                let mutable failure = None
                for shelf in allShelves do
                    if Option.isNone failure then
                        let! fetchResult = Goodreads.getShelf httpClient userId shelf
                        match fetchResult with
                        | Error err ->
                            failure <- Some (Goodreads.describeError err)
                        | Ok items ->
                            let mutable created = 0
                            let mutable linked = 0
                            let mutable statusChanged = 0
                            let mutable skipped = 0
                            for item in items do
                                try
                                    let! result = processItem conn jobLock httpClient getOpenLibraryConfig imageBasePath projectionHandlers shelf config.ImportShelves item
                                    if result.Created then created <- created + 1
                                    if result.Linked then linked <- linked + 1
                                    if result.StatusChanged then statusChanged <- statusChanged + 1
                                    if result.Skipped then skipped <- skipped + 1
                                with ex ->
                                    allErrors <- allErrors @ [ sprintf "%s (%s): %s" item.Title shelf ex.Message ]
                            shelfSummaries <-
                                shelfSummaries @ [
                                    { Shelf = shelf
                                      Fetched = List.length items
                                      Created = created
                                      Linked = linked
                                      StatusChanged = statusChanged
                                      Skipped = skipped }
                                ]
                match failure with
                | Some err ->
                    withLock jobLock (fun () -> SettingsStore.setSetting conn "goodreads_last_error" err)
                    return Error err
                | None ->
                    let result = { Shelves = shelfSummaries; Errors = allErrors }
                    withLock jobLock (fun () ->
                        SettingsStore.setSetting conn "goodreads_last_sync" (System.DateTime.UtcNow.ToString("o"))
                        SettingsStore.setSetting conn "goodreads_last_sync_result" (formatResult result)
                        SettingsStore.deleteSetting conn "goodreads_last_error")
                    return Ok result
        }
