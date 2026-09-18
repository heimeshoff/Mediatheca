module Mediatheca.Tests.AudibleLibrarySyncTests

/// integration-jjvg2 (ADR-0074/ADR-0076/ADR-0026): "Import Audible library"
/// (`Api.importAudibleLibrary`) creates a Book per library title (matched by
/// ASIN) and observes reading progress for every item with one; the "Audible
/// progress sync" scheduled job (`AudibleSync.runProgressSync`) re-observes
/// KNOWN books only and never creates one; `Audible.getLibrary` pages until a
/// short page; a rejected auth file ends the job run `error` (never
/// `Skipped`), while no auth file at all IS `Skipped`. No live Audible call
/// -- every request goes through a stub `HttpMessageHandler`.

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

type private AsyncStubHandler(respond: HttpRequestMessage -> Async<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Async.StartAsTask(respond request)

let private jsonResponse (status: HttpStatusCode) (json: string) =
    let resp = new HttpResponseMessage(status)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private fakeCoverBytes = Array.create 2048 (byte 0xFF)

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn
    Administration.initializeJobRuns conn

let private allProjectionHandlers = [ BookProjection.handler ]

let private withTempImageDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-audible-sync-test-images-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

// ── Library item / response JSON builders (no trailing-comma footguns) ──

let private libraryItemJson (asin: string) (title: string) (percentComplete: float option) (isFinished: bool) (runtimeMinutes: int option) : string =
    let required =
        [ sprintf "\"asin\": \"%s\"" asin
          sprintf "\"title\": \"%s\"" title
          sprintf "\"authors\": [{\"name\": \"Author %s\"}]" asin
          "\"narrators\": []"
          sprintf "\"is_finished\": %s" (if isFinished then "true" else "false")
          sprintf "\"product_images\": {\"500\": \"https://example.com/cover-%s.jpg\"}" asin
          "\"release_date\": \"2020-01-01\""
          sprintf "\"publisher_summary\": \"Description of %s\"" title ]
    let optionalPercent =
        percentComplete
        // Always with a decimal point ("0.0", "42.0") -- the wire shape
        // Audible really sends. A plain ToString gave "0"/"42", which an
        // int decoder accepts too, and hid the eager `Decode.int` fallback
        // that failed every real library on its first `0.0`.
        |> Option.map (fun p -> sprintf "\"percent_complete\": %s" (p.ToString("0.0###", Globalization.CultureInfo.InvariantCulture)))
        |> Option.toList
    let optionalRuntime =
        runtimeMinutes
        |> Option.map (fun r -> sprintf "\"runtime_length_min\": %d" r)
        |> Option.toList
    "{" + String.concat ", " (required @ optionalPercent @ optionalRuntime) + "}"

let private libraryResponseJson (items: string list) : string =
    sprintf "{\"items\": [%s]}" (String.concat ", " items)

let private authFile : Audible.AudibleAuthFile =
    { RefreshToken = "refresh-token"
      AdpToken = "adp"
      DevicePrivateKey = "priv"
      LocaleCode = "us"
      CustomerName = "Marco H"
      CustomerUserId = None }

let private configuredAudibleConfig : Audible.AudibleConfig =
    { AuthFile = Some authFile; Marketplace = "us"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None }

let private noAuthFileConfig : Audible.AudibleConfig =
    { AuthFile = None; Marketplace = "us"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None }

/// Dispatches `/auth/token` (always a fresh minted token) and `/1.0/library`
/// (routed by `page=`, defaulting to page 1), plus Audnexus/cover URLs so
/// neither path throws. `requestedUrls` records every request for the
/// paging assertion.
let private httpClientForLibrary (pagesByNumber: Map<int, string>) : HttpClient * (unit -> string list) =
    let requestedUrls = Collections.Generic.List<string>()
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                requestedUrls.Add(url)
                if url.Contains("/auth/token") then
                    return jsonResponse HttpStatusCode.OK """{"access_token": "minted-token", "expires_in": 3600}"""
                elif url.Contains("/1.0/library") then
                    let pageMatch = Text.RegularExpressions.Regex.Match(url, "page=(\\d+)")
                    let page = if pageMatch.Success then int pageMatch.Groups.[1].Value else 1
                    match pagesByNumber.TryFind page with
                    | Some body -> return jsonResponse HttpStatusCode.OK body
                    | None -> return jsonResponse HttpStatusCode.OK (libraryResponseJson [])
                elif url.Contains("api.audnex.us") then
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                else
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
            })
    new HttpClient(handler), (fun () -> requestedUrls |> List.ofSeq)

