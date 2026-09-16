module Mediatheca.Tests.GoodreadsSyncTests

/// integration-wmqn3 (ADR-0075/ADR-0076): the shelf sync itself --
/// matching/linking/importing against a fixture that mixes an already-in-
/// library item, an unmatched item Open Library resolves, and an unmatched
/// item with no ISBN; status mapping's "never demote" guard and the
/// read-shelf re-dating correction; personal-rating seeding; a 403 ending
/// the run failed; and the job being a registered, recordable `JobSpec`.
/// No live Goodreads/Open Library call -- every request goes through a stub
/// `HttpMessageHandler`.

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

Goodreads.throttleInterval <- TimeSpan.Zero
OpenLibrary.throttleApiInterval <- TimeSpan.Zero
OpenLibrary.throttleCoversInterval <- TimeSpan.Zero

type private AsyncStubHandler(respond: HttpRequestMessage -> Async<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Async.StartAsTask(respond request)

let private xmlResponse (statusCode: HttpStatusCode) (xml: string) =
    let resp = new HttpResponseMessage(statusCode)
    resp.Content <- new StringContent(xml, System.Text.Encoding.UTF8, "application/rss+xml")
    resp

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private notFoundResponse () = new HttpResponseMessage(HttpStatusCode.NotFound)

let private fakeCoverBytes = Array.create 2048 (byte 0xFF)

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn
    Administration.initializeJobRuns conn

let private allProjectionHandlers = [ ContentBlockProjection.handler; BookProjection.handler ]

let private openLibraryConfig : OpenLibrary.OpenLibraryConfig =
    { UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" }

/// Seeding API -- `addBook`/`setBookStatus`/`setBookPersonalRating` build the
/// pre-existing-library state each scenario needs before the sync runs. The
/// Goodreads members are never exercised through this api; the sync itself
/// is called directly via `GoodreadsSync.runSync`.
let private createSeedApi (factory: unit -> SqliteConnection) (imageBasePath: string) : IMediathecaApi =
    Api.create
        factory
        (new HttpClient())
        (Qbittorrent.createHttpClient ())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> openLibraryConfig)
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        (fun () -> ({ UserId = None; ImportShelves = [ "currently-reading" ] } : Goodreads.GoodreadsConfig))
        (fun () -> async { return Error "not wired in this seeding api" })
        LocalCopyRemoval.defaultMountRoots
        imageBasePath
        allProjectionHandlers

let private withTempImageDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-goodreads-test-images-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

let private emptyChannel (title: string) =
    sprintf "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\"><channel><title>%s</title></channel></rss>" title

// ── The three-item currently-reading fixture (ADR-0075 acceptance
// criterion): item A already in the library (by Isbn13, unlinked), item B
// unmatched with an ISBN Open Library resolves, item C unmatched with no
// ISBN at all. ──

let private currentlyReadingFixture =
    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
<channel>
<title>Marco's bookshelf: currently-reading</title>
<item>
<title>Feed Title A</title>
<book_id>AAA111</book_id>
<author_name>Feed Author A</author_name>
<isbn13>9781111111111</isbn13>
<book_large_image_url>https://example.com/coverA-l.jpg</book_large_image_url>
<user_rating>0</user_rating>
<user_shelves>currently-reading</user_shelves>
</item>
<item>
<title>Feed Title B</title>
<book_id>BBB222</book_id>
<author_name>Feed Author B</author_name>
<isbn13>9782222222222</isbn13>
<book_large_image_url>https://example.com/coverB-l.jpg</book_large_image_url>
<user_rating>0</user_rating>
<user_shelves>currently-reading</user_shelves>
</item>
<item>
<title>Feed Only Title C</title>
<book_id>CCC333</book_id>
<author_name>Feed Author C</author_name>
<book_large_image_url>https://example.com/coverC-l.jpg</book_large_image_url>
<user_rating>0</user_rating>
<user_shelves>currently-reading</user_shelves>
</item>
</channel>
</rss>"""

let private editionJsonB =
    """
    {
        "key": "/books/OL999999M",
        "title": "OL Title B",
        "works": [{"key": "/works/OLWORKB"}],
        "authors": [{"key": "/authors/OLAUTHB"}],
        "publish_date": "2019",
        "publishers": ["Some Publisher"],
        "number_of_pages": 300,
        "covers": [55555]
    }
    """

let private workJsonB =
    """{"description": "OL description B", "subjects": []}"""

/// Dispatches the shelf feeds by `shelf=` query param, plus Open Library's
/// isbn/works/authors/covers endpoints for item B, plus the bare cover URLs
/// items A/C carry as their own feed-sourced cover.
let private httpClientFor (currentlyReading: string) (readShelf: string) (toReadShelf: string) : HttpClient =
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                if url.Contains("list_rss") && url.Contains("shelf=currently-reading") then
                    return xmlResponse HttpStatusCode.OK currentlyReading
                elif url.Contains("list_rss") && url.Contains("shelf=read") then
                    return xmlResponse HttpStatusCode.OK readShelf
                elif url.Contains("list_rss") && url.Contains("shelf=to-read") then
                    return xmlResponse HttpStatusCode.OK toReadShelf
                elif url.Contains("covers.openlibrary.org") then
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
                elif url.Contains("/authors/") then
                    return jsonResponse """{"name": "OL Author B"}"""
                elif url.Contains("/isbn/9782222222222.json") then
                    return jsonResponse editionJsonB
                elif url.Contains("/works/OLWORKB") then
                    return jsonResponse workJsonB
                elif url.Contains("coverA-l.jpg") || url.Contains("coverB-l.jpg") || url.Contains("coverC-l.jpg") then
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
                else
                    return notFoundResponse ()
            })
    new HttpClient(handler)

let private goodreadsConfig (importShelves: string list) : Goodreads.GoodreadsConfig =
    { UserId = Some "12345678"; ImportShelves = importShelves }

[<Tests>]
let goodreadsSyncTests =
    testList "Goodreads shelf sync (integration-wmqn3)" [

        testCase "currently-reading: links an existing book, imports via Open Library, imports from feed data, promotes all three to InFocus, and is idempotent" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath

                // Item A already exists in the library, unlinked to Goodreads.
                let existingSlug =
                    match seedApi.addBook { Title = "Existing Book A"; Authors = [ "Feed Author A" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ Isbn13 "9781111111111" ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other

                let httpClient = httpClientFor currentlyReadingFixture (emptyChannel "read") (emptyChannel "to-read")
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                let summary =
                    match result with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                let currentlyReadingSummary = summary.Shelves |> List.find (fun s -> s.Shelf = "currently-reading")
                Expect.equal currentlyReadingSummary.Fetched 3 "3 items fetched"
                Expect.equal currentlyReadingSummary.Created 2 "2 new books created (B, C)"
                Expect.equal currentlyReadingSummary.Linked 1 "1 external id linked (A)"
                Expect.equal currentlyReadingSummary.StatusChanged 3 "all three promoted to InFocus"

                let bookA =
                    match BookProjection.getBySlug db.Connection existingSlug with
                    | Some b -> b
                    | None -> failtest "book A should still be projected"
                Expect.equal bookA.GoodreadsBookId (Some "AAA111") "book A linked to the Goodreads id"
                Expect.equal bookA.Status BookStatus.InFocus "book A promoted to InFocus"

                match BookProjection.findByExternalId db.Connection (GoodreadsBookId "BBB222") with
                | None -> failtest "book B should have been created"
                | Some slug ->
                    let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                    Expect.equal detail.Title "OL Title B" "book B's title comes from the resolved Open Library edition"
                    Expect.equal detail.Format BookFormat.Print "book B's format is Print"
                    Expect.equal detail.Status BookStatus.InFocus "book B promoted to InFocus"
                    Expect.isSome detail.CoverRef "book B has a cover"

                match BookProjection.findByExternalId db.Connection (GoodreadsBookId "CCC333") with
                | None -> failtest "book C should have been created"
                | Some slug ->
                    let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                    Expect.equal detail.Title "Feed Only Title C" "book C's title comes from the feed (no ISBN to resolve)"
                    Expect.equal detail.Format BookFormat.Print "book C's format is Print"
                    Expect.equal detail.Status BookStatus.InFocus "book C promoted to InFocus"
                    Expect.isSome detail.CoverRef "book C's cover comes from the feed's own book_large_image_url"

                // Idempotent re-run: same fixture, zero new events.
                let secondResult =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously
                match secondResult with
                | Ok r ->
                    let shelf = r.Shelves |> List.find (fun s -> s.Shelf = "currently-reading")
                    Expect.equal shelf.Created 0 "re-run creates nothing"
                    Expect.equal shelf.Linked 0 "re-run links nothing new"
                    Expect.equal shelf.StatusChanged 0 "re-run appends zero status-change events"
                | Error e -> failtestf "Expected Ok on re-run, got Error %s" e)

        testCase "to-read never demotes an InFocus book; read finishes a Backlog book and re-dates an Audible-finished one; both idempotent" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath

                let addSeed title goodreadsId : string =
                    match seedApi.addBook { Title = title; Authors = [ "Someone" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ GoodreadsBookId goodreadsId ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other

                // bookY: InFocus, appears (mistakenly, or re-shelved) on to-read.
                let bookY = addSeed "Book Y" "Y1"
                seedApi.setBookProgress { Slug = bookY; Percent = Some 10; Page = None; TotalPages = None; ObservedOn = Some "2026-08-01" } |> Async.RunSynchronously |> ignore
                Expect.equal (BookProjection.getBySlug db.Connection bookY |> Option.get).Status BookStatus.InFocus "sanity: bookY is InFocus"

                // bookZ: Backlog, appears on read with a real ReadAt.
                let bookZ = addSeed "Book Z" "Z1"
                Expect.equal (BookProjection.getBySlug db.Connection bookZ |> Option.get).Status BookStatus.Backlog "sanity: bookZ is Backlog"

                // bookW: already Finished via an Audible import today; the
                // read shelf later reports a real, earlier ReadAt -- a
                // legitimate correction (ADR-0077's narrowed no-op rule).
                let bookW = addSeed "Book W" "W1"
                seedApi.setBookStatus bookW BookStatus.Finished (Some "2026-09-10") |> Async.RunSynchronously |> ignore

                let toReadFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>to-read</title>
<item><title>Book Y</title><book_id>Y1</book_id><author_name>Someone</author_name><user_rating>0</user_rating><user_shelves>to-read</user_shelves></item>
</channel></rss>"""

                let readFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>read</title>
<item><title>Book Z</title><book_id>Z1</book_id><author_name>Someone</author_name><user_read_at>Wed, 02 Sep 2026 00:00:00 -0800</user_read_at><user_rating>0</user_rating><user_shelves>read</user_shelves></item>
<item><title>Book W</title><book_id>W1</book_id><author_name>Someone</author_name><user_read_at>Mon, 01 Jun 2026 00:00:00 -0800</user_read_at><user_rating>0</user_rating><user_shelves>read</user_shelves></item>
</channel></rss>"""

                let httpClient = httpClientFor (emptyChannel "currently-reading") readFixture toReadFixture
                let jobLock = new SemaphoreSlim(1, 1)

                let run () =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                let first =
                    match run () with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (BookProjection.getBySlug db.Connection bookY |> Option.get).Status BookStatus.InFocus "to-read never demotes bookY"
                let toReadSummary = first.Shelves |> List.find (fun s -> s.Shelf = "to-read")
                Expect.equal toReadSummary.StatusChanged 0 "no status-change event for the never-demoted book"

                let zAfter = BookProjection.getBySlug db.Connection bookZ |> Option.get
                Expect.equal zAfter.Status BookStatus.Finished "bookZ finished"
                Expect.equal zAfter.FinishedAt (Some "2026-09-02") "bookZ's finished_at matches the feed's ReadAt"

                let wAfter = BookProjection.getBySlug db.Connection bookW |> Option.get
                Expect.equal wAfter.Status BookStatus.Finished "bookW stays Finished"
                Expect.equal wAfter.FinishedAt (Some "2026-06-01") "bookW's finished_at re-dated to the true (earlier, real) ReadAt"

                let readSummary = first.Shelves |> List.find (fun s -> s.Shelf = "read")
                Expect.equal readSummary.StatusChanged 2 "both Z (fresh finish) and W (re-date) produced exactly one event each"

                // Idempotent re-run with the identical fixtures.
                match run () with
                | Ok second ->
                    let readAgain = second.Shelves |> List.find (fun s -> s.Shelf = "read")
                    Expect.equal readAgain.StatusChanged 0 "re-run with identical ReadAt values appends zero events"
                | Error e -> failtestf "Expected Ok on re-run, got Error %s" e)

        testCase "UserRating seeds an unset personal rating and never overwrites an existing one" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath

                let addSeed title goodreadsId : string =
                    match seedApi.addBook { Title = title; Authors = [ "Someone" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ GoodreadsBookId goodreadsId ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other

                let unratedSlug = addSeed "Unrated Book" "R1"
                let ratedSlug = addSeed "Already Rated Book" "R2"
                seedApi.setBookPersonalRating ratedSlug (Some 5) |> Async.RunSynchronously |> ignore

                let fixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>currently-reading</title>
<item><title>Unrated Book</title><book_id>R1</book_id><author_name>Someone</author_name><user_rating>4</user_rating><user_shelves>currently-reading</user_shelves></item>
<item><title>Already Rated Book</title><book_id>R2</book_id><author_name>Someone</author_name><user_rating>2</user_rating><user_shelves>currently-reading</user_shelves></item>
</channel></rss>"""

                let httpClient = httpClientFor fixture (emptyChannel "read") (emptyChannel "to-read")
                let jobLock = new SemaphoreSlim(1, 1)

                GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                Expect.equal (BookProjection.getBySlug db.Connection unratedSlug |> Option.get).PersonalRating (Some 4) "unset rating seeded from UserRating"
                Expect.equal (BookProjection.getBySlug db.Connection ratedSlug |> Option.get).PersonalRating (Some 5) "existing rating untouched")

        testCase "a 403 feed response ends the run failed, records the fixed message, and appends no events" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let httpClient = httpClientFor "" (emptyChannel "read") (emptyChannel "to-read")
                // Override: currently-reading itself 403s (checked first).
                let handler =
                    new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("shelf=currently-reading") then
                                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                            else
                                return xmlResponse HttpStatusCode.OK (emptyChannel "x")
                        })
                let forbiddenClient = new HttpClient(handler)
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock forbiddenClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Error msg -> Expect.equal msg "profile private or user id unknown" "the fixed, exact error text"
                | Ok r -> failtestf "Expected Error, got Ok %A" r

                Expect.equal (SettingsStore.getSetting db.Connection "goodreads_last_error") (Some "profile private or user id unknown") "persisted for the Settings badge"
                Expect.isEmpty (BookProjection.getAll db.Connection) "no book was created or touched")
    ]
    |> testSequenced

