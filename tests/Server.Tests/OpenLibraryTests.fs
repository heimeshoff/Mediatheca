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

// ── books-xntts: canonical-edition selection fixtures ───────────────────

let private coverEditionKeyPresentFixture =
    """
    {
        "docs": [
            {
                "key": "/works/OL27448W",
                "title": "The Lord of the Rings",
                "cover_edition_key": "OL51694024M",
                "edition_key": ["OL62536872M", "OL33246498M"],
                "language": ["eng"]
            }
        ]
    }
    """

let private noCoverEditionKeyEngPresentFixture =
    """
    {
        "docs": [
            {
                "key": "/works/OLbxxxxW",
                "title": "Some Work",
                "edition_key": ["OL11111111M", "OL22222222M"],
                "language": ["spa", "eng"]
            }
        ]
    }
    """

let private noCoverEditionKeyNoLanguageFixture =
    """
    {
        "docs": [
            {
                "key": "/works/OLbyyyyW",
                "title": "Another Work",
                "edition_key": ["OL33333333M", "OL44444444M"]
            }
        ]
    }
    """

let private noCoverEditionKeyNoEngLanguageFixture =
    """
    {
        "docs": [
            {
                "key": "/works/OLbzzzzW",
                "title": "Foreign Work",
                "edition_key": ["OL55555555M", "OL66666666M"],
                "language": ["spa"]
            }
        ]
    }
    """

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
        "covers": [12345678]
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

        // books-xntts: `search.json`'s `edition_key` carries no meaningful
        // order (253 entries for a real work, the first a decades-old
        // foreign printing) -- `cover_edition_key` is the request field that
        // lets the request URL and decoder aim at the edition Open Library
        // itself already picked as canonical.
        testCase "searchBooks's request URL includes cover_edition_key and language in fields=" <| fun _ ->
            let mutable capturedUrl = ""
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        capturedUrl <- req.RequestUri.ToString()
                        return jsonResponse """{"docs":[]}"""
                    })
            let http = new HttpClient(handler)
            OpenLibrary.searchBooks http testConfig "books-xntts fields url unique query" |> Async.RunSynchronously |> ignore
            Expect.isTrue (capturedUrl.Contains("cover_edition_key")) "fields= includes cover_edition_key"
            Expect.isTrue (capturedUrl.Contains("language")) "fields= includes language"

        testCase "decodeSearchDoc: cover_edition_key present is always preferred over the first edition_key" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse coverEditionKeyPresentFixture })
            let http = new HttpClient(handler)
            let results = OpenLibrary.searchBooks http testConfig "books-xntts cover edition key unique query" |> Async.RunSynchronously
            Expect.equal results.[0].EditionKey (Some "OL51694024M") "cover_edition_key wins over edition_key[0]"

        testCase "decodeSearchDoc: no cover_edition_key, language contains eng -> first edition_key" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse noCoverEditionKeyEngPresentFixture })
            let http = new HttpClient(handler)
            let results = OpenLibrary.searchBooks http testConfig "books-xntts eng language unique query" |> Async.RunSynchronously
            Expect.equal results.[0].EditionKey (Some "OL11111111M") "Falls back to the first edition_key"

        testCase "decodeSearchDoc: no cover_edition_key, no language field -> first edition_key" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse noCoverEditionKeyNoLanguageFixture })
            let http = new HttpClient(handler)
            let results = OpenLibrary.searchBooks http testConfig "books-xntts no language unique query" |> Async.RunSynchronously
            Expect.equal results.[0].EditionKey (Some "OL33333333M") "Falls back to the first edition_key"

        testCase "decodeSearchDoc: no cover_edition_key, language present without eng -> first edition_key (documented ceiling)" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse noCoverEditionKeyNoEngLanguageFixture })
            let http = new HttpClient(handler)
            let results = OpenLibrary.searchBooks http testConfig "books-xntts no eng language unique query" |> Async.RunSynchronously
            Expect.equal results.[0].EditionKey (Some "OL55555555M") "Still falls back to the first edition_key -- today's behaviour is the ceiling, not a regression"

        // books-xntts: `getWork` now converts the decoded Markdown
        // description into `DescriptionSanitizer`'s allowlisted HTML subset
        // at decode time -- a plain, tag-free single-line description
        // becomes one `<p>` block (`descriptionToHtml`'s own tests below
        // pin the conversion itself in detail).
        testCase "getWork decodes a plain string description, converted to a <p> block" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse workJsonStringDescription })
            let http = new HttpClient(handler)
            let work = OpenLibrary.getWork http testConfig "/works/OL893415W" |> Async.RunSynchronously
            Expect.equal work.Description (Some "<p>A plain string description.</p>") "String description decodes and is converted to a <p> block"
            Expect.equal work.Subjects [ "Novel" ] "Subjects decoded"

        testCase "getWork decodes a {type,value} object description, converted to a <p> block" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse workJsonObjectDescription })
            let http = new HttpClient(handler)
            let work = OpenLibrary.getWork http testConfig "/works/OL893415W" |> Async.RunSynchronously
            Expect.equal work.Description (Some "<p>An object-shaped description.</p>") "Object-shaped description decodes via its 'value' field, then is converted to a <p> block"

        testCase "getEditionByIsbn decodes the edition and page count" <| fun _ ->
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

