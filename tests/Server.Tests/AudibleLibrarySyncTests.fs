module Mediatheca.Tests.AudibleLibrarySyncTests

/// integration-jjvg2 (ADR-0074/ADR-0076/ADR-0026), reversed for the nightly
/// sync's create path by integration-dvbjp (ADR-0082); minutes-based percent
/// added by integration-fn3yx (ADR-0086): "Import Audible library"
/// (`Api.importAudibleLibrary`) is now a ONE-TIME bootstrap -- creates a
/// Book per library title (matched by ASIN), records a PRIOR for any book
/// with no Audible row yet, stamps `audible_library_imported_at` after a
/// populated run, and refuses a second run once stamped. The "Audible
/// progress sync" scheduled job (`AudibleSync.runProgressSync`) now ALSO
/// creates a book for an unmatched ASIN (via the `createBook` function
/// parameter, `Api.createBookFromAudibleItem` in real wiring), then observes
/// it exactly like a matched book -- an ordinary observation, NEVER a prior.
/// integration-fn3yx: the sync now calls `Audible.getLastPositionHeard` for
/// EVERY item (matched, or one it just created) -- the library listing's own
/// `percent_complete` is no longer trusted as a position; the true
/// `position_ms` decides, and the percent is calculated from it whenever
/// both it and the item's own runtime are known. `Audible.getLibrary` pages
/// until a short page; a rejected auth file ends the job run `error` (never
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

/// integration-fn3yx (ADR-0086): the real wire shape --
/// `content_metadata.last_position_heard`, NOT a top-level field (the bug
/// this task fixes). `lastUpdated = None` omits the field entirely, matching
/// a genuine `status = "DoesNotExist"` payload that carries no date either.
let private lastPositionHeardJson (status: string) (lastUpdated: string option) (positionMs: int64 option) : string =
    let fields =
        [ Some (sprintf "\"status\": \"%s\"" status)
          lastUpdated |> Option.map (fun d -> sprintf "\"last_updated\": \"%s\"" d)
          positionMs |> Option.map (fun p -> sprintf "\"position_ms\": %d" p) ]
        |> List.choose id
    sprintf "{\"content_metadata\": {\"last_position_heard\": {%s}}}" (String.concat ", " fields)

/// A successful `Exists` metadata response naming an exact position in
/// minutes -- the test's own arithmetic stays in minutes, not raw
/// milliseconds (`minutes * 60000L` is exact int division's own inverse).
let private existsAt (lastUpdated: string) (minutes: int) : HttpStatusCode * string =
    HttpStatusCode.OK, lastPositionHeardJson "Exists" (Some lastUpdated) (Some (int64 minutes * 60000L))

let private doesNotExist : HttpStatusCode * string =
    HttpStatusCode.OK, lastPositionHeardJson "DoesNotExist" None None

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

/// integration-dtdbb (ADR-0082), used by both the import and (since
/// integration-fn3yx) the nightly sync: like `httpClientForLibrary`, but
/// also dispatches `/1.0/content/{asin}/metadata` (the last-position-heard
/// endpoint) per `metadataResponses` (`asin -> status, body`); an ASIN with
/// no entry there gets a plain, correctly-nested `status: "DoesNotExist"`
/// 200 (`doesNotExist`). Every metadata URL hit is recorded (by ASIN) for
/// the "exactly once" / "never called" assertions.
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
                    | None -> return jsonResponse (fst doesNotExist) (snd doesNotExist)
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

/// integration-dvbjp: `AudibleSync.runProgressSync`'s new `createBook`
/// parameter, for tests whose fixture only ever carries ALREADY-matched
/// ASINs (or hits an auth-file error before the per-item loop runs) --
/// asserts the create path is never reached in those tests instead of
/// silently wiring in a working stub that would mask a regression.
let private createBookMustNotBeCalled : Audible.AudibleLibraryItem -> Async<Result<AddBookOutcome, string>> =
    fun item -> async { return Error (sprintf "createBook should not have been called for %s in this test" item.Asin) }

/// integration-dvbjp: the real create path (`Api.createBookFromAudibleItem`,
/// `Api.noLocker` -- this per-test `SqliteConnection` is never shared with a
/// second thread), for tests that exercise the sync's own book-creation
/// behaviour directly (bypassing `Api.create`/`Composition.fs`'s wiring,
/// mirroring how `Composition.fs`'s own `createBookForAudibleSync` closure
/// wires it for the real job).
let private realCreateBook (conn: SqliteConnection) (httpClient: HttpClient) (imageBasePath: string) : Audible.AudibleLibraryItem -> Async<Result<AddBookOutcome, string>> =
    fun item -> Api.createBookFromAudibleItem conn httpClient imageBasePath allProjectionHandlers Api.noLocker "us" item

/// integration-dvbjp: proves the one-time-gate refusal never reaches Audible
/// at all -- any request throws, failing the test loudly instead of a stub
/// that quietly returns something plausible.
let private httpClientThatMustNotBeCalled () : HttpClient =
    let handler = new AsyncStubHandler(fun req -> failwith (sprintf "Audible must not be called once the library is already imported (requested %s)" (req.RequestUri.ToString())))
    new HttpClient(handler)

