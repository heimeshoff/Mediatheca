module Mediatheca.Tests.SteamFamilyImportOwnedGamesTests

/// integration-r8kwd: the Steam Family import's `Steam.getOwnedGames`
/// supplement (own-ownership enrichment, `Api.fs`'s `runSteamFamilyImport`)
/// used to escape as a thrown `HttpRequestException` on a revoked/rejected
/// Web API key (401), landing in the outer catch-all and producing the
/// misleading "Steam Family import failed ... 401" message -- which reads as
/// a rejected *family* token, not the unrelated Web API key. This suite
/// pins:
/// 1. `Steam.tryGetOwnedGames` maps a 401/403 to the typed `KeyRejected`,
///    not an exception (the underlying fix).
/// 2. A 401 from the supplement, wired through `IMediathecaApi.importSteamFamily`,
///    no longer fails the import -- it completes `Ok` with exactly one
///    attributed error line naming the Web API key and its remedy, and still
///    writes `steam_family_last_sync`.

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

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

/// Answers the two family-service calls the import needs to get past
/// (`GetFamilyGroupForUser`, `GetSharedLibraryApps`, the latter carrying one
/// shared app so the import's per-app game-creation path is genuinely
/// exercised under the 401, not just skipped past) with a valid response, and
/// answers `GetOwnedGames` -- the Web-API-key-authenticated supplement --
/// with 401, as if Valve had revoked the key. Store-details/CDN calls for the
/// one app fall through to 401/404, which `Steam.getSteamStoreDetails` and
/// `downloadSteamCover`/`downloadSteamBackdrop` already degrade from
/// gracefully (see `AddGameFromSteamTests`'s equivalent coverage).
let private httpClientWithRejectedOwnedGames () : HttpClient =
    let handler =
        new StubHandler(fun req ->
            let url = req.RequestUri.ToString()
            if url.Contains("GetOwnedGames") then unauthorizedResponse ()
            elif url.Contains("GetFamilyGroupForUser") then
                jsonResponse """{"response":{"family_groupid":"12345","members":[]}}"""
            elif url.Contains("GetSharedLibraryApps") then
                jsonResponse """{"response":{"apps":[{"appid":620,"name":"Portal 2","owner_steamids":["76561198000000000"],"rt_time_acquired":1700000000}]}}"""
            else unauthorizedResponse ())
    new HttpClient(handler)

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = "revoked-key"; SteamId = "76561198000000000" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        noImagesDir
        allProjectionHandlers

[<Tests>]
let steamWebApiKeyTypedErrorTests =
    testList "Steam.tryGetOwnedGames (integration-r8kwd)" [

        testCase "A 401 from GetOwnedGames maps to Error KeyRejected, not a thrown exception" <| fun _ ->
            let http = httpClientWithRejectedOwnedGames ()
            let config: Steam.SteamConfig = { ApiKey = "revoked-key"; SteamId = "76561198000000000" }
            let result = Steam.tryGetOwnedGames http config |> Async.RunSynchronously
            Expect.equal result (Error Steam.KeyRejected) "401/403 maps to the typed KeyRejected case"

        testCase "A 403 from GetOwnedGames also maps to Error KeyRejected" <| fun _ ->
            let handler = new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.Forbidden))
            let http = new HttpClient(handler)
            let config: Steam.SteamConfig = { ApiKey = "revoked-key"; SteamId = "76561198000000000" }
            let result = Steam.tryGetOwnedGames http config |> Async.RunSynchronously
            Expect.equal result (Error Steam.KeyRejected) "403 also maps to KeyRejected"

        testCase "A non-auth failure maps to WebApiOtherFailure, distinct from KeyRejected" <| fun _ ->
            let handler = new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.InternalServerError))
            let http = new HttpClient(handler)
            let config: Steam.SteamConfig = { ApiKey = "some-key"; SteamId = "76561198000000000" }
            let result = Steam.tryGetOwnedGames http config |> Async.RunSynchronously
            match result with
            | Error (Steam.WebApiOtherFailure _) -> ()
            | other -> failtestf "Expected WebApiOtherFailure for a 500, got %A" other
    ]