/// books-xntts: `OpenLibrary.descriptionToHtml`, the pure Markdown ->
/// `DescriptionSanitizer`-allowlisted-HTML converter `getWork` applies to
/// every decoded work description. No HTTP involved -- these drive the
/// function directly.
[<Tests>]
let openLibraryDescriptionToHtmlTests =
    testList "OpenLibrary.descriptionToHtml (books-xntts)" [

        testCase "blank-line-separated paragraphs become <p> blocks" <| fun _ ->
            let input = "First paragraph.\n\nSecond paragraph."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>First paragraph.</p><p>Second paragraph.</p>" "Each blank-line-separated chunk becomes its own <p>"

        testCase "a single newline inside a paragraph becomes <br>" <| fun _ ->
            let input = "Line one.\nLine two."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>Line one.<br>Line two.</p>" "Internal single newline becomes <br>, not a new paragraph"

        testCase "**bold** and *italic* convert to <strong>/<em>" <| fun _ ->
            let input = "This is **bold** and *italic* text."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>This is <strong>bold</strong> and <em>italic</em> text.</p>" "** -> strong, * -> em"

        testCase "__bold__ and _italic_ (underscore family) convert to <strong>/<em>" <| fun _ ->
            let input = "This is __bold__ and _italic_ text."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>This is <strong>bold</strong> and <em>italic</em> text.</p>" "__ -> strong, _ -> em"

        testCase "a [text](url) link becomes plain text" <| fun _ ->
            let input = "See [Open Library](https://openlibrary.org/works/OL1W) for more."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>See Open Library for more.</p>" "Link syntax drops to its text, no anchor, no URL"

        testCase "a reference-style [text][ref] link becomes plain text" <| fun _ ->
            let input = "See [Open Library][1] for more."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>See Open Library for more.</p>" "Reference-style link syntax also drops to its text"

        testCase "a bare <https://...> autolink is dropped" <| fun _ ->
            let input = "Visit <https://openlibrary.org> today."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>Visit  today.</p>" "The autolink is removed entirely, not converted to text or a link"

        testCase "a `- ` list becomes <ul><li>" <| fun _ ->
            let input = "- one\n- two\n- three"
            Expect.equal (OpenLibrary.descriptionToHtml input) "<ul><li>one</li><li>two</li><li>three</li></ul>" "-  items become an unordered list"

        testCase "a `1. ` list becomes <ol><li>" <| fun _ ->
            let input = "1. one\n2. two"
            Expect.equal (OpenLibrary.descriptionToHtml input) "<ol><li>one</li><li>two</li></ol>" "Numbered items become an ordered list"

        testCase "a #-heading becomes <p><strong>...</strong></p>" <| fun _ ->
            let input = "# Great Book\n\nThe blurb."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p><strong>Great Book</strong></p><p>The blurb.</p>" "Heading text is bolded and wrapped in its own <p>, not rendered as a real heading tag (not in the allowlist)"

        testCase "\\[2/2\\] unescaping to [2/2]" <| fun _ ->
            let input = "Split into parts \\[2/2\\] of the series."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>Split into parts [2/2] of the series.</p>" "Escaped brackets unescape to their literal characters, not link syntax"

        testCase "\\* and \\_ unescape to their literal characters, not emphasis" <| fun _ ->
            let input = "A literal \\*asterisk\\* and \\_underscore\\_ here."
            Expect.equal (OpenLibrary.descriptionToHtml input) "<p>A literal *asterisk* and _underscore_ here.</p>" "Escaped emphasis markers are never interpreted as emphasis"

        // The exact shape `.agentheim`'s live-evidence note describes: a
        // blurb, a blank line, a `---` rule, a blank line, a `**Contains**`
        // heading, a blank line, then eight `- [title](url)` bullets (their
        // link text carrying `\[1/2\]`/`\[2/2\]` escapes) -- everything from
        // the rule onward is editorial appendix, never rendered.
        testCase "the exact Lord of the Rings trailer: only the blurb's <p> blocks survive, no Contains/[/https://" <| fun _ ->
            let input =
                "One of the most influential works of the 20th century, The Lord of the Rings is an epic set in the fictional universe of Middle-earth.\n\n" +
                "This work includes six volumes of a trilogy told across three books.\n\n" +
                "---\n\n" +
                "**Contains**\n\n" +
                "- [The Fellowship of the Ring \\[1/2\\]](https://openlibrary.org/works/OL27479W)\n" +
                "- [The Fellowship of the Ring \\[2/2\\]](https://openlibrary.org/works/OL27480W)\n" +
                "- [The Two Towers \\[1/2\\]](https://openlibrary.org/works/OL27482W)\n" +
                "- [The Two Towers \\[2/2\\]](https://openlibrary.org/works/OL27483W)\n" +
                "- [The Return of the King \\[1/2\\]](https://openlibrary.org/works/OL27484W)\n" +
                "- [The Return of the King \\[2/2\\]](https://openlibrary.org/works/OL27485W)\n" +
                "- [Unfinished Tales](https://openlibrary.org/works/OL1234567W)\n" +
                "- [The Silmarillion](https://openlibrary.org/works/OL675783W)"
            let result = OpenLibrary.descriptionToHtml input
            let expected =
                "<p>One of the most influential works of the 20th century, The Lord of the Rings is an epic set in the fictional universe of Middle-earth.</p>" +
                "<p>This work includes six volumes of a trilogy told across three books.</p>"
            Expect.equal result expected "Only the blurb's two <p> blocks survive"
            Expect.isFalse (result.Contains("Contains")) "No Contains heading"
            Expect.isFalse (result.Contains("[")) "No literal bracket left over"
            Expect.isFalse (result.Contains("https://")) "No URL left over"

        testCase "a description with no Markdown syntax matches DescriptionSanitizer.sanitize's own blank-line-split <p> output" <| fun _ ->
            let input = "Paragraph one.\n\nParagraph two."
            let expected = DescriptionSanitizer.sanitize "<p>Paragraph one.</p><p>Paragraph two.</p>"
            Expect.equal (OpenLibrary.descriptionToHtml input) expected "Plain-prose works are unchanged in substance"

        testCase "stray HTML inside the Markdown is allowlisted by DescriptionSanitizer.sanitize, not a second allowlist" <| fun _ ->
            let input = "Text with <script>alert(1)</script> and <a href=\"http://x\">a link</a> embedded."
            let result = OpenLibrary.descriptionToHtml input
            Expect.isFalse (result.Contains("<script")) "No <script> tag survives"
            Expect.isFalse (result.Contains("<a ")) "No <a> tag survives"
    ]
    |> testSequenced