/// integration-y2ak4 (ADR-0075 §4): the user-status-feed progress step --
/// join to a currently-reading shelf item, ambiguous-title handling,
/// oldest-first ordering, per-source idempotency via the persisted marker,
/// `Started` items never emitting a command, floor rounding, and pagination
/// (natural end-of-feed and a mid-walk fetch failure).
[<Tests>]
let goodreadsProgressSyncTests =
    testList "Goodreads user-status progress step (integration-y2ak4)" [

        testCase "an on-page status joins to a currently-reading shelf item; an ambiguous title counts Unmatched" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath

                let addSeed title goodreadsId : string =
                    match seedApi.addBook { Title = title; Authors = [ "Someone" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ GoodreadsBookId goodreadsId ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other

                let duneSlug = addSeed "Dune" "D1"
                addSeed "Foundation" "F1" |> ignore
                addSeed "Foundation and Empire" "F2" |> ignore

                let currentlyReadingFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>currently-reading</title>
<item><title>Dune</title><book_id>D1</book_id><author_name>Someone</author_name><user_rating>0</user_rating><user_shelves>currently-reading</user_shelves></item>
</channel></rss>"""

                let statusFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco is on page 120 of 300 of Dune</title><link>https://www.goodreads.com/user_status/show/5001</link><pubDate>Tue, 15 Sep 2026 10:00:00 -0800</pubDate></item>
<item><title>Marco is 50% done with Foundation and E</title><link>https://www.goodreads.com/user_status/show/5002</link><pubDate>Wed, 16 Sep 2026 10:00:00 -0800</pubDate></item>
</channel></rss>"""

                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") && url.Contains("shelf=currently-reading") then
                                return xmlResponse HttpStatusCode.OK currentlyReadingFixture
                            elif url.Contains("list_rss") then
                                return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                if page = 1 then return xmlResponse HttpStatusCode.OK statusFixture
                                else return xmlResponse HttpStatusCode.OK (emptyChannel "status")
                            else
                                return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                let summary =
                    match result with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal summary.Progress.ProgressObserved 1 "the Dune item matched (via the currently-reading shelf join) and was observed"
                Expect.equal summary.Progress.Unmatched 1 "the ambiguous \"Foundation and E\" item counted Unmatched, not a guess"

                match BookProjection.getProgressHistory db.Connection duneSlug with
                | [ obs ] ->
                    Expect.equal obs.Percent 40 "floor(120/300*100) = 40"
                    Expect.equal obs.Source ProgressSource.Goodreads "source"
                    Expect.equal obs.Position (Some (Page (120, Some 300))) "position"
                    Expect.equal obs.ObservedOn "2026-09-15" "observed on the item's own pubDate"
                | other -> failtestf "Expected exactly one observation on Dune, got %A" other)

        testCase "items are processed oldest-first (promote then finish), idempotent on re-run, and only a genuinely new item is processed next" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath

                let addSeed title goodreadsId : string =
                    match seedApi.addBook { Title = title; Authors = [ "Someone" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ GoodreadsBookId goodreadsId ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other

                let syncSlug = addSeed "Sync Book" "S1"
                let otherSlug = addSeed "Other Book" "S2"

                let mutable statusFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco finished reading Sync Book</title><link>https://www.goodreads.com/user_status/show/6003</link><pubDate>Thu, 10 Sep 2026 09:00:00 -0800</pubDate></item>
<item><title>Marco is 40% done with Sync Book</title><link>https://www.goodreads.com/user_status/show/6002</link><pubDate>Sat, 05 Sep 2026 09:00:00 -0800</pubDate></item>
<item><title>Marco is 10% done with Sync Book</title><link>https://www.goodreads.com/user_status/show/6001</link><pubDate>Tue, 01 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""

                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") then
                                return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                if page = 1 then return xmlResponse HttpStatusCode.OK statusFixture
                                else return xmlResponse HttpStatusCode.OK (emptyChannel "status")
                            else
                                return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                let run () =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                let first =
                    match run () with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal first.Progress.ProgressObserved 3 "all three items observed"

                let history = BookProjection.getProgressHistory db.Connection syncSlug
                Expect.equal (history |> List.map (fun h -> h.Percent)) [ 10; 40; 100 ] "oldest-first: 10, then 40, then the finish's 100"

                let afterFirst = BookProjection.getBySlug db.Connection syncSlug |> Option.get
                Expect.equal afterFirst.Status BookStatus.Finished "the finished item's 100% finishes the book"
                Expect.equal afterFirst.FinishedAt (Some "2026-09-10") "finished_at dates from the finishing observation's own day"

                let second =
                    match run () with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on re-run, got Error %s" e
                Expect.equal second.Progress.ProgressObserved 0 "re-running with the identical feed appends zero events"
                Expect.equal second.Progress.Unmatched 0 "nothing unmatched on the re-run either"

                // A genuinely new 4th item, for a different book, is added.
                statusFixture <-
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco is 50% done with Other Book</title><link>https://www.goodreads.com/user_status/show/6004</link><pubDate>Fri, 11 Sep 2026 09:00:00 -0800</pubDate></item>
<item><title>Marco finished reading Sync Book</title><link>https://www.goodreads.com/user_status/show/6003</link><pubDate>Thu, 10 Sep 2026 09:00:00 -0800</pubDate></item>
<item><title>Marco is 40% done with Sync Book</title><link>https://www.goodreads.com/user_status/show/6002</link><pubDate>Sat, 05 Sep 2026 09:00:00 -0800</pubDate></item>
<item><title>Marco is 10% done with Sync Book</title><link>https://www.goodreads.com/user_status/show/6001</link><pubDate>Tue, 01 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""

                let third =
                    match run () with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on third run, got Error %s" e
                Expect.equal third.Progress.ProgressObserved 1 "only the new 4th item (a different book) is processed"

                let otherHistory = BookProjection.getProgressHistory db.Connection otherSlug
                Expect.equal (otherHistory |> List.map (fun h -> h.Percent)) [ 50 ] "Other Book received exactly the new observation")

        testCase "a Started item never calls Observe_reading_progress and is counted separately from Unmatched/ProgressObserved" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let statusFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco is starting Some New Book</title><link>https://www.goodreads.com/user_status/show/7001</link><pubDate>Mon, 07 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""
                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") then return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                if page = 1 then return xmlResponse HttpStatusCode.OK statusFixture
                                else return xmlResponse HttpStatusCode.OK (emptyChannel "status")
                            else return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Progress.Started 1 "the Started item is counted"
                    Expect.equal r.Progress.ProgressObserved 0 "no Observe_reading_progress command was issued for it"
                    Expect.equal r.Progress.Unmatched 0 "Started is counted separately from Unmatched"
                    Expect.isEmpty (BookProjection.getAll db.Connection) "no book exists at all -- nothing was created or touched"
                | Error e -> failtestf "Expected Ok, got Error %s" e)

        testCase "PageProgress (299, 300, _) floors to 99, never rounds to 100" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let seedApi = createSeedApi db.Factory imageBasePath
                let addSeed title goodreadsId : string =
                    match seedApi.addBook { Title = title; Authors = [ "Someone" ]; Year = Some 2020; CoverUrl = None; Subjects = []; Format = BookFormat.Unknown; ExternalIds = [ GoodreadsBookId goodreadsId ]; SkipDuplicateCheck = true } |> Async.RunSynchronously with
                    | Ok (Book_added slug) -> slug
                    | other -> failtestf "seed failed: %A" other
                let slug = addSeed "Precision Book" "P1"

                let statusFixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco is on page 299 of 300 of Precision Book</title><link>https://www.goodreads.com/user_status/show/8001</link><pubDate>Wed, 09 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""
                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") then return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                if page = 1 then return xmlResponse HttpStatusCode.OK statusFixture
                                else return xmlResponse HttpStatusCode.OK (emptyChannel "status")
                            else return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                Expect.equal detail.ProgressPercent 99 "floor(299/300*100) = 99, not 100"
                Expect.equal detail.Status BookStatus.InFocus "99% never auto-finishes the book")

        testCase "first run: a two-page status feed is fully walked and the marker records the newest item" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let page1Fixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco wrote a review of Some Book</title><link>https://www.goodreads.com/user_status/show/9002</link><pubDate>Fri, 12 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""
                let page2Fixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco added Some Other Book to a shelf</title><link>https://www.goodreads.com/user_status/show/9001</link><pubDate>Thu, 11 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""
                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") then return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                match page with
                                | 1 -> return xmlResponse HttpStatusCode.OK page1Fixture
                                | 2 -> return xmlResponse HttpStatusCode.OK page2Fixture
                                | _ -> return xmlResponse HttpStatusCode.OK (emptyChannel "status")
                            else return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Progress.Ignored 2 "both items are unrecognized shapes -- pages 1 and 2 were both walked"
                    Expect.equal (SettingsStore.getSetting db.Connection "goodreads_last_status_id") (Some "9002") "the marker records the newest (page 1) item's id"
                | Error e -> failtestf "Expected Ok, got Error %s" e)

        testCase "a status-page fetch failure after page 1 still processes page 1's items and reports the error" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let page1Fixture =
                    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Marco's updates</title>