/// integration-dtdbb (ADR-0082): like `httpClientForLibrary`, but also
/// dispatches `/1.0/content/{asin}/metadata` (the last-position-heard
/// endpoint) per `metadataResponses` (`asin -> status, body`); an ASIN with
/// no entry there gets a plain `status: "DoesNotExist"` 200. Every metadata
/// URL hit is recorded (by ASIN) for the "exactly once" / "never called"
/// assertions.
let private httpClientForImportWithMetadata (libraryFixture: string) (metadataResponses: Map<string, HttpStatusCode * string>) : HttpClient * (unit -> string list) =
    let metadataCalls = Collections.Generic.List<string>()
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                if url.Contains("/auth/token") then
                    return jsonResponse HttpStatusCode.OK """{"access_token": "minted-token", "expires_in": 3600}"""
                elif url.Contains("/1.0/library") then
                    return jsonResponse HttpStatusCode.OK libraryFixture
                elif url.Contains("/1.0/content/") && url.Contains("/metadata") then
                    let asinMatch = Text.RegularExpressions.Regex.Match(url, "/1\\.0/content/([^/]+)/metadata")
                    let asin = if asinMatch.Success then asinMatch.Groups.[1].Value else ""
                    metadataCalls.Add(asin)
                    match metadataResponses.TryFind asin with
                    | Some (status, body) -> return jsonResponse status body
                    | None -> return jsonResponse HttpStatusCode.OK """{"last_position_heard": {"status": "DoesNotExist"}}"""
                elif url.Contains("api.audnex.us") then
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                else
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
            })
    new HttpClient(handler), (fun () -> metadataCalls |> List.ofSeq)

/// `/1.0/library` always 401s (any page); `/auth/token` always mints
/// successfully -- exercises the single-refresh-and-retry-then-give-up path.
let private httpClientAlwaysUnauthorized () : HttpClient =
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                if url.Contains("/auth/token") then
                    return jsonResponse HttpStatusCode.OK """{"access_token": "minted-token", "expires_in": 3600}"""
                elif url.Contains("/1.0/library") then
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                else
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
            })
    new HttpClient(handler)

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) (imageBasePath: string) (getAudibleConfig: unit -> Audible.AudibleConfig) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (Qbittorrent.createHttpClient ())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> ({ UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" } : OpenLibrary.OpenLibraryConfig))
        getAudibleConfig
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        imageBasePath
        allProjectionHandlers

let private totalEventCount (conn: SqliteConnection) : int64 =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(*) FROM events"
    cmd.ExecuteScalar() :?> int64

let private noopPersistToken (_: Audible.AudibleAccessToken) : unit = ()

[<Tests>]
let importAudibleLibraryTests =
    testList "Api.importAudibleLibrary (integration-jjvg2)" [

        testCase "a 3-item fixture (0%, 42%, finished) creates 3 books, each recording a PRIOR (integration-dtdbb, ADR-0082); a second import is a total no-op" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let itemZero = libraryItemJson "A1" "Book A" (Some 0.0) false (Some 600)
                let itemMid = libraryItemJson "B1" "Book B" (Some 42.0) false (Some 600)
                let itemFinished = libraryItemJson "C1" "Book C" None true (Some 600)
                let fixture = libraryResponseJson [ itemZero; itemMid; itemFinished ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal result.Total 3 "3 items in the fixture"
                Expect.equal result.Created 3 "3 new books created"
                Expect.equal result.AlreadyKnown 0 "nothing pre-existed"
                // Every book here has no Audible book_progress row yet, so
                // ALL THREE record a prior (ADR-0082 §2: "a source's
                // first-ever reported position" -- even 0%, since it is a
                // starting position, not a delta) -- unlike a plain
                // Observe_reading_progress, a 0% baseline is not a no-op.
                Expect.equal result.ProgressObserved 3 "all three items -- including 0%% -- record a first-ever prior"
                Expect.equal (result.PriorsFromAudible + result.PriorsToday) 3 "every recorded prior is counted as either from-Audible or dated-today"

                let slugA = BookProjection.findByExternalId db.Connection (AudibleAsin "A1") |> Option.get
                let slugB = BookProjection.findByExternalId db.Connection (AudibleAsin "B1") |> Option.get
                let slugC = BookProjection.findByExternalId db.Connection (AudibleAsin "C1") |> Option.get

                Expect.equal (BookProjection.getBySlug db.Connection slugA |> Option.get).Status BookStatus.Backlog "0%% stays Backlog"
                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.Backlog "42%% is a PRIOR, so it never promotes to InFocus (ADR-0082)"
                Expect.equal (BookProjection.getBySlug db.Connection slugC |> Option.get).Status BookStatus.Finished "is_finished with no percent lands Finished (treated as 100%%) even as a prior"

                let progressRowCount =
                    [ slugA; slugB; slugC ]
                    |> List.sumBy (fun slug -> (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory |> List.length)
                Expect.equal progressRowCount 3 "exactly 3 book_progress rows total -- one prior per book, including 0%%"

                let eventsBeforeSecondImport = totalEventCount db.Connection
                let second =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on re-run, got Error %s" e
                Expect.equal second.Created 0 "re-run creates nothing"
                Expect.equal second.AlreadyKnown 3 "all three now known"
                Expect.equal second.ProgressObserved 0 "every book already carries an Audible prior row, so the re-run falls through to a same-percent-per-source no-op"
                Expect.equal (totalEventCount db.Connection) eventsBeforeSecondImport "the second import appends ZERO events")

        testCase "an unknown ASIN (404 from the catalog fallback path) does not abort the run -- errors are collected, not thrown" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "OK1" "Book OK" (Some 10.0) false (Some 300) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)
                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Ok r -> Expect.equal r.Created 1 "the one valid item is still created despite no other items"
                | Error e -> failtestf "Expected Ok, got Error %s" e)

        testCase "an empty (200, zero items) library response reports 0 items plainly and does NOT clear a pre-set audible_last_error (ADR-0068)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                SettingsStore.setSetting db.Connection "audible_last_error" "audible auth file rejected: a prior run"
                let emptyFixture = libraryResponseJson []
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, emptyFixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal result.Total 0 "an empty library reports 0 items plainly, not an error"
                match SettingsStore.getSetting db.Connection "audible_last_error" with
                | Some msg -> Expect.stringContains msg "a prior run" "an inconclusive empty response must NOT clear a standing notice"
                | None -> failtest "Expected the pre-set audible_last_error to survive an empty library response")

        testCase "the import path stamps a fresh book's PRIOR's ObservedOn as the local calendar date when the metadata call yields no last-listened date" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "D1" "Book D" (Some 30.0) false (Some 400) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                api.importAudibleLibrary () |> Async.RunSynchronously |> ignore

                let slugD = BookProjection.findByExternalId db.Connection (AudibleAsin "D1") |> Option.get
                let events =
                    EventStore.readStream db.Connection (Books.streamId slugD)
                    |> List.choose Books.Serialization.fromStoredEvent
                let observation =
                    events
                    |> List.tryPick (function
                        | Books.Prior_reading_progress_recorded data -> Some data
                        | _ -> None)
                match observation with
                | Some data -> Expect.equal data.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "the import stamps the local calendar date, matching AudibleSync.runProgressSync"
                | None -> failtest "Expected a Reading_progress_observed event")

        testCase "a library item lacking publisher_summary falls back to a sanitized Audnexus description in book_metadata_cache (books-nvnyk)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let itemWithoutDescription =
                    let fields =
                        [ "\"asin\": \"ND1\""
                          "\"title\": \"Book No Desc\""
                          "\"authors\": [{\"name\": \"Author ND1\"}]"
                          "\"narrators\": []"
                          "\"is_finished\": false"
                          "\"product_images\": {\"500\": \"https://example.com/cover-ND1.jpg\"}"
                          "\"release_date\": \"2020-01-01\""
                          "\"percent_complete\": 10.0" ]
                    "{" + String.concat ", " fields + "}"
                let fixture = libraryResponseJson [ itemWithoutDescription ]
                let handler =
                    new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("/auth/token") then
                                return jsonResponse HttpStatusCode.OK """{"access_token": "minted-token", "expires_in": 3600}"""
                            elif url.Contains("/1.0/library") then
                                return jsonResponse HttpStatusCode.OK fixture
                            elif url.Contains("api.audnex.us") then
                                return jsonResponse HttpStatusCode.OK """{"summary": "<p>First.</p><p>Second.</p><script>alert(1)</script>"}"""
                            else
                                let resp = new HttpResponseMessage(HttpStatusCode.OK)
                                resp.Content <- new ByteArrayContent(fakeCoverBytes)
                                return resp
                        })
                use httpClient = new HttpClient(handler)
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Ok r -> Expect.equal r.Created 1 "the one item is still created"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin "ND1") |> Option.get
                let metadata = MetadataCache.tryGetBookMetadata db.Connection slug
                match metadata.Description with
                | Some d ->
                    Expect.stringContains d "<p>" "sanitized Audnexus fallback description keeps allowlisted <p>"
                    Expect.isFalse (d.Contains "<script") "no <script> tag survives sanitization"
                    Expect.isFalse (d.Contains "onclick") "no attributes survive sanitization"
                | None -> failtest "Expected the Audnexus fallback to populate a description")
    ]
    |> testSequenced

