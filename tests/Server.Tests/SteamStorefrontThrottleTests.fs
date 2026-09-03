module Mediatheca.Tests.SteamStorefrontThrottleTests

/// integration-w7ktb: Steam's storefront (`store.steampowered.com`) has an
/// informal ceiling of ~200 requests/5 minutes (1.5s/request). Pacing used
/// to be caller-owned and failed open on 8 of 11 `appdetails` call sites --
/// including the Family import's per-app loop, the exact traffic pattern
/// Valve twice flagged the builder's account over. This suite pins the
/// Adapter-owned throttle (`Steam.throttleStorefrontCall`, wired into
/// `Steam.getSteamStoreDetails`) that replaces caller-owned pacing:
/// 1. Two back-to-back gated calls space out by at least the configured
///    interval (the gate itself, no HTTP involved).
/// 2. A real family import against a fake `HttpClient` issues exactly one
///    `appdetails` request per app, spaced by the interval, and never two
///    storefront requests in flight at once (the sequential per-app loop's
///    guarantee, pinned).
///
/// No test here makes a live Steam call -- every request goes through a
/// stub `HttpMessageHandler` (ADR-0066). Both cases mutate the module-level
/// `Steam.throttleStorefrontInterval` -- `testSequenced` below keeps them
/// from racing each other; no other test file touches that knob.

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

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private notFoundResponse () =
    new HttpResponseMessage(HttpStatusCode.NotFound)

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ ContentBlockProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        noImagesDir
        allProjectionHandlers

/// Thread-safe log of every `appdetails` request's start time and how many
/// were in flight at once. The stub's small simulated response delay
/// (`Async.Sleep 15` below) gives concurrency a real window to show up in,
/// if the gate ever let two calls run at once.
type private RequestLog() =
    let gate = obj ()
    let mutable timestamps: System.DateTime list = []
    let mutable inFlight = 0
    let mutable maxInFlight = 0
    member _.Start() =
        lock gate (fun () ->
            inFlight <- inFlight + 1
            if inFlight > maxInFlight then maxInFlight <- inFlight
            timestamps <- timestamps @ [ System.DateTime.UtcNow ])
    member _.End() = lock gate (fun () -> inFlight <- inFlight - 1)
    member _.Timestamps = lock gate (fun () -> timestamps)
    member _.MaxInFlight = lock gate (fun () -> maxInFlight)

/// Clock/dispatch-overhead tolerance shared by both timing assertions below.
/// `Async.Sleep`/`Task.Delay` (used by `Steam.throttleStorefrontCall`'s interval wait) is
/// only guaranteed to sleep *at least* the requested duration in principle, but in practice
/// -- confirmed empirically here, including on the direct gate test with no HTTP involved at
/// all -- the *measured* gap between two gated calls can come in a few ms under the nominal
/// interval, from ordinary timer-resolution/scheduling jitter. A bare `gap >= interval` is
/// therefore flaky regardless of how large the interval is (the jitter is a roughly-bounded
/// absolute quantity, not proportional to the interval). This tolerance absorbs that jitter
/// while staying far below the 15ms simulated response delay the family-import stub uses, so
/// a deleted interval wait (see the two tests' mutation-tested comments) still fails loudly.
let private clockTolerance = System.TimeSpan.FromMilliseconds(5.0)

let private appDetailsJson (appId: int) =
    sprintf
        """{"%d":{"success":true,"data":{"short_description":"d","detailed_description":"","about_the_game":"about %d","categories":[],"header_image":"https://example.com/h.jpg"}}}"""
        appId appId

let private appIdFromUrl (url: string) : int =
    let m = System.Text.RegularExpressions.Regex.Match(url, @"appids=(\d+)")
    if m.Success then int m.Groups.[1].Value else 0

