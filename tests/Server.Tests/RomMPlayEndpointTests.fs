module Mediatheca.Tests.RomMPlayEndpointTests

/// integration-f8ncw (ADR-0088): HTTP-level coverage for `Api.
/// rommPlayHandler`/`GET /api/romm/play/{romId}`, wired exactly as
/// `Composition.fs` wires it -- via `Giraffe.routef` inside `choose`,
/// through a minimal `TestServer` (mirrors `PdfServingTests.fs`'s own
/// "never the full `Composition.buildApp` pipeline" shape). `romm_rom_
/// platform` is seeded directly via `GameProjection.upsertRommRomPlatform`
/// (no event stream needed -- that table isn't event-sourced, see its own
/// doc comment). RomM itself is a stubbed `HttpMessageHandler`, the same
/// shape `RomMTests.fs` uses for `getLatestState` -- this file is about the
/// endpoint's REDIRECT decision, not the adapter's decoding (already
/// covered there).

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Giraffe
open Mediatheca.Server

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, Text.Encoding.UTF8, "application/json")
    resp

let private config (baseUrl: string) : RomM.RomMConfig =
    { BaseUrl = baseUrl; ApiToken = "rmm_test_token"; SelectedPlatformIds = [] }

/// Two states, same shape as `RomMTests.fs`'s `statesFixture` -- id 12 is
/// the newer of the two by `updated_at`.
let private statesFixture =
    """[{"id":11,"rom_id":33,"user_id":1,"file_name":"slot1.state","file_name_no_tags":"slot1","file_name_no_ext":"slot1","file_extension":"state","file_path":"states/33","file_size_bytes":123456,"full_path":"states/33/slot1.state","download_path":"/assets/romm/states/33/slot1.state","missing_from_fs":false,"created_at":"2026-09-20T10:00:00","updated_at":"2026-09-20T10:00:00","emulator":"snes9x","is_public":false,"screenshot":null},{"id":12,"rom_id":33,"user_id":1,"file_name":"slot2.state","file_name_no_tags":"slot2","file_name_no_ext":"slot2","file_extension":"state","file_path":"states/33","file_size_bytes":123999,"full_path":"states/33/slot2.state","download_path":"/assets/romm/states/33/slot2.state","missing_from_fs":false,"created_at":"2026-09-25T10:00:00","updated_at":"2026-09-25T10:00:00","emulator":"snes9x","is_public":false,"screenshot":null}]"""

let private testServer (factory: unit -> Microsoft.Data.Sqlite.SqliteConnection) (httpClient: HttpClient) (getRomMConfig: unit -> RomM.RomMConfig) : TestServer =
    let webApp =
        choose [
            routef "/api/romm/play/%i" (fun romId -> Api.rommPlayHandler factory httpClient getRomMConfig romId)
        ]
    let hostBuilder =
        (new WebHostBuilder())
            .Configure(fun app -> app.UseGiraffe webApp)
            .ConfigureServices(fun services -> services.AddGiraffe() |> ignore)
    new TestServer(hostBuilder)

/// A `HttpClient` whose handler never gets invoked -- for the "no states
/// call at all" assertions (Ruffle/js-dos/PICO-8, and the 404 cases).
let private neverCalledHttpClient () : HttpClient =
    new HttpClient(new StubHandler(fun _ -> failtest "RomM must not be called for this platform"))

/// `TestServer.CreateClient()`'s handler talks to the in-process pipeline
/// directly (`ClientHandler`, no real socket/navigation involved) -- there
/// is no "follow the redirect" step to disable, unlike a real `HttpClient`
/// hitting a live server. Every assertion here reads the raw 302 response
/// + its `Location` header.
let private noRedirectClient (server: TestServer) : HttpClient =
    server.CreateClient()

let private bootstrap (conn: Microsoft.Data.Sqlite.SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

[<Tests>]
let rommPlayEndpointTests =
    testList "Api.rommPlayHandler -- GET /api/romm/play/{romId} (integration-f8ncw, ADR-0088)" [

        testCase "an EmulatorJS rom with states redirects to the console play route with the newest state id" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse statesFixture))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal response.StatusCode HttpStatusCode.Found "302 redirect"
            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/console/rom/33/play?state=12" "Redirects to the console play route with the NEWEST state (id 12, later updated_at)"

        testCase "an EmulatorJS rom with no states redirects to the console play route with no state param" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse "[]"))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/console/rom/33/play" "No states -- a fresh boot, no ?state="

        testCase "an EmulatorJS rom falls back to a fresh boot when the states call fails" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.InternalServerError)))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/console/rom/33/play" "A RomM problem never blocks Play -- fresh boot instead"

        testCase "an EmulatorJS rom falls back to a fresh boot when the states response does not parse" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse "{not valid json"))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/console/rom/33/play" "An unparsable states body also falls back to a fresh boot"

        testCase "a trailing slash on romm_base_url does not produce a double slash in the console play redirect" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse "[]"))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com/")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/console/rom/33/play" "Trailing slash trimmed -- no double slash before /console/"

        testCase "the redirect Location never contains the RomM API token" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse statesFixture))
            use server = testServer db.Factory http (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.isFalse ((response.Headers.Location.ToString()).Contains("rmm_test_token")) "The API token stays server-side, in the Authorization header of the RomM call -- never in the client-facing redirect"

        testCase "a Ruffle rom redirects straight to {base}/rom/{id}/ruffle and never calls RomM" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 50 "flash"
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/50") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/rom/50/ruffle" "Ruffle keeps the existing direct rom route"

        testCase "a js-dos rom redirects straight to {base}/rom/{id}/jsdos and never calls RomM" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 51 "win9x"
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/51") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/rom/51/jsdos" "js-dos keeps the existing direct rom route"

        testCase "a PICO-8 rom redirects straight to {base}/rom/{id}/pico8 and never calls RomM" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 52 "pico"
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/52") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal (response.Headers.Location.ToString()) "https://romm.example.com/rom/52/pico8" "PICO-8 keeps the existing direct rom route"

        testCase "404s when the rom's platform has no in-browser player (e.g. Switch)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 60 "switch"
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/60") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal response.StatusCode HttpStatusCode.NotFound "Switch has no in-browser player -- 404, never a partial redirect"

        testCase "404s when no romm_rom_platform row exists for the rom id" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "https://romm.example.com")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/999") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal response.StatusCode HttpStatusCode.NotFound "No recorded platform for this rom id -- 404"

        testCase "404s when romm_base_url is not configured" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            GameProjection.upsertRommRomPlatform db.Connection 33 "snes"
            use server = testServer db.Factory (neverCalledHttpClient ()) (fun () -> config "")
            use client = noRedirectClient server

            let response = client.GetAsync("/api/romm/play/33") |> Async.AwaitTask |> Async.RunSynchronously

            Expect.equal response.StatusCode HttpStatusCode.NotFound "No romm_base_url -- 404, never a partial redirect"
    ]