[<Tests>]
let audibleProgressSyncJobTests =
    testList "AudibleSync.runProgressSync -- the scheduled job (integration-jjvg2)" [

        testCase "the job never creates a book -- an unknown ASIN is counted Unmatched" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "UNKNOWN1" "Mystery Book" (Some 50.0) false (Some 400) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Unmatched 1 "the unknown ASIN is counted as unmatched"
                    Expect.equal r.Observed 0 "nothing was observed"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.isEmpty (BookProjection.getAll db.Connection) "no book was created by the job")

        testCase "changing a known book's percent from 42%% to 55%% (after the import's own prior) appends the observation AND promotes to InFocus" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let firstFixture = libraryResponseJson [ libraryItemJson "B1" "Book B" (Some 42.0) false (Some 600) ]
                let httpClient1, _ = httpClientForLibrary (Map.ofList [ 1, firstFixture ])
                let api = createApi db.Factory httpClient1 imageBasePath (fun () -> configuredAudibleConfig)
                api.importAudibleLibrary () |> Async.RunSynchronously |> ignore

                let slugB = BookProjection.findByExternalId db.Connection (AudibleAsin "B1") |> Option.get
                // integration-dtdbb, ADR-0082: the import's own first-ever
                // percent for this book is a PRIOR, which never promotes to
                // InFocus -- only the SYNC's ordinary Observe_reading_progress
                // (below) can do that.
                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.Backlog "sanity: the import's prior at 42%% does not promote to InFocus"
                let eventsBeforeJob = EventStore.readStream db.Connection (Books.streamId slugB) |> List.length

                let secondFixture = libraryResponseJson [ libraryItemJson "B1" "Book B" (Some 55.0) false (Some 600) ]
                let httpClient2, _ = httpClientForLibrary (Map.ofList [ 1, secondFixture ])
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient2 (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "one book observed"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let eventsAfterJob = EventStore.readStream db.Connection (Books.streamId slugB)
                Expect.equal (List.length eventsAfterJob) (eventsBeforeJob + 2) "TWO new events appended: the observation, then the Backlog -> InFocus promotion it triggers"

                let newEvents = eventsAfterJob |> List.skip eventsBeforeJob |> List.choose Books.Serialization.fromStoredEvent
                match newEvents with
                | [ Books.Reading_progress_observed data; Books.Book_status_changed (BookStatus.InFocus, _) ] ->
                    Expect.equal data.Percent 55 "the new percent"
                    Expect.equal data.Source ProgressSource.Audible "sourced from Audible"
                    Expect.equal data.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "observed today"
                | other -> failtestf "Expected [Reading_progress_observed; Book_status_changed InFocus], got %A" other

                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.InFocus "the ordinary observation promotes Backlog -> InFocus once the prior's baseline (42%%) is exceeded")

        testCase "paging: a fixture returning 1000 then 3 items makes exactly two requests, page=1 and page=2" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                let fullPageItems = [ for i in 1 .. 1000 -> libraryItemJson (sprintf "FULL%04d" i) (sprintf "Full %d" i) (Some 0.0) false None ]
                let page1 = libraryResponseJson fullPageItems
                let page2 = libraryResponseJson [ libraryItemJson "TAIL1" "Tail 1" (Some 0.0) false None; libraryItemJson "TAIL2" "Tail 2" (Some 0.0) false None; libraryItemJson "TAIL3" "Tail 3" (Some 0.0) false None ]
                let httpClient, requestedUrls = httpClientForLibrary (Map.ofList [ 1, page1; 2, page2 ])

                let result =
                    Audible.getLibrary httpClient "api.audible.com" "some-token"
                    |> Async.RunSynchronously

                match result with
                | Ok items -> Expect.equal (List.length items) 1003 "1000 + 3 items combined across both pages"
                | Error e -> failtestf "Expected Ok, got Error %A" e

                let libraryRequests = requestedUrls () |> List.filter (fun u -> u.Contains("/1.0/library"))
                Expect.equal (List.length libraryRequests) 2 "exactly two /1.0/library requests"
                Expect.isTrue (libraryRequests |> List.exists (fun u -> u.Contains("page=1"))) "one request for page=1"
                Expect.isTrue (libraryRequests |> List.exists (fun u -> u.Contains("page=2"))) "one request for page=2")

        testCase "no auth file at all is Skipped, never touching audible_last_error" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                let jobLock = new SemaphoreSlim(1, 1)
                let httpClient, _ = httpClientForLibrary Map.empty

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> noAuthFileConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Error msg -> Expect.isFalse (msg.StartsWith(Audible.authFileRejectedPrefix)) "the 'not configured' message never carries the rejection prefix"
                | Ok r -> failtestf "Expected Error, got Ok %A" r
                Expect.isNone (SettingsStore.getSetting db.Connection "audible_last_error") "no standing notice for a simple config gap")

        testCase "a 401 after the single refresh-and-retry sets audible_last_error with the fixed prefix" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                let jobLock = new SemaphoreSlim(1, 1)
                let httpClient = httpClientAlwaysUnauthorized ()

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Error msg -> Expect.isTrue (msg.StartsWith(Audible.authFileRejectedPrefix)) (sprintf "Expected the fixed rejection prefix, got: %s" msg)
                | Ok r -> failtestf "Expected Error, got Ok %A" r

                match SettingsStore.getSetting db.Connection "audible_last_error" with
                | Some msg -> Expect.isTrue (msg.StartsWith(Audible.authFileRejectedPrefix)) "persisted last-error carries the fixed prefix"
                | None -> failtest "Expected audible_last_error to be persisted")

        testCase "an empty (200, zero items) library response reports 0 items plainly and does NOT clear a pre-set audible_last_error (ADR-0068)" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                SettingsStore.setSetting db.Connection "audible_last_error" "audible auth file rejected: a prior run"
                let jobLock = new SemaphoreSlim(1, 1)
                let emptyFixture = libraryResponseJson []
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, emptyFixture ])

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Observed 0 "an empty library observes nothing"
                    Expect.equal r.Unmatched 0 "an empty library reports 0 items plainly, not an error"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                match SettingsStore.getSetting db.Connection "audible_last_error" with
                | Some msg -> Expect.stringContains msg "a prior run" "an inconclusive empty response must NOT clear a standing notice"
                | None -> failtest "Expected the pre-set audible_last_error to survive an empty library response")

        testCase "a book with no Audible progress row, synced for the FIRST TIME by the JOB (not the import), gets a plain observation and never a prior -- zero getLastPositionHeard calls (ADR-0082 §2, integration-dtdbb)" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                // Seed a known book matched by ASIN but with NO Audible
                // book_progress row at all (no Reading_progress_observed,
                // no Prior_reading_progress_recorded) -- e.g. a title added
                // by a source other than the Audible import.
                let slug = "job-first-sync-book"
                let addedData : Books.BookAddedData = {
                    Title = "Job First Sync Book"; Authors = [ "Author" ]; Year = None; CoverRef = None
                    Subjects = []; Format = BookFormat.Audiobook; ExternalIds = [ AudibleAsin "N1" ]
                }
                match EventStore.appendToStream db.Connection (Books.streamId slug) -1L [ Books.Serialization.toEventData (Books.Book_added_to_library addedData) ] with
                | EventStore.Success _ -> ()
                | EventStore.ConcurrencyConflict _ -> failtest "unexpected concurrency conflict seeding the fixture"
                for handler in allProjectionHandlers do Projection.runProjection db.Connection handler

                Expect.isEmpty (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory "sanity: no Audible book_progress row exists yet"

                let fixture = libraryResponseJson [ libraryItemJson "N1" "Job First Sync Book" (Some 35.0) false (Some 500) ]
                let metadataResponses =
                    Map.ofList [
                        "N1", (HttpStatusCode.OK, """{"last_position_heard": {"last_updated": "2024-05-05", "position_ms": 1000, "status": "Exists"}}""")
                    ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "the book's first-ever percent is observed by the job"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.isEmpty (metadataCalls ()) "the job never calls getLastPositionHeard, even for a book with no prior Audible row -- priors are the import's business only (ADR-0082 §2)"

                let events = EventStore.readStream db.Connection (Books.streamId slug) |> List.choose Books.Serialization.fromStoredEvent
                match events |> List.filter (function Books.Reading_progress_observed _ | Books.Prior_reading_progress_recorded _ -> true | _ -> false) with
                | [ Books.Reading_progress_observed data ] ->
                    Expect.equal data.Percent 35 "the observed percent"
                    Expect.equal data.Source ProgressSource.Audible "sourced from Audible"
                | other -> failtestf "Expected exactly one Reading_progress_observed and no Prior_reading_progress_recorded on the raw stream, got %A" other

                let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                match detail.ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Kind ProgressKind.Observed "the job's sync writes a plain observation, never a prior"
                    Expect.equal row.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "dated to the sync day, not Audible's (uncalled) last-listened day"
                | other -> failtestf "Expected exactly one progress row, got %A" other)
    ]
    |> testSequenced

