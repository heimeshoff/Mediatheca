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
    ContentBlockProjection.handler.Init conn
    NotesProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn
    Administration.initializeJobRuns conn

let private allProjectionHandlers = [ ContentBlockProjection.handler; BookProjection.handler ]

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
        (fun () -> ({ UserId = None; ImportShelves = [ "currently-reading" ] } : Goodreads.GoodreadsConfig))
        (fun () -> async { return Error "not wired in tests" })
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

        testCase "a 3-item fixture (0%, 42%, finished) creates 3 books with the right statuses and exactly 2 progress observations; a second import is a total no-op" <| fun _ ->
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
                Expect.equal result.ProgressObserved 2 "only the 42%% and finished items produced an observation -- 0%% with no prior Audible observation is the per-source default baseline"

                let slugA = BookProjection.findByExternalId db.Connection (AudibleAsin "A1") |> Option.get
                let slugB = BookProjection.findByExternalId db.Connection (AudibleAsin "B1") |> Option.get
                let slugC = BookProjection.findByExternalId db.Connection (AudibleAsin "C1") |> Option.get

                Expect.equal (BookProjection.getBySlug db.Connection slugA |> Option.get).Status BookStatus.Backlog "0%% stays Backlog"
                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.InFocus "42%% promotes to InFocus"
                Expect.equal (BookProjection.getBySlug db.Connection slugC |> Option.get).Status BookStatus.Finished "is_finished with no percent lands Finished (treated as 100%%)"

                let progressRowCount =
                    [ slugA; slugB; slugC ]
                    |> List.sumBy (fun slug -> (BookProjection.getBySlug db.Connection slug |> Option.get).ProgressHistory |> List.length)
                Expect.equal progressRowCount 2 "exactly 2 book_progress rows total, only for the two items with percent > 0"

                let eventsBeforeSecondImport = totalEventCount db.Connection
                let second =
                    match api.importAudibleLibrary () |> Async.RunSynchronously with
                    | Ok r -> r
                    | Error e -> failtestf "Expected Ok on re-run, got Error %s" e
                Expect.equal second.Created 0 "re-run creates nothing"
                Expect.equal second.AlreadyKnown 3 "all three now known"
                Expect.equal second.ProgressObserved 0 "same-percent-per-source no-op -- re-run observes nothing"
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

        testCase "the import path stamps ObservedOn as the local calendar date -- the same date the sync path uses" <| fun _ ->
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
                        | Books.Reading_progress_observed data -> Some data
                        | _ -> None)
                match observation with
                | Some data -> Expect.equal data.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "the import stamps the local calendar date, matching AudibleSync.runProgressSync"
                | None -> failtest "Expected a Reading_progress_observed event")
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

        testCase "changing a known book's percent from 42%% to 55%% appends exactly one Reading_progress_observed and no status change (already InFocus)" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let firstFixture = libraryResponseJson [ libraryItemJson "B1" "Book B" (Some 42.0) false (Some 600) ]
                let httpClient1, _ = httpClientForLibrary (Map.ofList [ 1, firstFixture ])
                let api = createApi db.Factory httpClient1 imageBasePath (fun () -> configuredAudibleConfig)
                api.importAudibleLibrary () |> Async.RunSynchronously |> ignore

                let slugB = BookProjection.findByExternalId db.Connection (AudibleAsin "B1") |> Option.get
                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.InFocus "sanity: promoted to InFocus by the import"
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
                Expect.equal (List.length eventsAfterJob) (eventsBeforeJob + 1) "exactly ONE new event appended (the observation; no status-change event since already InFocus)"

                let newEvent = eventsAfterJob |> List.last |> Books.Serialization.fromStoredEvent |> Option.get
                match newEvent with
                | Books.Reading_progress_observed data ->
                    Expect.equal data.Percent 55 "the new percent"
                    Expect.equal data.Source ProgressSource.Audible "sourced from Audible"
                    Expect.equal data.ObservedOn (DateTime.Now.ToString("yyyy-MM-dd")) "observed today"
                | other -> failtestf "Expected Reading_progress_observed, got %A" other

                Expect.equal (BookProjection.getBySlug db.Connection slugB |> Option.get).Status BookStatus.InFocus "status unchanged -- already InFocus")

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
