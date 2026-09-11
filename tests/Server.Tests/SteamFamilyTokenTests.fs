module Mediatheca.Tests.SteamFamilyTokenTests

// integration-v0xmv (ADR-0070): the mint-and-retry seam (`withTokenRefresh`,
// `TokenMinter`) and every Steam-login-capable path (`mintFamilyAccessToken`,
// `steamIdFromRefreshToken`) are removed outright — the Steam Family import
// runs only against a browser-obtained access token pasted by the user in
// Settings. This suite now pins what's left: the 401/403 attribution that
// still distinguishes "paste a fresh token" from any other failure, and the
// "not configured, no HTTP call" degenerate case.

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Server.Steam
open Mediatheca.Shared

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private unauthorizedResponse () =
    new HttpResponseMessage(HttpStatusCode.Unauthorized)

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
        (Qbittorrent.createHttpClient ()) // ADR-0072: qBittorrent uses its own cookie-jar-free client
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        allProjectionHandlers

[<Tests>]
let familyTokenRejectedTests =
    testList "family token rejection (integration-v0xmv, ADR-0070)" [

        testCase "a 401 from GetFamilyGroupForUser maps to the 'family token rejected: ' prefixed message" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> unauthorizedResponse ()))
            let result = Steam.getFamilyGroupForUser http "stale-token" |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringStarts msg "family token rejected: " "Prefixed with the fixed, typed marker"
            | Ok _ -> failtest "Expected an Error for a rejected token"

        testCase "a 403 from GetFamilyGroupForUser also maps to the same prefixed message" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.Forbidden)))
            let result = Steam.getFamilyGroupForUser http "stale-token" |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.equal msg Steam.familyTokenRejectedMessage "Same fixed message as the 401 case"
            | Ok _ -> failtest "Expected an Error for a rejected token"

        testCase "a non-auth failure does not carry the family-token-rejected prefix" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.InternalServerError)))
            let result = Steam.getFamilyGroupForUser http "some-token" |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.isFalse (msg.StartsWith("family token rejected: ")) "A 500 is not a token-rejection"
            | Ok _ -> failtest "Expected an Error for HTTP 500"

        testCase "the family-token rejection message shares no prefix with the Web API key rejection message (ADR-0065 rule 3)" <| fun _ ->
            let familyPrefix = Steam.familyTokenRejectedMessage.Split(':').[0]
            let webApiPrefix = Steam.webApiKeyRejectedMessage.Split('(').[0].Trim()
            Expect.isFalse (Steam.familyTokenRejectedMessage.StartsWith(webApiPrefix)) "Family message doesn't start with the Web API key message's wording"
            Expect.isFalse (Steam.webApiKeyRejectedMessage.StartsWith(familyPrefix)) "Web API key message doesn't start with the family message's wording"
            Expect.notEqual familyPrefix webApiPrefix "The two credentials' failures use textually distinct prefixes"

        testCase "family import with an empty steam_family_token returns 'not configured' with no HTTP call" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let mutable httpCallMade = false
            let http =
                new HttpClient(new StubHandler(fun req ->
                    httpCallMade <- true
                    unauthorizedResponse ()))
            let api = createApi db.Factory http
            let result = api.importSteamFamily () |> Async.RunSynchronously
            Expect.equal result (Error "Steam Family access token not configured") "Empty token short-circuits without any HTTP call"
            Expect.isFalse httpCallMade "No HTTP call was made"

        testCase "family member fetch with an empty steam_family_token returns 'not configured' with no HTTP call" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let mutable httpCallMade = false
            let http =
                new HttpClient(new StubHandler(fun req ->
                    httpCallMade <- true
                    unauthorizedResponse ()))
            let api = createApi db.Factory http
            let result = api.fetchSteamFamilyMembers () |> Async.RunSynchronously
            Expect.equal result (Error "Steam Family access token not configured") "Empty token short-circuits without any HTTP call"
            Expect.isFalse httpCallMade "No HTTP call was made"
    ]

[<Tests>]
let startupDeletesRetiredRefreshTokenTests =
    testList "Composition.deleteRetiredSteamRefreshToken (integration-v0xmv, ADR-0070)" [

        testCase "a stored steam_family_refresh_token is deleted" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_refresh_token" "a-live-steam-credential"
            Expect.isSome (SettingsStore.getSetting db.Connection "steam_family_refresh_token") "sanity: the setting was seeded"
            Composition.deleteRetiredSteamRefreshToken db.Connection
            Expect.isNone (SettingsStore.getSetting db.Connection "steam_family_refresh_token") "The retired credential is gone after startup cleanup"

        testCase "no stored token is a no-op, not an error" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            Composition.deleteRetiredSteamRefreshToken db.Connection
            Expect.isNone (SettingsStore.getSetting db.Connection "steam_family_refresh_token") "Still absent, cleanup didn't throw or otherwise misbehave"

        testCase "other Steam settings are untouched" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_refresh_token" "a-live-steam-credential"
            SettingsStore.setSetting db.Connection "steam_family_token" "the-pasted-access-token"
            Composition.deleteRetiredSteamRefreshToken db.Connection
            Expect.isNone (SettingsStore.getSetting db.Connection "steam_family_refresh_token") "The refresh token is gone"
            Expect.equal (SettingsStore.getSetting db.Connection "steam_family_token") (Some "the-pasted-access-token") "The unrelated pasted access token survives"
    ]