/// Answers the family-service calls needed to get past the top of
/// `runSteamFamilyImport` with `appCount` distinct, unowned shared apps (so
/// every one of them goes through the "new game" per-app branch, and each
/// issues exactly one `appdetails` request), answers `GetOwnedGames` with an
/// empty-but-successful list, 404s the CDN cover/backdrop downloads (out of
/// scope -- `downloadSteamCover`/`downloadSteamBackdrop` already degrade to
/// `None` on a non-2xx response, per `AddGameFromSteamTests`), and logs
/// every `appdetails` request's start time and in-flight count via `log`.
let private httpClientForFamilyImport (appCount: int) (log: RequestLog) : HttpClient =
    let apps =
        [ for i in 1 .. appCount ->
            sprintf """{"appid":%d,"name":"Storefront Throttle Game %d","owner_steamids":[],"rt_time_acquired":1700000000}""" (9000 + i) i ]
        |> String.concat ","
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                if url.Contains("GetOwnedGames") then
                    return jsonResponse """{"response":{"game_count":0,"games":[]}}"""
                elif url.Contains("GetFamilyGroupForUser") then
                    return jsonResponse """{"response":{"family_groupid":"12345","members":[]}}"""
                elif url.Contains("GetSharedLibraryApps") then
                    return jsonResponse (sprintf """{"response":{"apps":[%s]}}""" apps)
                elif url.Contains("appdetails") then
                    log.Start()
                    do! Async.Sleep 15
                    log.End()
                    return jsonResponse (appDetailsJson (appIdFromUrl url))
                else
                    return notFoundResponse ()
            })
    new HttpClient(handler)

[<Tests>]
let steamStorefrontThrottleTests =
    testList "Steam storefront throttle (integration-w7ktb)" [

        testCase "The gate: two back-to-back gated calls are spaced at least the configured interval apart" <| fun _ ->
            let originalInterval = Steam.throttleStorefrontInterval
            let interval = System.TimeSpan.FromMilliseconds(80.0)
            try
                Steam.throttleStorefrontInterval <- interval
                let timestamps = System.Collections.Generic.List<System.DateTime>()
                let gatedCall () =
                    Steam.throttleStorefrontCall (fun () ->
                        async {
                            timestamps.Add(System.DateTime.UtcNow)
                            return ()
                        })
                gatedCall () |> Async.RunSynchronously
                gatedCall () |> Async.RunSynchronously
                Expect.equal timestamps.Count 2 "Both gated calls ran"
                let gap = timestamps.[1] - timestamps.[0]
                Expect.isTrue
                    (gap >= interval - clockTolerance)
                    (sprintf "Expected the second call to start at least %A after the first (tolerance %A), got %A" interval clockTolerance gap)
            finally
                Steam.throttleStorefrontInterval <- originalInterval

        testCase "importSteamFamily against 3 shared apps issues exactly 3 appdetails requests, spaced by the interval, never two in flight" <| fun _ ->
            let originalInterval = Steam.throttleStorefrontInterval
            // 250ms, not 80ms as in the gate-only test above: this test additionally measures
            // the gap at `SendAsync` entry inside the stub -- one HttpClient pipeline hop
            // *after* the gate records `lastStorefrontCallStartedAt` and calls `fetch ()`
            // (Steam.fs's `throttleStorefrontCall`) -- which is its own, separate source of
            // jitter on top of `clockTolerance`'s. 250ms comfortably outruns both while keeping
            // total test runtime sub-second. `interval - clockTolerance` (245ms) still fails
            // loudly if the gate's wait were ever deleted: consecutive requests would then land
            // tens of milliseconds apart at most, bounded by the stub's 15ms simulated delay.
            let interval = System.TimeSpan.FromMilliseconds(250.0)
            try
                Steam.throttleStorefrontInterval <- interval
                use db = TestDb.withTempDbFactory bootstrap
                SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"
                let log = RequestLog()
                let http = httpClientForFamilyImport 3 log
                let api = createApi db.Factory http

                let result = api.importSteamFamily () |> Async.RunSynchronously

                match result with
                | Ok importResult ->
                    Expect.equal importResult.GamesProcessed 3 "All 3 shared apps went through the per-app loop"
                | Error e -> failtestf "Expected the import to succeed, got Error %s" e

                let timestamps = log.Timestamps
                Expect.equal timestamps.Length 3 "Exactly 3 appdetails requests were issued -- one per app"

                let gaps = timestamps |> List.pairwise |> List.map (fun (a, b) -> b - a)
                for gap in gaps do
                    Expect.isTrue
                        (gap >= interval - clockTolerance)
                        (sprintf "Expected consecutive appdetails requests spaced at least %A apart, got %A" interval gap)

                Expect.equal log.MaxInFlight 1 "No two storefront requests were ever in flight at once -- the sequential per-app loop, pinned"
            finally
                Steam.throttleStorefrontInterval <- originalInterval
    ]
    |> testSequenced
