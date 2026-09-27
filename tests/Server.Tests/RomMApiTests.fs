module Mediatheca.Tests.RomMApiTests

/// integration-jkbm1 (ADR-0088), iteration 2: coverage for criterion 1 (the
/// Settings card's persistence contract), which iteration 1 shipped with no
/// test at all -- `setRomMSettings`/`getRomMSettings` round-trip through
/// `SettingsStore` exactly like `steam_family_members` does (a JSON array for
/// `romm_platform_ids`, the same shape `Composition.getRomMConfig` parses),
/// and the API token is proven to never round-trip back to the client. Also
/// covers `fetchRomMPlatforms` clearing a standing `romm_last_error` on a
/// successful call -- the iteration-1 verifier's "minor" finding against
/// `Api.fs`.

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
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

let private unauthorizedResponse () = new HttpResponseMessage(HttpStatusCode.Unauthorized)

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: Microsoft.Data.Sqlite.SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ GameProjection.handler; PlaySessionProjection.handler ]

/// Mirrors `Composition.getRomMConfig` exactly (same re-read-every-call,
/// JSON-array-parsing shape) -- `fetchRomMPlatforms` is exercised through
/// this closure, the same way `Api.create`'s caller wires the real one in
/// `Composition.fs`.
let private getRomMConfig (factory: unit -> Microsoft.Data.Sqlite.SqliteConnection) () : RomM.RomMConfig =
    use conn = factory ()
    let platformIds =
        SettingsStore.getSetting conn "romm_platform_ids"
        |> Option.bind (fun json ->
            match Thoth.Json.Net.Decode.fromString (Thoth.Json.Net.Decode.list Thoth.Json.Net.Decode.int) json with
            | Ok ids -> Some ids
            | Error _ -> None)
        |> Option.defaultValue []
    { BaseUrl = SettingsStore.getSetting conn "romm_base_url" |> Option.defaultValue ""
      ApiToken = SettingsStore.getSetting conn "romm_api_token" |> Option.defaultValue ""
      SelectedPlatformIds = platformIds }

let private createApi (factory: unit -> Microsoft.Data.Sqlite.SqliteConnection) (httpClient: HttpClient) : IMediathecaApi =
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
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        (fun () -> async { return Error "not wired in tests" })
        (getRomMConfig factory)
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        "" // integration-t4q7k: pdfBasePath, unused in this test
        allProjectionHandlers