[<Tests>]
let audibleProgressSyncJobRegistrationTests =
    testList "Audible progress sync job registration (integration-jjvg2, ADR-0026)" [

        testCase "\"Audible progress sync\" is a registered JobSpec whose manual run is recorded, and a rejected auth file resolves the run to 'error' (never 'skipped')" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                let jobLock = new SemaphoreSlim(1, 1)
                let httpClient = httpClientAlwaysUnauthorized ()

                // Mirrors Composition.fs's own "Audible progress sync" Run
                // body verbatim: a rejected auth file `failwith`s (caught by
                // `tryStartJob` -> `Fail` -> 'error'), never `Skipped`.
                let spec : ScheduledJobs.JobSpec = {
                    Name = "Audible progress sync"
                    Hour = 5
                    Run = fun () ->
                        async {
                            match! AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers with
                            | Ok result -> return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = AudibleSync.formatResult result } : ScheduledJobs.JobRunOutcome)
                            | Error err when err.StartsWith(Audible.authFileRejectedPrefix) -> return failwith err
                            | Error err -> return ({ Disposition = ScheduledJobs.JobDisposition.Skipped; Summary = err } : ScheduledJobs.JobRunOutcome)
                        }
                }

                let recorder = Administration.makeJobRunRecorder db.Connection jobLock
                let adminApi = Administration.create db.Factory "test-fixtures-do-not-exist/mediatheca.db" "test-fixtures-do-not-exist/images" allProjectionHandlers [ spec ] recorder (Administration.makeGuards ())

                let statuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                Expect.exists statuses (fun s -> s.JobName = "Audible progress sync") "the job is listed in the Jobs section"

                match adminApi.runJobNow "Audible progress sync" |> Async.RunSynchronously with
                | RunJobStarted _ -> ()
                | RunJobRejected -> failtest "Expected the manual run to start"

                let mutable attempts = 0
                let mutable settled = false
                while not settled && attempts < 50 do
                    let statuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                    match statuses |> List.tryFind (fun s -> s.JobName = "Audible progress sync") |> Option.bind (fun s -> s.LastRun) with
                    | Some run when run.Status <> RunStatusRunning -> settled <- true
                    | _ ->
                        Thread.Sleep(20)
                        attempts <- attempts + 1

                let finalStatuses = adminApi.getJobStatuses () |> Async.RunSynchronously
                let jobStatus = finalStatuses |> List.find (fun s -> s.JobName = "Audible progress sync")
                match jobStatus.LastRun with
                | Some run ->
                    Expect.equal run.Trigger "manual" "the \"Sync progress now\" trigger is recorded as manual"
                    Expect.equal run.Status RunStatusError "a rejected auth file resolves the run to 'error', never 'skipped'"
                | None -> failtest "Expected a recorded run")
    ]
    |> testSequenced