<item><title>Marco wrote a review of Some Book</title><link>https://www.goodreads.com/user_status/show/9102</link><pubDate>Fri, 12 Sep 2026 09:00:00 -0800</pubDate></item>
</channel></rss>"""
                let httpClient =
                    new HttpClient(new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("list_rss") then return xmlResponse HttpStatusCode.OK (emptyChannel "shelf")
                            elif url.Contains("user_status/list") then
                                let m = System.Text.RegularExpressions.Regex.Match(url, @"page=(\d+)")
                                let page = if m.Success then int m.Groups.[1].Value else 1
                                if page = 1 then return xmlResponse HttpStatusCode.OK page1Fixture
                                else return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                            else return notFoundResponse ()
                        }))
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Progress.Ignored 1 "page 1's item was still processed despite page 2 failing"
                    Expect.isTrue (r.Errors |> List.exists (fun e -> e.Contains "progress feed")) "the page 2 failure is reported as an error"
                    Expect.equal (SettingsStore.getSetting db.Connection "goodreads_last_status_id") (Some "9102") "the marker still records the newest item actually fetched"
                | Error e -> failtestf "Expected Ok (the run itself still succeeds), got Error %s" e)
    ]
    |> testSequenced

[<Tests>]
let goodreadsJobRegistrationTests =
    testList "Goodreads shelf sync job registration (integration-wmqn3, ADR-0026)" [

        testCase "\"Goodreads shelf sync\" is a registered JobSpec whose manual run is recorded via the shared job_runs registry" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let jobLock = new SemaphoreSlim(1, 1)
                let httpClient = httpClientFor (emptyChannel "currently-reading") (emptyChannel "read") (emptyChannel "to-read")

                let spec : ScheduledJobs.JobSpec = {
                    Name = "Goodreads shelf sync"
                    Hour = 5
                    Run = fun () ->
                        async {
                            match! GoodreadsSync.runSync db.Connection jobLock httpClient (fun () -> goodreadsConfig [ "currently-reading" ]) (fun () -> openLibraryConfig) imageBasePath allProjectionHandlers with
                            | Ok result -> return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = GoodreadsSync.formatResult result } : ScheduledJobs.JobRunOutcome)
                            | Error err -> return ({ Disposition = ScheduledJobs.JobDisposition.Skipped; Summary = err } : ScheduledJobs.JobRunOutcome)
                        }
                }

                let recorder = Administration.makeJobRunRecorder db.Connection jobLock
                let adminApi = Administration.create db.Factory "test-fixtures-do-not-exist/mediatheca.db" imageBasePath allProjectionHandlers [ spec ] recorder (Administration.makeGuards ())

                // The job is listed.
                let statuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                Expect.exists statuses (fun s -> s.JobName = "Goodreads shelf sync") "the job is listed in the Jobs section"

                // "Sync now" (runJobNow) records a manual run.
                match adminApi.runJobNow "Goodreads shelf sync" |> Async.RunSynchronously with
                | RunJobStarted _ -> ()
                | RunJobRejected -> failtest "Expected the manual run to start"

                // runJobNow is fire-and-forget; give the (fast, stubbed) body
                // a moment to reach a terminal row.
                let mutable attempts = 0
                let mutable settled = false
                while not settled && attempts < 50 do
                    let statuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                    match statuses |> List.tryFind (fun s -> s.JobName = "Goodreads shelf sync") |> Option.bind (fun s -> s.LastRun) with
                    | Some run when run.Status <> RunStatusRunning -> settled <- true
                    | _ ->
                        Thread.Sleep(20)
                        attempts <- attempts + 1

                let finalStatuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                let jobStatus = finalStatuses |> List.find (fun s -> s.JobName = "Goodreads shelf sync")
                match jobStatus.LastRun with
                | Some run ->
                    Expect.equal run.Trigger "manual" "the Settings/Jobs \"Sync now\" trigger is recorded as manual"
                    Expect.notEqual run.Status RunStatusRunning "the run reached a terminal status"
                | None -> failtest "Expected a recorded run")
    ]
    |> testSequenced