[<Tests>]
let importAudibleLibraryTests =
    testList "Api.importAudibleLibrary (integration-jjvg2)" [

        testCase "a 3-item fixture (0%, 42%, finished) creates 3 books, each recording a PRIOR with a calculated percent (integration-dtdbb, ADR-0082; minutes-based percent, integration-fn3yx, ADR-0086); a second import call is refused outright by the one-time bootstrap gate (integration-dvbjp)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                // The listing's own percent_complete is irrelevant now
                // whenever the metadata endpoint reports a real position
                // (ADR-0086) -- A1/B1 still carry it for realism, but their
                // recorded percent comes from `existsAt`'s minutes, not this
                // field. C1 (finished, no percent) is left unstubbed --
                // DoesNotExist + is_finished lands Percent=100 (ADR-0086).
                let itemZero = libraryItemJson "A1" "Book A" (Some 0.0) false (Some 600)
                let itemMid = libraryItemJson "B1" "Book B" (Some 42.0) false (Some 600)
                let itemFinished = libraryItemJson "C1" "Book C" None true (Some 600)
                let fixture = libraryResponseJson [ itemZero; itemMid; itemFinished ]
                let metadataResponses =
                    Map.ofList [
                        "A1", existsAt "2026-01-01" 0     // 0 / 600 = 0%
                        "B1", existsAt "2026-01-02" 252    // 252 / 600 = 42%
                    ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
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

                // integration-dvbjp (ADR-0082 Consequences): the first
                // populated run stamps the one-time bootstrap gate, so a
                // second call is refused outright -- it never reaches
                // Audible or the aggregate again. New purchases/percent
                // changes from here on are the nightly sync's job
                // (AudibleSync.runProgressSync tests below).
                let eventsBeforeSecondImport = totalEventCount db.Connection
                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Error msg -> Expect.stringContains msg "already imported" "the one-time gate refuses a second call"
                | Ok r -> failtestf "Expected the one-time gate to refuse a second call, got Ok %A" r
                Expect.equal (totalEventCount db.Connection) eventsBeforeSecondImport "the refused second call appends ZERO events")

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

        testCase "the import path stamps a fresh book's PRIOR's ObservedOn as the local calendar date when the metadata call yields no last-listened date (DoesNotExist + is_finished, ADR-0086)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                // integration-fn3yx (ADR-0086): a plain, not-finished
                // DoesNotExist item records NO prior at all any more (the
                // listing's own percent_complete is never trusted as a
                // fallback -- see the dedicated observationFor test below).
                // The only way a DoesNotExist item still records something
                // is the source's own explicit is_finished flag, which is
                // what this test exercises -- and it is exactly the "no
                // last-listened date" case this test's title describes,
                // since a DoesNotExist body carries no last_updated either.
                let fixture = libraryResponseJson [ libraryItemJson "D1" "Book D" None true (Some 400) ]
                let httpClient, _ = httpClientForImportWithMetadata fixture Map.empty
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
                | Some data ->
                    Expect.equal data.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "the import stamps the local calendar date, matching AudibleSync.runProgressSync"
                    Expect.equal data.Percent 100 "DoesNotExist + is_finished lands Percent = 100 (ADR-0086), never guessed from the listing"
                    Expect.isNone data.Position "DoesNotExist carries no position"
                | None -> failtest "Expected a Prior_reading_progress_recorded event")

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

/// integration-dvbjp (ADR-0082 Consequences): "Import library" is a true
/// one-time bootstrap -- stamped `audible_library_imported_at` after its
/// first populated run, refused (with no Audible call at all) once stamped.
[<Tests>]
let audibleOneTimeImportGateTests =
    testList "Api.importAudibleLibrary one-time bootstrap gate (integration-dvbjp, ADR-0082)" [

        testCase "a populated run stamps audible_library_imported_at; an empty-but-200 response never stamps it" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let emptyFixture = libraryResponseJson []
                let httpClientEmpty, _ = httpClientForLibrary (Map.ofList [ 1, emptyFixture ])
                let apiEmpty = createApi db.Factory httpClientEmpty imageBasePath (fun () -> configuredAudibleConfig)
                apiEmpty.importAudibleLibrary () |> Async.RunSynchronously |> ignore
                Expect.isNone (SettingsStore.getSetting db.Connection "audible_library_imported_at") "an empty-but-200 response never stamps the one-time gate (ADR-0068's lesson)"

                let fixture = libraryResponseJson [ libraryItemJson "S1" "Stamped Book" (Some 10.0) false (Some 300) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)
                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Ok _ -> ()
                | Error e -> failtestf "Expected Ok, got Error %s" e
                Expect.isSome (SettingsStore.getSetting db.Connection "audible_library_imported_at") "a genuinely populated run stamps the one-time gate")

        testCase "once stamped, a second importAudibleLibrary call is refused, names the date, and never calls Audible" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                SettingsStore.setSetting db.Connection "audible_library_imported_at" "2026-09-18T05:00:00.0000000Z"
                let httpClient = httpClientThatMustNotBeCalled ()
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Error msg ->
                    Expect.stringContains msg "2026-09-18" "the refusal names the stamped date"
                    Expect.stringContains msg "already imported" "the refusal explains why -- new purchases arrive with the nightly sync"
                | Ok r -> failtestf "Expected Error, got Ok %A" r)

        testCase "getAudibleSyncStatus().LibraryImportedAt round-trips the one-time bootstrap stamp" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "R1" "Roundtrip Book" (Some 10.0) false (Some 300) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)

                let beforeImport = api.getAudibleSyncStatus () |> Async.RunSynchronously
                Expect.isNone beforeImport.LibraryImportedAt "not stamped before any import runs"

                api.importAudibleLibrary () |> Async.RunSynchronously |> ignore
                let afterImport = api.getAudibleSyncStatus () |> Async.RunSynchronously
                Expect.isSome afterImport.LibraryImportedAt "the stamp round-trips through getAudibleSyncStatus")
    ]
    |> testSequenced