[<Tests>]
let audibleSyncStatusPersistenceTests =
    testList "IMediathecaApi.getAudibleSyncStatus reads persisted (not in-memory) state (integration-jjvg2)" [

        testCase "after an import, getAudibleSyncStatus reports the persisted last-import summary; after a sync, the persisted last-sync time/result" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "P1" "Persisted Book" (Some 20.0) false (Some 300) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                api.importAudibleLibrary () |> Async.RunSynchronously |> ignore
                let afterImport = api.getAudibleSyncStatus () |> Async.RunSynchronously
                Expect.isSome afterImport.LastImportResult "the import result is persisted via SettingsStore"
                Expect.isNone afterImport.LastSync "no sync has run yet"

                let jobLock = new SemaphoreSlim(1, 1)
                AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let afterSync = api.getAudibleSyncStatus () |> Async.RunSynchronously
                Expect.isSome afterSync.LastSync "the sync time is persisted via SettingsStore"
                Expect.isSome afterSync.LastSyncResult "the sync result summary is persisted via SettingsStore")
    ]

/// integration-dtdbb (ADR-0082): `AudibleSync.observationFor`'s pure
/// last-listened-date/position decision, exercised directly (no HTTP, no DB).
[<Tests>]
let observationForTests =
    let baseItem : Audible.AudibleLibraryItem =
        { Asin = "X1"; Title = "Book X"; Authors = []; Narrators = []; RuntimeMinutes = Some 600
          PercentComplete = Some 50.0; IsFinished = false; PurchaseDate = None; CoverUrl = None
          SeriesName = None; SeriesPosition = None; ReleaseDate = None; Description = None }
    testList "AudibleSync.observationFor (integration-dtdbb, ADR-0082)" [

        testCase "lastListened = None (the nightly sync's own call shape): ObservedOn defaults to today, Position is the percent x runtime estimate" <| fun _ ->
            match AudibleSync.observationFor baseItem "2026-09-18" None with
            | Some data ->
                Expect.equal data.ObservedOn "2026-09-18" "defaults to today when there's no last-listened info at all"
                Expect.equal data.Position (Some (Minutes (300, Some 600))) "percent x runtime estimate: 50%% of 600 = 300"
            | None -> failtest "Expected Some data"

        testCase "LastUpdatedOn = Some d: ObservedOn is d, not today" <| fun _ ->
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = None }
            match AudibleSync.observationFor baseItem "2026-09-18" (Some lastListened) with
            | Some data -> Expect.equal data.ObservedOn "2023-09-23" "prefers the source's own last-listened day over today"
            | None -> failtest "Expected Some data"

        testCase "LastUpdatedOn = None inside a Some LastPositionHeard (a metadata call that found nothing): ObservedOn falls back to today" <| fun _ ->
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = None; PositionMs = None }
            match AudibleSync.observationFor baseItem "2026-09-18" (Some lastListened) with
            | Some data -> Expect.equal data.ObservedOn "2026-09-18" "no last-listened date known, falls back to today exactly like the None case"
            | None -> failtest "Expected Some data"

        testCase "PositionMs = Some 896068L with a known runtime: Position is Minutes (14, Some runtime), preferred over the percent estimate" <| fun _ ->
            let item = { baseItem with RuntimeMinutes = Some 999; PercentComplete = Some 10.0 }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = Some 896068L }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data -> Expect.equal data.Position (Some (Minutes (14, Some 999))) "896068ms / 60000 = 14 minutes (int division), NOT the 10%% x 999 estimate"
            | None -> failtest "Expected Some data"

        testCase "PositionMs present but no runtime known: Position is None (mirrors the existing percent-estimate guard)" <| fun _ ->
            let item = { baseItem with RuntimeMinutes = None }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = Some 896068L }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data -> Expect.isNone data.Position "no runtime -- no Position, regardless of PositionMs"
            | None -> failtest "Expected Some data"
    ]

