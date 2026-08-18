module Mediatheca.Tests.PlaytimeSyncKeyRejectionTests

/// integration-k4vqm: `PlaytimeTracker.runSync` used to call the throwing
/// `Steam.getRecentlyPlayedGames`, which lets a rejected/revoked Web API key
/// (401) escape as an opaque `HttpRequestException` caught only by this
/// function's own outer catch-all -- recorded as a generic "Playtime sync
/// failed: ...401..." message that never names the Web API key or its
/// remedy, and never surfaces the same standing `steam_api_key_last_error`
/// notice Settings -> Steam already renders for the Family import
/// (integration-r8kwd). This suite pins the fix: a rejected key is now a
/// typed, attributed, PERSISTED outcome (`Steam.tryGetRecentlyPlayedGames` +
/// `steam_api_key_last_error`), while a genuinely empty ("nothing played
/// recently") response is left alone -- that is the normal, common case for
/// this endpoint, not evidence of anything wrong.

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private unauthorizedResponse () =
    new HttpResponseMessage(HttpStatusCode.Unauthorized)

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ ContentBlockProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

let private steamConfig : Steam.SteamConfig =
    { ApiKey = "revoked-key"; SteamId = "76561198000000000" }

let private rawgConfig () : Rawg.RawgConfig = { ApiKey = "" }

[<Tests>]
let playtimeSyncKeyRejectionTests =
    testList "PlaytimeTracker.runSync -- Web API key rejection is attributed and persisted (integration-k4vqm)" [

        testCase "A scheduled sync whose owned/recently-played-games-shaped call returns nothing (401, key rejected) surfaces a persisted, user-visible indication rather than completing silently" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let jobLock = new SemaphoreSlim(1, 1)
            let http = new HttpClient(new StubHandler(fun _ -> unauthorizedResponse ()))

            let result =
                PlaytimeTracker.runSync db.Connection jobLock http (fun () -> steamConfig) rawgConfig noImagesDir allProjectionHandlers None
                |> Async.RunSynchronously

            match result with
            | Error msg -> Expect.stringContains msg "steamcommunity.com/dev/apikey" "The failure names the Web API key's regenerate remedy"
            | Ok r -> failtestf "Expected the sync to fail on a rejected key, got Ok %A" r

            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            match persistedError with
            | Some msg -> Expect.stringContains msg "steamcommunity.com/dev/apikey" "The persisted notice carries the remedy, same as the Family import's (integration-r8kwd)"
            | None -> failtest "Expected steam_api_key_last_error to be persisted so Settings -> Steam surfaces a standing notice"

        testCase "A genuinely empty (200, no recent games) response completes normally without touching steam_api_key_last_error" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_api_key_last_error" "stale notice from an earlier rejection"
            let jobLock = new SemaphoreSlim(1, 1)
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse """{"response":{"total_count":0,"games":[]}}"""))

            let result =
                PlaytimeTracker.runSync db.Connection jobLock http (fun () -> steamConfig) rawgConfig noImagesDir allProjectionHandlers None
                |> Async.RunSynchronously

            match result with
            | Ok r -> Expect.equal r.SessionsRecorded 0 "No recent games -> no sessions recorded, and that's fine"
            | Error e -> failtestf "A genuinely empty response is not a failure, got Error %s" e

            // Not evidence either way -- a stale notice is left as-is, since an
            // empty "recently played" result here is the common, benign case
            // and the sync itself made no observation about the key's health.
            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            Expect.isSome persistedError "A benign empty result does not clear an unrelated stale notice"
    ]