[<Tests>]
let rommApiTests =
    testList "RomM Settings persistence (integration-jkbm1, ADR-0088, iteration 2)" [

        testCase "setRomMSettings persists base URL, sync hour, and platform ids as a JSON array; getRomMSettings reads it back" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            let request : SetRomMSettingsRequest =
                { BaseUrl = "https://romm.example.com"
                  ApiToken = Some "rmm_supersecrettoken0000000000000000000000000000000000000000000"
                  SelectedPlatformIds = [ 1; 3; 7 ]
                  SyncHour = 11 }
            match api.setRomMSettings request |> Async.RunSynchronously with
            | Ok () -> ()
            | Error e -> failtestf "Expected the save to succeed, got %s" e

            // Persisted verbatim as a JSON array -- the same shape
            // `steam_family_members` uses for its own list-valued setting,
            // and the exact shape `Composition.getRomMConfig`'s parser reads.
            Expect.equal (SettingsStore.getSetting db.Connection "romm_platform_ids") (Some "[1,3,7]") "romm_platform_ids is a JSON array"

            let settings = api.getRomMSettings () |> Async.RunSynchronously
            Expect.equal settings.BaseUrl "https://romm.example.com" "Base URL round-trips"
            Expect.equal settings.SelectedPlatformIds [ 1; 3; 7 ] "Selected platform ids round-trip, order preserved"
            Expect.equal settings.SyncHour 11 "Sync hour round-trips"
            Expect.isTrue settings.TokenConfigured "TokenConfigured is true once a non-empty token is saved"

        testCase "getRomMSettings never returns the raw API token" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            let secretToken = "rmm_thisisaverysecrettokenthatmustneverreachtheclient00000000000"
            let request : SetRomMSettingsRequest =
                { BaseUrl = "https://romm.example.com"; ApiToken = Some secretToken; SelectedPlatformIds = [ 1 ]; SyncHour = 9 }
            api.setRomMSettings request |> Async.RunSynchronously |> ignore
            let settings = api.getRomMSettings () |> Async.RunSynchronously
            // `RomMSettingsDto` carries no `ApiToken` field at all -- the
            // surest proof it never round-trips. Pinned by asserting the
            // DTO's own printed form never contains the token value, so a
            // future field accidentally populated with it would fail here.
            Expect.isTrue settings.TokenConfigured "Only the boolean flag is exposed"
            Expect.isFalse ((sprintf "%A" settings).Contains("thisisaverysecrettoken")) "The raw token text never appears anywhere in the returned DTO"
            Expect.equal (SettingsStore.getSetting db.Connection "romm_api_token") (Some secretToken) "sanity: the token IS stored server-side, just never exposed via the DTO"

        testCase "setRomMSettings with ApiToken = None keeps the previously-saved token (mirrors Audible/Steam's 'blank keeps existing' convention)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            api.setRomMSettings
                { BaseUrl = "https://romm.example.com"; ApiToken = Some "rmm_originaltoken00000000000000000000000000000000000000000000000"; SelectedPlatformIds = [ 1 ]; SyncHour = 9 }
            |> Async.RunSynchronously |> ignore
            api.setRomMSettings
                { BaseUrl = "https://romm.example.com"; ApiToken = None; SelectedPlatformIds = [ 1; 2 ]; SyncHour = 10 }
            |> Async.RunSynchronously |> ignore
            let settings = api.getRomMSettings () |> Async.RunSynchronously
            Expect.isTrue settings.TokenConfigured "The original token is still configured"
            Expect.equal settings.SelectedPlatformIds [ 1; 2 ] "But the platform ids and sync hour still updated"
            Expect.equal settings.SyncHour 10 "Sync hour updated"

        testCase "an empty/unset romm_platform_ids degrades to an empty selection, matching Composition.getRomMConfig's parser" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            let settings = api.getRomMSettings () |> Async.RunSynchronously
            Expect.equal settings.SelectedPlatformIds [] "No setting saved yet -- empty selection, not an error"
            Expect.isFalse settings.TokenConfigured "No token saved yet"

        testCase "fetchRomMPlatforms clears a standing romm_last_error on a successful call" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let setupApi = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            setupApi.setRomMSettings
                { BaseUrl = "https://romm.example.com"; ApiToken = Some "rmm_atoken000000000000000000000000000000000000000000000000000"; SelectedPlatformIds = []; SyncHour = 9 }
            |> Async.RunSynchronously |> ignore
            SettingsStore.setSetting db.Connection "romm_last_error" RomM.tokenRejectedMessage
            Expect.isSome (SettingsStore.getSetting db.Connection "romm_last_error") "sanity: the notice was seeded"

            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse """[{"id":1,"name":"Nintendo Switch","slug":"switch"}]"""))
            let api = createApi db.Factory http
            match api.fetchRomMPlatforms () |> Async.RunSynchronously with
            | Ok platforms -> Expect.equal (List.length platforms) 1 "The platform list still decodes"
            | Error e -> failtestf "Expected the fetch to succeed, got %s" e
            Expect.isNone (SettingsStore.getSetting db.Connection "romm_last_error") "A successful call clears the standing notice"

        testCase "fetchRomMPlatforms sets romm_last_error on a 401" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let setupApi = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> jsonResponse "[]")))
            setupApi.setRomMSettings
                { BaseUrl = "https://romm.example.com"; ApiToken = Some "rmm_atoken000000000000000000000000000000000000000000000000000"; SelectedPlatformIds = []; SyncHour = 9 }
            |> Async.RunSynchronously |> ignore

            let api401 = createApi db.Factory (new HttpClient(new StubHandler(fun _ -> unauthorizedResponse ())))
            match api401.fetchRomMPlatforms () |> Async.RunSynchronously with
            | Error msg -> Expect.stringStarts msg "RomM token rejected: " "The typed rejection message"
            | Ok _ -> failtest "Expected a 401 to be reported as an error"
            Expect.isSome (SettingsStore.getSetting db.Connection "romm_last_error") "Persisted after a 401"
    ]