[<Tests>]
let steamFamilyImportOwnedGamesSupplementTests =
    testList "IMediathecaApi.importSteamFamily -- owned-games supplement resilience (integration-r8kwd)" [

        testCase "A 401 from the owned-games supplement does not fail the import; it completes Ok with one attributed error line naming the Web API key and remedy" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let http = httpClientWithRejectedOwnedGames ()
            let api = createApi db.Factory http

            let result = api.importSteamFamily () |> Async.RunSynchronously

            match result with
            | Ok importResult ->
                // The rejected supplement didn't stop the per-app loop: the
                // one shared app (owned by the caller per the stub's
                // owner_steamids) is still created and its owner still set —
                // acceptance criterion 1's "games are created/matched, family
                // owners ... set" despite the 401.
                Expect.equal importResult.GamesProcessed 1 "The per-app loop still ran"
                Expect.equal importResult.GamesCreated 1 "The shared app was still created"
                Expect.equal importResult.FamilyOwnersSet 1 "Its listed owner was still set"
                Expect.equal importResult.Errors.Length 1 "Exactly one error line"
                let errorLine = importResult.Errors.Head
                Expect.stringContains errorLine "Web API key" "The error names the Web API key, not the family token"
                Expect.stringContains errorLine "steamcommunity.com/dev/apikey" "The error carries the regenerate remedy"
                Expect.isFalse (errorLine.ToLowerInvariant().Contains("reconnect required")) "Never shares wording with the family-token reconnect message"
            | Error e -> failtestf "Expected the import to complete Ok despite the rejected Web API key, got Error %s" e

            let lastSync = SettingsStore.getSetting db.Connection "steam_family_last_sync"
            Expect.isSome lastSync "steam_family_last_sync is still written even though the supplement failed"

            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            match persistedError with
            | Some msg -> Expect.stringContains msg "steamcommunity.com/dev/apikey" "The persisted last-error notice carries the remedy"
            | None -> failtest "Expected steam_api_key_last_error to be persisted for Settings to surface as a standing notice"

        testCase "A subsequent successful owned-games call clears the persisted last-error notice" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"
            SettingsStore.setSetting db.Connection "steam_api_key_last_error" "stale notice from an earlier failure"

            let handler =
                new StubHandler(fun req ->
                    let url = req.RequestUri.ToString()
                    if url.Contains("GetOwnedGames") then jsonResponse """{"response":{"game_count":0,"games":[]}}"""
                    elif url.Contains("GetFamilyGroupForUser") then
                        jsonResponse """{"response":{"family_groupid":"12345","members":[]}}"""
                    elif url.Contains("GetSharedLibraryApps") then
                        jsonResponse """{"response":{"apps":[]}}"""
                    else unauthorizedResponse ())
            let http = new HttpClient(handler)
            let api = createApi db.Factory http

            let result = api.importSteamFamily () |> Async.RunSynchronously

            match result with
            | Ok importResult -> Expect.isEmpty importResult.Errors "No errors once the key works again"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            Expect.isNone persistedError "A successful owned-games call clears the stale notice"
    ]

/// integration-r8kwd (iteration 2): acceptance criterion 4 says the "Steam
/// Web API key rejected" notice is cleared once a key is saved/tested
/// successfully. The suite above only pins the clearing that happens as a
/// side effect of a subsequent successful *import*. These two cases pin the
/// two other production clear points directly: `setSteamApiKey` (saving a
/// fresh key) and `testSteamApiKey` (the Settings page's "Test" button).
[<Tests>]
let steamApiKeyLastErrorClearedOnSaveOrTestTests =
    testList "IMediathecaApi.setSteamApiKey / testSteamApiKey -- clear the persisted last-error notice (integration-r8kwd)" [

        testCase "setSteamApiKey clears a stale steam_api_key_last_error notice" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_api_key_last_error" "stale notice from an earlier rejection"

            // Not exercised by this call -- setSteamApiKey does no HTTP -- but
            // createApi requires an HttpClient.
            let http = new HttpClient(new StubHandler(fun _ -> unauthorizedResponse ()))
            let api = createApi db.Factory http

            let result = api.setSteamApiKey "fresh-key" |> Async.RunSynchronously

            Expect.equal result (Ok ()) "Saving the key succeeds"
            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            Expect.isNone persistedError "Saving a (presumably fresh) key clears the stale notice"

        testCase "testSteamApiKey clears a stale steam_api_key_last_error notice on a successful test" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_api_key_last_error" "stale notice from an earlier rejection"

            let handler =
                new StubHandler(fun req ->
                    let url = req.RequestUri.ToString()
                    if url.Contains("GetOwnedGames") then
                        jsonResponse
                            """{"response":{"game_count":1,"games":[{"appid":620,"name":"Portal 2","playtime_forever":100,"img_icon_url":"","rtime_last_played":0}]}}"""
                    else unauthorizedResponse ())
            let http = new HttpClient(handler)
            let api = createApi db.Factory http

            let result = api.testSteamApiKey "fresh-key" |> Async.RunSynchronously

            Expect.equal result (Ok ()) "The test call succeeds because the stub returns a non-empty game list"
            let persistedError = SettingsStore.getSetting db.Connection "steam_api_key_last_error"
            Expect.isNone persistedError "A successful key test clears the stale notice"
    ]