[<Tests>]
let audibleProgressSyncJobTests =
    testList "AudibleSync.runProgressSync -- the scheduled job (integration-jjvg2)" [

        testCase "an unmatched ASIN is created by the nightly sync itself and observed with a calculated percent -- never a prior, but the metadata endpoint IS now called (integration-fn3yx/ADR-0086 reverses integration-dtdbb's 'the sync never calls getLastPositionHeard'; ADR-0082/integration-dvbjp reverses integration-jjvg2's 'this job never creates a book' rule)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let fixture = libraryResponseJson [ libraryItemJson "NEW1" "New Purchase" (Some 20.0) false (Some 400) ]
                // 80 / 400 = 20%; the listing's own percent_complete (also
                // 20%) is irrelevant now -- this is the CALCULATED value.
                let metadataResponses = Map.ofList [ "NEW1", existsAt today 80 ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Created 1 "the unmatched ASIN was created"
                    Expect.equal r.Observed 1 "the newly-created book's first percent is observed"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (metadataCalls ()) [ "NEW1" ] "the job now calls getLastPositionHeard once, even for a book it just created"

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin "NEW1") |> Option.get
                let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                Expect.equal detail.Format BookFormat.Audiobook "created as an audiobook"
                Expect.equal detail.AudibleAsin (Some "NEW1") "linked to the library item's ASIN"

                let events = EventStore.readStream db.Connection (Books.streamId slug) |> List.choose Books.Serialization.fromStoredEvent
                match events |> List.filter (function Books.Reading_progress_observed _ | Books.Prior_reading_progress_recorded _ -> true | _ -> false) with
                | [ Books.Reading_progress_observed data ] ->
                    Expect.equal data.Percent 20 "the calculated percent (80 minutes of 400)"
                    Expect.equal data.Position (Some (Minutes (80, Some 400))) "the true position, not a percent x runtime estimate"
                    Expect.equal data.ObservedOn today "never a prior"
                | other -> failtestf "Expected exactly one Reading_progress_observed and NO Prior_reading_progress_recorded, got %A" other

                let metadata = MetadataCache.tryGetBookMetadata db.Connection slug
                Expect.equal metadata.Source (Some "audible") "the metadata cache slice is tagged as an audible source")

        testCase "a second sync of the same library creates nothing and appends nothing for the book it created on the first run (position AND percent both unchanged -- books-wk67x's no-op rule)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let fixture = libraryResponseJson [ libraryItemJson "NEW2" "New Purchase Two" (Some 20.0) false (Some 400) ]
                let metadataResponses = Map.ofList [ "NEW2", existsAt today 80 ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin "NEW2") |> Option.get
                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Created 0 "the book already exists -- the second run never re-creates it"
                    Expect.equal r.Observed 0 "same percent AND position as before -- a no-op (books-wk67x)"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "the second run appends zero events")

        testCase "a create failure for one item is reported in Errors and does not abort the run for the others" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "FAIL1" "Book Fail" (Some 10.0) false (Some 300); libraryItemJson "OK2" "Book OK2" (Some 20.0) false (Some 300) ]
                let httpClient, _ = httpClientForLibrary (Map.ofList [ 1, fixture ])
                let jobLock = new SemaphoreSlim(1, 1)
                let createBook (item: Audible.AudibleLibraryItem) : Async<Result<AddBookOutcome, string>> =
                    if item.Asin = "FAIL1" then async { return Error "boom" }
                    else realCreateBook db.Connection httpClient imageBasePath item

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBook allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Created 1 "only the successfully-created item counts"
                    Expect.equal (List.length r.Errors) 1 "the failed creation is reported as an error"
                    Expect.stringContains r.Errors.[0] "FAIL1" "the error names the failing item"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.isNone (BookProjection.findByExternalId db.Connection (AudibleAsin "FAIL1")) "the failed item never got a book"
                Expect.isSome (BookProjection.findByExternalId db.Connection (AudibleAsin "OK2")) "the other item was still created despite the first item's failure")

        testCase "changing a known book's calculated percent from 42%% to 55%% (after the import's own prior) appends the observation AND promotes to InFocus" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let firstFixture = libraryResponseJson [ libraryItemJson "B1" "Book B" (Some 42.0) false (Some 600) ]
                // 252 / 600 = 42%.
                let firstMetadata = Map.ofList [ "B1", existsAt today 252 ]
                let httpClient1, _ = httpClientForImportWithMetadata firstFixture firstMetadata
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
                // 330 / 600 = 55%; the listing's own percent_complete (also
                // "55.0" here) is irrelevant -- this is the CALCULATED
                // value, from a real position beyond the prior's 252 minutes.
                let secondMetadata = Map.ofList [ "B1", existsAt today 330 ]
                let httpClient2, _ = httpClientForImportWithMetadata secondFixture secondMetadata
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient2 (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "one book observed"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let eventsAfterJob = EventStore.readStream db.Connection (Books.streamId slugB)
                Expect.equal (List.length eventsAfterJob) (eventsBeforeJob + 2) "TWO new events appended: the observation, then the Backlog -> InFocus promotion it triggers"

                let newEvents = eventsAfterJob |> List.skip eventsBeforeJob |> List.choose Books.Serialization.fromStoredEvent
                match newEvents with
                | [ Books.Reading_progress_observed data; Books.Book_status_changed (BookStatus.InFocus, _) ] ->
                    Expect.equal data.Percent 55 "the new, calculated percent"
                    Expect.equal data.Position (Some (Minutes (330, Some 600))) "the true position"
                    Expect.equal data.Source ProgressSource.Audible "sourced from Audible"
                    Expect.equal data.ObservedOn today "observed today"
                | other -> failtestf "Expected [Reading_progress_observed; Book_status_changed InFocus], got %A" other

                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.InFocus "the ordinary observation promotes Backlog -> InFocus once the prior's baseline (42%%) is exceeded")

        // ── integration-fn3yx / ADR-0086: minutes decide, percent is
        // calculated -- the task's own acceptance criteria, pinned directly. ──

        testCase "reproduces the 2026-09-18 incident: the library listing reports percent_complete 0.0 while the metadata endpoint reports a real position_ms for a 539-minute title -- the observation trusts the metadata, never the reset listing percent" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let asin = "B06Y5MY16Q"
                let fixture = libraryResponseJson [ libraryItemJson asin "For We Are Many" (Some 0.0) false (Some 539) ]
                // The live captured sample: position_ms 3627052 (about 60
                // minutes) for a 539-minute runtime.
                let metadataResponses = Map.ofList [ asin, (HttpStatusCode.OK, lastPositionHeardJson "Exists" (Some today) (Some 3627052L)) ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "the item is observed from the true position"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin asin) |> Option.get
                let events = EventStore.readStream db.Connection (Books.streamId slug) |> List.choose Books.Serialization.fromStoredEvent
                match events |> List.filter (function Books.Reading_progress_observed _ -> true | _ -> false) with
                | [ Books.Reading_progress_observed data ] ->
                    Expect.equal data.Percent 11 "60 / 539 = 11%%, never the listing's reset 0%%"
                    Expect.equal data.Position (Some (Minutes (60, Some 539))) "3627052ms / 60000 = 60 minutes (int division)"
                | other -> failtestf "Expected exactly one Reading_progress_observed, got %A" other)

        testCase "the position moves from 60 to 62 minutes with the calculated percent unchanged at 11%% -- a second Reading_progress_observed is still written (books-wk67x: position, not just percent, drives the no-op comparison)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let asin = "B06Y5MY16Q"
                let fixture = libraryResponseJson [ libraryItemJson asin "For We Are Many" (Some 0.0) false (Some 539) ]
                let firstMetadata = Map.ofList [ asin, existsAt today 60 ]
                let httpClient1, _ = httpClientForImportWithMetadata fixture firstMetadata
                let jobLock = new SemaphoreSlim(1, 1)
                AudibleSync.runProgressSync db.Connection jobLock httpClient1 (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient1 imageBasePath) allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin asin) |> Option.get
                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length

                let secondMetadata = Map.ofList [ asin, existsAt today 62 ]
                let httpClient2, _ = httpClientForImportWithMetadata fixture secondMetadata

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient2 (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "the position change still counts as an observation despite the unchanged percent"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let eventsAfterSecondRun = EventStore.readStream db.Connection (Books.streamId slug)
                Expect.equal (List.length eventsAfterSecondRun) (eventsBeforeSecondRun + 1) "exactly one new event, a second Reading_progress_observed"

                let newEvents = eventsAfterSecondRun |> List.skip eventsBeforeSecondRun |> List.choose Books.Serialization.fromStoredEvent
                match newEvents with
                | [ Books.Reading_progress_observed data ] ->
                    Expect.equal data.Percent 11 "62 / 539 = 11%% too -- the percent alone did not change"
                    Expect.equal data.Position (Some (Minutes (62, Some 539))) "but the position did"
                | other -> failtestf "Expected exactly one new Reading_progress_observed, got %A" other)

        testCase "position and percent both unchanged since the last run -- zero events appended, Observed = 0" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let asin = "UNCHANGED1"
                let fixture = libraryResponseJson [ libraryItemJson asin "Unchanged Book" (Some 0.0) false (Some 539) ]
                let metadataResponses = Map.ofList [ asin, existsAt today 60 ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)
                AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin asin) |> Option.get
                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 0 "identical position and percent -- a genuine no-op"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "zero events appended")

        testCase "the metadata call fails for one item -- that item is skipped and listed in Errors, the other items are still observed, and the listing's percent is never used for the failed item" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let fixture =
                    libraryResponseJson [
                        libraryItemJson "FAILM1" "Book Fail Metadata" (Some 40.0) false (Some 300)
                        libraryItemJson "OKM1" "Book OK Metadata" (Some 10.0) false (Some 300)
                    ]
                let metadataResponses =
                    Map.ofList [
                        "FAILM1", (HttpStatusCode.InternalServerError, "")
                        "OKM1", existsAt today 60
                    ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Observed 1 "only the successfully-fetched item is observed"
                    Expect.equal (List.length r.Errors) 1 "the failed metadata call is reported as an error"
                    Expect.stringContains r.Errors.[0] "FAILM1" "the error names the failing item"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let slugFail = BookProjection.findByExternalId db.Connection (AudibleAsin "FAILM1") |> Option.get
                Expect.isEmpty (BookProjection.getBySlug db.Connection slugFail |> Option.get).ProgressHistory "the failing item's book creation is unaffected (creation precedes the metadata fetch), but NO progress row exists -- the listing's own 40%% was never used as a fallback"

                let slugOk = BookProjection.findByExternalId db.Connection (AudibleAsin "OKM1") |> Option.get
                match (BookProjection.getBySlug db.Connection slugOk |> Option.get).ProgressHistory with
                | [ row ] -> Expect.equal row.Percent 20 "60 / 300 = 20%%, calculated from the true position"
                | other -> failtestf "Expected exactly one progress row for OKM1, got %A" other)

        testCase "DoesNotExist with is_finished = false writes nothing; DoesNotExist with is_finished = true writes a finishing observation" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture =
                    libraryResponseJson [
                        libraryItemJson "NEVER1" "Never Started" (Some 0.0) false (Some 300)
                        libraryItemJson "FINISHED1" "Finished Never-Fetched" None true (Some 300)
                    ]
                // Neither ASIN has a metadataResponses entry -- both default
                // to the nested DoesNotExist stub.
                let httpClient, _ = httpClientForImportWithMetadata fixture Map.empty
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "only the finished title writes an observation"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let slugNever = BookProjection.findByExternalId db.Connection (AudibleAsin "NEVER1") |> Option.get
                Expect.isEmpty (BookProjection.getBySlug db.Connection slugNever |> Option.get).ProgressHistory "not finished, DoesNotExist -- no observation at all, never guessed from the listing's percent_complete"

                let slugFinished = BookProjection.findByExternalId db.Connection (AudibleAsin "FINISHED1") |> Option.get
                match (BookProjection.getBySlug db.Connection slugFinished |> Option.get).ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Percent 100 "DoesNotExist + is_finished lands Percent = 100"
                    Expect.isNone row.Position "DoesNotExist carries no position"
                | other -> failtestf "Expected exactly one progress row for FINISHED1, got %A" other
                Expect.equal (BookProjection.getBySlug db.Connection slugFinished |> Option.get).Status BookStatus.Finished "the explicit is_finished flag still finishes the book")

        testCase "a title with no RuntimeMinutes falls back to the floored listing percent, Position = None -- the only remaining use of the listing's percent" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let fixture = libraryResponseJson [ libraryItemJson "NORUNTIME1" "No Runtime Known" (Some 33.7) false None ]
                let metadataResponses = Map.ofList [ "NORUNTIME1", existsAt today 10 ]
                let httpClient, _ = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "the item is still observed, via the listing fallback"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                let slug = BookProjection.findByExternalId db.Connection (AudibleAsin "NORUNTIME1") |> Option.get
                match (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Percent 33 "33.7%% floors to 33%%, never rounds to 34%%"
                    Expect.isNone row.Position "no runtime to place a position against, even though a real position_ms was fetched"
                | other -> failtestf "Expected exactly one progress row, got %A" other)

        testCase "the sync calls getLastPositionHeard exactly once per library item, through Audible.throttleMetadataCall -- replaces the pre-fn3yx counting-stub that pinned the sync NEVER calling it" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let fixture =
                    libraryResponseJson [
                        libraryItemJson "CNT1" "Count One" (Some 10.0) false (Some 200)
                        libraryItemJson "CNT2" "Count Two" (Some 20.0) false (Some 200)
                        libraryItemJson "CNT3" "Count Three" (Some 30.0) false (Some 200)
                    ]
                let metadataResponses =
                    Map.ofList [
                        "CNT1", existsAt today 20
                        "CNT2", existsAt today 40
                        "CNT3", existsAt today 60
                    ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken (realCreateBook db.Connection httpClient imageBasePath) allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let calls = metadataCalls () |> List.sort
                Expect.equal calls [ "CNT1"; "CNT2"; "CNT3" ] "exactly one metadata call per library item, no more, no fewer")

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
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> noAuthFileConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
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
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
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
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r ->
                    Expect.equal r.Observed 0 "an empty library observes nothing"
                    Expect.equal r.Created 0 "an empty library reports 0 items plainly, not an error"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                match SettingsStore.getSetting db.Connection "audible_last_error" with
                | Some msg -> Expect.stringContains msg "a prior run" "an inconclusive empty response must NOT clear a standing notice"
                | None -> failtest "Expected the pre-set audible_last_error to survive an empty library response")

        testCase "a book with no Audible progress row, synced for the FIRST TIME by the JOB (not the import), gets a plain, calculated-percent observation and never a prior -- ONE getLastPositionHeard call, its date wins ObservedOn (integration-fn3yx/ADR-0086 reverses integration-dtdbb's 'zero getLastPositionHeard calls')" <| fun _ ->
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
                // 175 / 500 = 35%; the listing's own percent_complete (also
                // "35.0" here) is irrelevant -- this is the CALCULATED value.
                let metadataResponses = Map.ofList [ "N1", existsAt "2024-05-05" 175 ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture metadataResponses
                let jobLock = new SemaphoreSlim(1, 1)

                let result =
                    AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                    |> Async.RunSynchronously

                match result with
                | Ok r -> Expect.equal r.Observed 1 "the book's first-ever percent is observed by the job"
                | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (metadataCalls ()) [ "N1" ] "the job calls getLastPositionHeard exactly once now, even for a book with no prior Audible row -- priors stay the import's business only (ADR-0082 §2), but the metadata fetch itself is no longer import-exclusive (ADR-0086)"

                let events = EventStore.readStream db.Connection (Books.streamId slug) |> List.choose Books.Serialization.fromStoredEvent
                match events |> List.filter (function Books.Reading_progress_observed _ | Books.Prior_reading_progress_recorded _ -> true | _ -> false) with
                | [ Books.Reading_progress_observed data ] ->
                    Expect.equal data.Percent 35 "the calculated percent (175 minutes of 500)"
                    Expect.equal data.Position (Some (Minutes (175, Some 500))) "the true position"
                    Expect.equal data.Source ProgressSource.Audible "sourced from Audible"
                | other -> failtestf "Expected exactly one Reading_progress_observed and no Prior_reading_progress_recorded on the raw stream, got %A" other

                let detail = BookProjection.getBySlug db.Connection slug |> Option.get
                match detail.ProgressHistory with
                | [ row ] ->
                    Expect.equal row.Kind ProgressKind.Observed "the job's sync writes a plain observation, never a prior"
                    Expect.equal row.ObservedOn "2024-05-05" "dated to Audible's own last-listened day (ADR-0086 point 6), not the sync's run day"
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
                            match! AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers with
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
                AudibleSync.runProgressSync db.Connection jobLock httpClient (fun () -> configuredAudibleConfig) noopPersistToken createBookMustNotBeCalled allProjectionHandlers
                |> Async.RunSynchronously
                |> ignore

                let afterSync = api.getAudibleSyncStatus () |> Async.RunSynchronously
                Expect.isSome afterSync.LastSync "the sync time is persisted via SettingsStore"
                Expect.isSome afterSync.LastSyncResult "the sync result summary is persisted via SettingsStore")
    ]