/// integration-dtdbb (ADR-0082): `Api.importAudibleLibraryImpl`'s prior
/// recording, its degrade-on-failure behaviour, and its legacy repair --
/// the acceptance criteria this task's own "What"/"Acceptance criteria"
/// sections describe. No live Audible call -- every request (including the
/// metadata endpoint) goes through `httpClientForImportWithMetadata`.
[<Tests>]
let importPriorRecordingTests =
    testList "Api.importAudibleLibrary priors (integration-dtdbb, ADR-0082)" [

        testCase "a fresh book (no Audible row yet) calls getLastPositionHeard exactly once; the resulting row is kind = prior, dated from Audible, and finished_at matches when the item is finished" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "F1" "Book F" None true (Some 600) ]
                let metadataResponses =
                    Map.ofList [
                        "F1", (HttpStatusCode.OK, """{"last_position_heard": {"last_updated": "2023-09-23 21:03:18.228", "position_ms": 896068, "status": "Exists"}}""")
                    ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (metadataCalls () |> List.filter (fun a -> a = "F1") |> List.length) 1 "getLastPositionHeard called exactly once for this ASIN"
                Expect.equal result.PriorsFromAudible 1 "the prior was dated from Audible's own last_position_heard"
                Expect.equal result.PriorsToday 0 "not a today-dated fallback"

                let slugF = BookProjection.findByExternalId db.Connection (AudibleAsin "F1") |> Option.get
                let detail = BookProjection.getBySlug db.Connection slugF |> Option.get
                Expect.equal detail.ProgressHistory (
                    [ { ObservedOn = "2023-09-23"; Source = ProgressSource.Audible; Percent = 100; Position = Some (Minutes (14, Some 600)); Kind = ProgressKind.Prior } ]
                ) "one prior row, dated/positioned from Audible's own metadata"
                Expect.equal detail.Status BookStatus.Finished "is_finished lands Finished even as a prior"
                Expect.equal detail.FinishedAt (Some "2023-09-23") "finished_at matches the metadata's own last-listened date")

        testCase "a book that already has an Audible row makes no metadata call and appends an ordinary observation" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let firstFixture = libraryResponseJson [ libraryItemJson "G1" "Book G" (Some 20.0) false (Some 600) ]
                let httpClient1, metadataCalls1 = httpClientForImportWithMetadata firstFixture Map.empty
                let api1 = createApi db.Factory httpClient1 imageBasePath (fun () -> configuredAudibleConfig)
                api1.importAudibleLibrary () |> Async.RunSynchronously |> ignore
                Expect.equal (metadataCalls1 () |> List.length) 1 "sanity: the first (prior-recording) import called the metadata endpoint once"

                let secondFixture = libraryResponseJson [ libraryItemJson "G1" "Book G" (Some 40.0) false (Some 600) ]
                let httpClient2, metadataCalls2 = httpClientForImportWithMetadata secondFixture Map.empty
                let api2 = createApi db.Factory httpClient2 imageBasePath (fun () -> configuredAudibleConfig)
                let result =
                    match api2.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (metadataCalls2 () |> List.length) 0 "a book that already carries an Audible row never calls the metadata endpoint again"
                Expect.equal result.ProgressObserved 1 "the percent change (20%% -> 40%%) appends an ordinary observation"
                Expect.equal result.PriorsFromAudible 0 "no new prior this run"
                Expect.equal result.PriorsToday 0 "no new prior this run"

                // Both imports run on the same calendar day in this test, so
                // ADR-0076 §2's existing same-day/same-source collapse (the
                // `book_progress` PK is (book_slug, observed_on, source))
                // upserts the ordinary observation over the prior's own row
                // -- an intentional, pre-existing behaviour this task does
                // not change, not a second row.
                let slugG = BookProjection.findByExternalId db.Connection (AudibleAsin "G1") |> Option.get
                let history = (BookProjection.getBySlug db.Connection slugG |> Option.get).ProgressHistory
                match history with
                | [ row ] ->
                    Expect.equal row.Kind ProgressKind.Observed "the plain Observe_reading_progress command's write wins the same-day collapse"
                    Expect.equal row.Percent 40 "the latest percent"
                | other -> failtestf "Expected exactly one (same-day-collapsed) progress row, got %A" other)

        testCase "a getLastPositionHeard failure (401) never surfaces as an import Error -- the book still gets a prior, dated today" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "H1" "Book H" (Some 15.0) false (Some 600) ]
                let metadataResponses = Map.ofList [ "H1", (HttpStatusCode.Unauthorized, "") ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok even though the metadata call 401s, got Error %s" e

                Expect.equal (metadataCalls () |> List.length) 1 "the metadata call was attempted"
                Expect.isEmpty result.Errors "a metadata-call failure is not an import item error"
                Expect.equal result.PriorsToday 1 "the prior falls back to today-dating"
                Expect.equal result.PriorsFromAudible 0 "no Audible-sourced date -- the call failed"

                let slugH = BookProjection.findByExternalId db.Connection (AudibleAsin "H1") |> Option.get
                let detail = BookProjection.getBySlug db.Connection slugH |> Option.get
                match detail.ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Kind ProgressKind.Prior "still a prior, just dated today"
                    Expect.equal row.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "falls back to today"
                | other -> failtestf "Expected exactly one progress row, got %A" other)

        testCase "legacy repair: a book whose only Audible row is kind=observation becomes a single re-dated prior after one import; a second run is a no-op" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                // Seed the "legacy" shape directly: a book created and its
                // Audible progress observed via the OLD code path (plain
                // Reading_progress_observed), predating priors entirely --
                // exactly what a real pre-books-d4wtc event log holds.
                let slug = "legacy-book"
                let addedData : Books.BookAddedData = {
                    Title = "Legacy Book"; Authors = [ "Author" ]; Year = None; CoverRef = None
                    Subjects = []; Format = BookFormat.Audiobook; ExternalIds = [ AudibleAsin "L1" ]
                }
                let legacyObservedOn = "2024-01-15"
                let progressData : Books.ReadingProgressObservedData = {
                    Percent = 60; Position = Some (Minutes (360, Some 600)); Source = ProgressSource.Audible
                    ObservedOn = legacyObservedOn; Finished = false
                }
                let legacyEvents =
                    [ Books.Book_added_to_library addedData
                      Books.Reading_progress_observed progressData
                      Books.Book_status_changed (BookStatus.InFocus, Some legacyObservedOn) ]
                let eventDataList = legacyEvents |> List.map Books.Serialization.toEventData
                match EventStore.appendToStream db.Connection (Books.streamId slug) -1L eventDataList with
                | EventStore.Success _ -> ()
                | EventStore.ConcurrencyConflict _ -> failtest "unexpected concurrency conflict seeding the legacy fixture"
                for handler in allProjectionHandlers do Projection.runProjection db.Connection handler

                Expect.equal (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory.[0].Kind ProgressKind.Observed "sanity: the seeded fixture is the legacy (pre-prior) shape"

                let fixture = libraryResponseJson [ libraryItemJson "L1" "Legacy Book" (Some 60.0) false (Some 600) ]
                let metadataResponses =
                    Map.ofList [
                        "L1", (HttpStatusCode.OK, """{"last_position_heard": {"last_updated": "2024-03-01 10:00:00.000", "position_ms": 21600000, "status": "Exists"}}""")
                    ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let api = createApi db.Factory httpClient "unused-image-dir" (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal result.Repaired 1 "the legacy observation-only row was repaired"
                Expect.equal result.AlreadyKnown 1 "the book is matched by ASIN, not re-created"

                let detailAfterRepair = BookProjection.getBySlug db.Connection slug |> Option.get
                Expect.equal detailAfterRepair.ProgressHistory (
                    [ { ObservedOn = "2024-03-01"; Source = ProgressSource.Audible; Percent = 60; Position = Some (Minutes (360, Some 600)); Kind = ProgressKind.Prior } ]
                ) "the legacy row is replaced by a single, correctly-dated prior"

                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length
                let second =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on re-run, got Error %s" e
                Expect.equal second.Repaired 0 "idempotent: a second run finds a prior, not a legacy row, so there is nothing left to repair"
                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "the second run appends zero events")

        testCase "legacy repair on an ALREADY-FINISHED book re-dates finished_at to Audible's last-listened day; a second run is a no-op" <| fun _ ->
            withTempImageDir (fun _ ->
                use db = TestDb.withTempDbFactory bootstrap
                // Seed the "legacy, already finished" shape directly: a book
                // finished via the OLD code path -- a plain
                // Reading_progress_observed at 100%% followed by the
                // Book_status_changed the old observation logic emitted --
                // predating priors entirely. This is the dominant real-world
                // repair case: a book Audible already reports as finished,
                // whose finished_at is stuck at the old import day.
                let slug = "legacy-finished-book"
                let addedData : Books.BookAddedData = {
                    Title = "Legacy Finished Book"; Authors = [ "Author" ]; Year = None; CoverRef = None
                    Subjects = []; Format = BookFormat.Audiobook; ExternalIds = [ AudibleAsin "M1" ]
                }
                let importDay = "2023-01-10"
                let progressData : Books.ReadingProgressObservedData = {
                    Percent = 100; Position = Some (Minutes (600, Some 600)); Source = ProgressSource.Audible
                    ObservedOn = importDay; Finished = false
                }
                let legacyEvents =
                    [ Books.Book_added_to_library addedData
                      Books.Reading_progress_observed progressData
                      Books.Book_status_changed (BookStatus.Finished, Some importDay) ]
                let eventDataList = legacyEvents |> List.map Books.Serialization.toEventData
                match EventStore.appendToStream db.Connection (Books.streamId slug) -1L eventDataList with
                | EventStore.Success _ -> ()
                | EventStore.ConcurrencyConflict _ -> failtest "unexpected concurrency conflict seeding the legacy fixture"
                for handler in allProjectionHandlers do Projection.runProjection db.Connection handler

                let beforeRepair = BookProjection.getBySlug db.Connection slug |> Option.get
                Expect.equal beforeRepair.ProgressHistory.[0].Kind ProgressKind.Observed "sanity: the seeded fixture is the legacy (pre-prior) shape"
                Expect.equal beforeRepair.Status BookStatus.Finished "sanity: the legacy fixture is already Finished"
                Expect.equal beforeRepair.FinishedAt (Some importDay) "sanity: finished_at is stuck at the old import day before repair"

                let fixture = libraryResponseJson [ libraryItemJson "M1" "Legacy Finished Book" None true (Some 600) ]
                let metadataResponses =
                    Map.ofList [
                        "M1", (HttpStatusCode.OK, """{"last_position_heard": {"last_updated": "2023-09-23 21:03:18.228", "position_ms": 36000000, "status": "Exists"}}""")
                    ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let api = createApi db.Factory httpClient "unused-image-dir" (fun () -> configuredAudibleConfig)

                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal result.Repaired 1 "the legacy observation-only row was repaired"

                let detailAfterRepair = BookProjection.getBySlug db.Connection slug |> Option.get
                match detailAfterRepair.ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Kind ProgressKind.Prior "the legacy row is replaced by a single prior"
                    Expect.equal row.ObservedOn "2023-09-23" "dated from Audible's own last-listened day"
                | other -> failtestf "Expected exactly one progress row, got %A" other
                Expect.equal detailAfterRepair.FinishedAt (Some "2023-09-23") "finished_at is RE-DATED to the last-listened day, not stuck at the old import day (the exact defect this task exists to fix)"

                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length
                let second =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on re-run, got Error %s" e
                Expect.equal second.Repaired 0 "idempotent: a second run finds a prior, not a legacy row"
                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "the second run appends zero events -- the re-finish Change_status is a no-op once the date already matches")
    ]
