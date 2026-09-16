module Mediatheca.Tests.OpenLibraryTests

/// integration-c8d4x (ADR-0075): Open Library is the key-less book search
/// and metadata source. This suite pins:
/// 1. The decoders (`search.json`'s `docs`, `/works/{key}.json`'s
///    `description` string-or-object shape, missing `cover_i`, first-13-digit
///    `isbn` picking).
/// 2. The adapter-owned throttle (`Steam.throttleStorefrontCall`'s shape,
///    ADR-0066) — two independent gates, `openlibrary.org` and
///    `covers.openlibrary.org`.
/// 3. Every outbound request carries the configured `User-Agent`.
/// 4. A 404 from `/isbn/{isbn}.json` degrades to `None`, never an exception.
///
/// No test here makes a live Open Library call — every request goes through
/// a stub `HttpMessageHandler`.

open System.Net
open System.Net.Http
open System.Threading
open Expecto
open Thoth.Json.Net
open Mediatheca.Server

let private testConfig : OpenLibrary.OpenLibraryConfig =
    { UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" }

type private AsyncStubHandler(respond: HttpRequestMessage -> Async<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Async.StartAsTask(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private notFoundResponse () =
    new HttpResponseMessage(HttpStatusCode.NotFound)

/// Clock/dispatch-overhead tolerance shared by every timing assertion below
/// — the same reasoning `SteamStorefrontThrottleTests.clockTolerance`
/// documents (jitter is a roughly-bounded absolute quantity, not
/// proportional to the interval).
let private clockTolerance = System.TimeSpan.FromMilliseconds(5.0)

// ── Fixtures ─────────────────────────────────────────────────────────────

/// A captured-shaped `search.json` fixture: doc 1 carries a full field set
/// (mixed ISBN-10/ISBN-13 list, to pin "first 13-digit value"); doc 2 omits
/// `cover_i` entirely (-> `None`).
let private searchJsonFixture =
    """
    {
        "docs": [
            {
                "key": "/works/OL893415W",
                "title": "Project Hail Mary",
                "author_name": ["Andy Weir"],
                "first_publish_year": 2021,
                "cover_i": 12345678,
                "isbn": ["0593135202", "9780593135204", "593135202X"],
                "edition_key": ["OL33246498M", "OL33246499M"],
                "subject": ["Science fiction", "Space flight", "Fiction"],
                "number_of_pages_median": 496
            },
            {
                "key": "/works/OL1234567W",
                "title": "No Cover Book",
                "author_name": ["Someone Else"],
                "first_publish_year": 1999
            }
        ]
    }
    """

let private workJsonStringDescription =
    """{"description": "A plain string description.", "subjects": ["Novel"]}"""

let private workJsonObjectDescription =
    """{"description": {"type": "/type/text", "value": "An object-shaped description."}, "subjects": []}"""

let private isbnEditionJson =
    """
    {
        "key": "/books/OL33246498M",
        "title": "Project Hail Mary",
        "works": [{"key": "/works/OL893415W"}],
        "authors": [{"key": "/authors/OL1394865A"}],
        "publish_date": "2021",
        "publishers": ["Ballantine Books"],
        "number_of_pages": 496,
        "covers": [12345678],
        "identifiers": {"goodreads": ["54493401"]}
    }
    """

[<Tests>]
let openLibraryTests =
    testList "Open Library adapter (integration-c8d4x)" [

        testCase "searchBooks decodes docs: full fields, first 13-digit ISBN, missing cover_i -> None" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse searchJsonFixture })
            let http = new HttpClient(handler)
            let results = OpenLibrary.searchBooks http testConfig "project hail mary unique query 1" |> Async.RunSynchronously

            Expect.equal (List.length results) 2 "Both docs decoded"

            let first = results.[0]
            Expect.equal first.WorkKey "/works/OL893415W" "WorkKey decoded"
            Expect.equal first.Title "Project Hail Mary" "Title decoded"
            Expect.equal first.Authors [ "Andy Weir" ] "Authors decoded"
            Expect.equal first.Year (Some 2021) "Year decoded"
            Expect.equal first.CoverId (Some 12345678) "CoverId decoded"
            Expect.equal first.Isbn13 (Some "9780593135204") "Picks the first 13-digit ISBN from a mixed list"
            Expect.equal first.EditionKey (Some "OL33246498M") "First edition key picked"
            Expect.equal (List.length first.Subjects) 3 "Subjects decoded"
            Expect.equal first.PageCount (Some 496) "PageCount decoded"
            Expect.isTrue (first.CoverUrl |> Option.exists (fun u -> u.Contains("12345678") && u.Contains("-M.jpg"))) "CoverUrl built at size M"

            let second = results.[1]
            Expect.equal second.CoverId None "Missing cover_i decodes to None"
            Expect.equal second.CoverUrl None "No CoverId -> no CoverUrl"
            Expect.equal second.Isbn13 None "No isbn field -> None"

        testCase "getWork decodes a plain string description" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse workJsonStringDescription })
            let http = new HttpClient(handler)
            let work = OpenLibrary.getWork http testConfig "/works/OL893415W" |> Async.RunSynchronously
            Expect.equal work.Description (Some "A plain string description.") "String description decodes"
            Expect.equal work.Subjects [ "Novel" ] "Subjects decoded"

        testCase "getWork decodes a {type,value} object description" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse workJsonObjectDescription })
            let http = new HttpClient(handler)
            let work = OpenLibrary.getWork http testConfig "/works/OL893415W" |> Async.RunSynchronously
            Expect.equal work.Description (Some "An object-shaped description.") "Object-shaped description decodes via its 'value' field"

        testCase "getEditionByIsbn decodes the edition, resolves goodreads ids and page count" <| fun _ ->
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        let url = req.RequestUri.ToString()
                        if url.Contains("/authors/") then
                            return jsonResponse """{"name": "Andy Weir"}"""
                        else
                            return jsonResponse isbnEditionJson
                    })
            let http = new HttpClient(handler)
            let editionOpt = OpenLibrary.getEditionByIsbn http testConfig "9780593135204" |> Async.RunSynchronously
            match editionOpt with
            | None -> failtest "Expected Some edition"
            | Some edition ->
                Expect.equal edition.EditionKey "/books/OL33246498M" "EditionKey decoded"
                Expect.equal edition.WorkKey (Some "/works/OL893415W") "WorkKey decoded"
                Expect.equal edition.Authors [ "Andy Weir" ] "Author name resolved via /authors/{key}.json"
                Expect.equal edition.PublishDate (Some "2021") "PublishDate decoded"
                Expect.equal edition.Publishers [ "Ballantine Books" ] "Publishers decoded"
                Expect.equal edition.PageCount (Some 496) "PageCount decoded"
                Expect.equal edition.CoverId (Some 12345678) "First cover id picked"
                Expect.equal edition.GoodreadsIds [ "54493401" ] "Goodreads cross-reference decoded from identifiers.goodreads"

        testCase "getEditionByIsbn yields None on a 404, not an exception" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return notFoundResponse () })
            let http = new HttpClient(handler)
            let result = OpenLibrary.getEditionByIsbn http testConfig "0000000000000" |> Async.RunSynchronously
            Expect.equal result None "A 404 degrades to None"

        // verifier iteration 1: `search.json`'s `edition_key` is an OLID
        // (`OL33246498M`), never an ISBN — `getEditionByOlid` is the
        // OLID-addressed sibling of `getEditionByIsbn`, hitting
        // `/books/{OLID}.json` instead of `/isbn/{isbn}.json`.
        testCase "getEditionByOlid resolves a genuine edition_key OLID, decoding the same shape as getEditionByIsbn" <| fun _ ->
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        let url = req.RequestUri.ToString()
                        if url.Contains("/authors/") then
                            return jsonResponse """{"name": "Andy Weir"}"""
                        elif url.Contains("/books/OL33246498M.json") then
                            return jsonResponse isbnEditionJson
                        else
                            return notFoundResponse ()
                    })
            let http = new HttpClient(handler)
            let editionOpt = OpenLibrary.getEditionByOlid http testConfig "OL33246498M" |> Async.RunSynchronously
            match editionOpt with
            | None -> failtest "Expected Some edition for a genuine edition_key OLID"
            | Some edition ->
                Expect.equal edition.EditionKey "/books/OL33246498M" "EditionKey decoded"
                Expect.equal edition.Title "Project Hail Mary" "Title decoded"
                Expect.equal edition.CoverId (Some 12345678) "CoverId decoded"
                Expect.equal edition.GoodreadsIds [ "54493401" ] "Goodreads cross-reference decoded"

        testCase "getEditionByOlid yields None on a 404, not an exception" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return notFoundResponse () })
            let http = new HttpClient(handler)
            let result = OpenLibrary.getEditionByOlid http testConfig "OL00000000M" |> Async.RunSynchronously
            Expect.equal result None "A 404 degrades to None"

        // verifier iteration 1: this used to only drive
        // searchBooks/getWork/getEditionByIsbn — never the cover path, which
        // is exactly the request that bypassed the User-Agent/throttle in
        // production (`downloadCover` was dead code). `downloadCover` is now
        // exercised here too.
        testCase "Every outbound request carries the configured User-Agent, including the cover download" <| fun _ ->
            let recordedUserAgents = System.Collections.Concurrent.ConcurrentBag<string list>()
            let fakeCoverBytes = Array.create 2048 (byte 0xFF)
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        let ua =
                            if req.Headers.Contains("User-Agent") then req.Headers.GetValues("User-Agent") |> List.ofSeq
                            else []
                        recordedUserAgents.Add(ua)
                        let url = req.RequestUri.ToString()
                        if url.Contains("search.json") then return jsonResponse """{"docs":[]}"""
                        elif url.Contains("covers.openlibrary.org") then
                            let resp = new HttpResponseMessage(HttpStatusCode.OK)
                            resp.Content <- new ByteArrayContent(fakeCoverBytes)
                            return resp
                        elif url.Contains("/works/") then return jsonResponse """{"description": "d", "subjects": []}"""
                        else return notFoundResponse ()
                    })
            let http = new HttpClient(handler)
            let imageDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "mediatheca-openlibrary-ua-test-%s" (System.Guid.NewGuid().ToString("N")))
            System.IO.Directory.CreateDirectory(imageDir) |> ignore
            try
                OpenLibrary.searchBooks http testConfig "unique user agent query" |> Async.RunSynchronously |> ignore
                OpenLibrary.getWork http testConfig "/works/OL1W" |> Async.RunSynchronously |> ignore
                OpenLibrary.getEditionByIsbn http testConfig "0000000000001" |> Async.RunSynchronously |> ignore
                let coverResult = OpenLibrary.downloadCover http testConfig 999 "some-book-slug" imageDir |> Async.RunSynchronously
                Expect.equal coverResult (Some "posters/book-some-book-slug.jpg") "Cover download succeeded through the throttled/UA'd path"

                Expect.isTrue (recordedUserAgents.Count >= 4) "At least 4 requests were recorded, including the cover"
                for ua in recordedUserAgents do
                    Expect.isTrue
                        (ua |> List.exists (fun v -> v.Contains("Mediatheca/1.0")))
                        (sprintf "Expected every request's User-Agent header — including the cover request — to carry the configured value, got %A" ua)
            finally
                try System.IO.Directory.Delete(imageDir, true) with _ -> ()

        testCase "The gate: three concurrent searchBooks calls complete in at least 2 x the configured interval" <| fun _ ->
            let originalInterval = OpenLibrary.throttleApiInterval
            let interval = System.TimeSpan.FromMilliseconds(50.0)
            try
                OpenLibrary.throttleApiInterval <- interval
                let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse """{"docs":[]}""" })
                let http = new HttpClient(handler)
                let sw = System.Diagnostics.Stopwatch.StartNew()
                [ "throttle query alpha"; "throttle query beta"; "throttle query gamma" ]
                |> List.map (OpenLibrary.searchBooks http testConfig)
                |> Async.Parallel
                |> Async.RunSynchronously
                |> ignore
                sw.Stop()
                let expectedMinimum = interval + interval - clockTolerance
                Expect.isTrue
                    (sw.Elapsed >= expectedMinimum)
                    (sprintf "Expected 3 gated calls to take at least %A, took %A" expectedMinimum sw.Elapsed)
            finally
                OpenLibrary.throttleApiInterval <- originalInterval

        testCase "The covers gate is independent of the API gate: a covers call does not wait on a held API call" <| fun _ ->
            let originalApi = OpenLibrary.throttleApiInterval
            let originalCovers = OpenLibrary.throttleCoversInterval
            try
                OpenLibrary.throttleApiInterval <- System.TimeSpan.FromMilliseconds(500.0)
                OpenLibrary.throttleCoversInterval <- System.TimeSpan.FromMilliseconds(20.0)
                let apiCallStarted = new ManualResetEventSlim(false)
                let apiTask =
                    OpenLibrary.throttleApiCall (fun () ->
                        async {
                            apiCallStarted.Set()
                            do! Async.Sleep 400
                            return ()
                        })
                    |> Async.StartAsTask
                apiCallStarted.Wait() |> ignore

                let sw = System.Diagnostics.Stopwatch.StartNew()
                OpenLibrary.throttleCoverCall (fun () -> async { return () }) |> Async.RunSynchronously
                sw.Stop()

                apiTask.Wait()
                Expect.isTrue
                    (sw.ElapsedMilliseconds < 200L)
                    (sprintf "A covers call should not wait on the API gate being held by a slow call; took %dms" sw.ElapsedMilliseconds)
            finally
                OpenLibrary.throttleApiInterval <- originalApi
                OpenLibrary.throttleCoversInterval <- originalCovers
    ]
    |> testSequenced