/// integration-dtdbb (ADR-0082), amended by integration-fn3yx (ADR-0086):
/// `AudibleSync.observationFor`'s pure last-listened-date/position/percent
/// decision, exercised directly (no HTTP, no DB).
[<Tests>]
let observationForTests =
    let baseItem : Audible.AudibleLibraryItem =
        { Asin = "X1"; Title = "Book X"; Authors = []; Narrators = []; RuntimeMinutes = Some 600
          PercentComplete = Some 50.0; IsFinished = false; PurchaseDate = None; CoverUrl = None
          SeriesName = None; SeriesPosition = None; ReleaseDate = None; Description = None }
    testList "AudibleSync.observationFor (integration-dtdbb/ADR-0082, minutes decide/ADR-0086)" [

        testCase "lastListened = None (never fetched -- Api.fs's already-has-an-Audible-row re-observation branch): ObservedOn defaults to today, Position is the percent x runtime estimate, from the listing's own percent" <| fun _ ->
            match AudibleSync.observationFor baseItem "2026-09-18" None with
            | Some data ->
                Expect.equal data.ObservedOn "2026-09-18" "defaults to today when there's no last-listened info at all"
                Expect.equal data.Percent 50 "the listing's own percent -- never fetched, so never calculated"
                Expect.equal data.Position (Some (Minutes (300, Some 600))) "percent x runtime estimate: 50%% of 600 = 300"
            | None -> failtest "Expected Some data"

        testCase "PositionMs AND RuntimeMinutes both known: MINUTES DECIDE -- the percent is CALCULATED from the true position, never the listing's own percent_complete" <| fun _ ->
            let item = { baseItem with RuntimeMinutes = Some 999; PercentComplete = Some 10.0 }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = Some 896068L }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data ->
                Expect.equal data.ObservedOn "2023-09-23" "prefers the source's own last-listened day over today"
                Expect.equal data.Position (Some (Minutes (14, Some 999))) "896068ms / 60000 = 14 minutes (int division), NOT the 10%% x 999 estimate"
                Expect.equal data.Percent 1 "floor (14 / 999 x 100) = 1 -- CALCULATED, never the listing's own 10%%"
            | None -> failtest "Expected Some data"

        testCase "the calculated percent clamps at 100 when the position exceeds the runtime" <| fun _ ->
            let item = { baseItem with RuntimeMinutes = Some 60 }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = Some (70L * 60000L) }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data -> Expect.equal data.Percent 100 "70 minutes of a 60-minute runtime clamps to 100%%, never overshoots"
            | None -> failtest "Expected Some data"

        testCase "PositionMs known, RuntimeMinutes unknown: falls back to the listing's own floored percent, Position = None -- the only remaining use of the listing's percent" <| fun _ ->
            let item = { baseItem with RuntimeMinutes = None }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = Some 896068L }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data ->
                Expect.equal data.Percent 50 "the listing's own floored percent -- no runtime to calculate against"
                Expect.isNone data.Position "no runtime -- no Position, regardless of PositionMs"
            | None -> failtest "Expected Some data"

        testCase "status = DoesNotExist (PositionMs = None), is_finished = false: no observation at all -- never guessed from the listing's percent_complete" <| fun _ ->
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = None; PositionMs = None }
            Expect.isNone (AudibleSync.observationFor baseItem "2026-09-18" (Some lastListened)) "DoesNotExist + not finished writes nothing"

        testCase "status = DoesNotExist (PositionMs = None), LastUpdatedOn known but not finished: STILL no observation -- the date alone is not enough" <| fun _ ->
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = Some "2023-09-23"; PositionMs = None }
            Expect.isNone (AudibleSync.observationFor baseItem "2026-09-18" (Some lastListened)) "a known last-listened date with no position is still nothing to report, unless finished"

        testCase "status = DoesNotExist (PositionMs = None), is_finished = true: Percent = 100, Position = None, ObservedOn falls back to today (no last-listened date on a DoesNotExist body)" <| fun _ ->
            let item = { baseItem with IsFinished = true }
            let lastListened : Audible.LastPositionHeard = { LastUpdatedOn = None; PositionMs = None }
            match AudibleSync.observationFor item "2026-09-18" (Some lastListened) with
            | Some data ->
                Expect.equal data.Percent 100 "the source's own explicit Finished flag, not a rounding artifact"
                Expect.isNone data.Position "DoesNotExist carries no position"
                Expect.equal data.ObservedOn "2026-09-18" "no last-listened date known, falls back to today"
                Expect.isTrue data.Finished "Finished rides through"
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

        testCase "a fresh book (no Audible row yet) calls getLastPositionHeard exactly once; the resulting row is kind = prior, dated/positioned/PERCENT-CALCULATED from Audible, and finished_at matches when the item is finished (integration-fn3yx: real content_metadata nesting, calculated percent)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let fixture = libraryResponseJson [ libraryItemJson "F1" "Book F" None true (Some 600) ]
                let metadataResponses =
                    Map.ofList [
                        "F1", (HttpStatusCode.OK, """{"content_metadata": {"last_position_heard": {"last_updated": "2023-09-23 21:03:18.228", "position_ms": 896068, "status": "Exists"}}}""")
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
                // books-wk67x: EntryId is the entry's real store position —
                // not predictable here, so compare every OTHER field.
                // 896068ms / 60000 = 14 minutes (int division); floor (14 /
                // 600 x 100) = 2 -- CALCULATED (ADR-0086), never 100 (the
                // item carries no percent_complete at all, only is_finished).
                Expect.equal
                    (detail.ProgressHistory |> List.map (fun r -> r.ObservedOn, r.Source, r.Percent, r.Position, r.Kind))
                    [ "2023-09-23", ProgressSource.Audible, 2, Some (Minutes (14, Some 600)), ProgressKind.Prior ]
                    "one prior row, dated/positioned/percent-calculated from Audible's own metadata"
                Expect.equal detail.Status BookStatus.Finished "is_finished lands Finished regardless of the (low) calculated percent -- the explicit flag decides, not the percent"
                Expect.equal detail.FinishedAt (Some "2023-09-23") "finished_at matches the metadata's own last-listened date")

        testCase "a book that already has an Audible row makes no metadata call and appends an ordinary observation" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                // integration-dvbjp (ADR-0082 Consequences): the one-time
                // bootstrap gate refuses a SECOND `importAudibleLibrary`
                // call outright, so this scenario -- a book that already
                // carries an Audible row by the time the (single) import
                // runs -- is seeded directly instead of via two public
                // import calls.
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                let slug = "g1-book"
                let addedData : Books.BookAddedData = {
                    Title = "Book G"; Authors = [ "Author" ]; Year = None; CoverRef = None
                    Subjects = []; Format = BookFormat.Audiobook; ExternalIds = [ AudibleAsin "G1" ]
                }
                let priorData : Books.ReadingProgressObservedData = {
                    Percent = 20; Position = Some (Minutes (120, Some 600)); Source = ProgressSource.Audible
                    ObservedOn = today; Finished = false
                }
                let seedEvents = [ Books.Book_added_to_library addedData; Books.Prior_reading_progress_recorded priorData ]
                match EventStore.appendToStream db.Connection (Books.streamId slug) -1L (seedEvents |> List.map Books.Serialization.toEventData) with
                | EventStore.Success _ -> ()
                | EventStore.ConcurrencyConflict _ -> failtest "unexpected concurrency conflict seeding the fixture"
                for handler in allProjectionHandlers do Projection.runProjection db.Connection handler

                let fixture = libraryResponseJson [ libraryItemJson "G1" "Book G" (Some 40.0) false (Some 600) ]
                let httpClient, metadataCalls = httpClientForImportWithMetadata fixture Map.empty
                let api = createApi db.Factory httpClient imageBasePath (fun () -> configuredAudibleConfig)
                let result =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok, got Error %s" e

                Expect.equal (metadataCalls () |> List.length) 0 "a book that already carries an Audible row never calls the metadata endpoint"
                Expect.equal result.ProgressObserved 1 "the percent change (20%% -> 40%%) appends an ordinary observation"
                Expect.equal result.PriorsFromAudible 0 "no new prior this run"
                Expect.equal result.PriorsToday 0 "no new prior this run"

                // books-wk67x (amending ADR-0076 §2): the seeded prior and
                // the new observation share the same calendar day and
                // source, but history is append-only now -- the prior
                // stays exactly as recorded, and the observation is a
                // SECOND, distinct row, never an upsert over the first.
                let history = (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory
                match history with
                | [ prior; observed ] ->
                    Expect.equal prior.Kind ProgressKind.Prior "the seeded prior is untouched"
                    Expect.equal prior.Percent 20 "the prior's own percent survives, not overwritten"
                    Expect.equal observed.Kind ProgressKind.Observed "the new entry is a plain observation"
                    Expect.equal observed.Percent 40 "the new entry's own percent"
                | other -> failtestf "Expected the prior plus one new observation row, got %A" other)

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

        testCase "legacy repair: a book whose only Audible row is kind=observation becomes a single re-dated prior after one import; a second import call is refused by the one-time gate (integration-dvbjp)" <| fun _ ->
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
                        "L1", (HttpStatusCode.OK, """{"content_metadata": {"last_position_heard": {"last_updated": "2024-03-01 10:00:00.000", "position_ms": 21600000, "status": "Exists"}}}""")
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
                // books-wk67x: EntryId is the entry's real store position —
                // not predictable here, so compare every OTHER field.
                Expect.equal
                    (detailAfterRepair.ProgressHistory |> List.map (fun r -> r.ObservedOn, r.Source, r.Percent, r.Position, r.Kind))
                    [ "2024-03-01", ProgressSource.Audible, 60, Some (Minutes (360, Some 600)), ProgressKind.Prior ]
                    "the legacy row is replaced by a single, correctly-dated prior"

                // integration-dvbjp (ADR-0082 Consequences): the one-time
                // bootstrap gate refuses a second call outright.
                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length
                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Error msg -> Expect.stringContains msg "already imported" "the one-time gate refuses a second call"
                | Ok r -> failtestf "Expected the one-time gate to refuse a second call, got Ok %A" r
                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "the refused second call appends zero events")

        testCase "legacy repair on an ALREADY-FINISHED book re-dates finished_at to Audible's last-listened day; a second import call is refused by the one-time gate (integration-dvbjp)" <| fun _ ->
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
                        "M1", (HttpStatusCode.OK, """{"content_metadata": {"last_position_heard": {"last_updated": "2023-09-23 21:03:18.228", "position_ms": 36000000, "status": "Exists"}}}""")
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

                // integration-dvbjp (ADR-0082 Consequences): the one-time
                // bootstrap gate refuses a second call outright.
                let eventsBeforeSecondRun = EventStore.readStream db.Connection (Books.streamId slug) |> List.length
                match api.importAudibleLibrary () |> Async.RunSynchronously with
                | Error msg -> Expect.stringContains msg "already imported" "the one-time gate refuses a second call"
                | Ok r -> failtestf "Expected the one-time gate to refuse a second call, got Ok %A" r
                Expect.equal (EventStore.readStream db.Connection (Books.streamId slug) |> List.length) eventsBeforeSecondRun "the refused second call appends zero events")
    ]
