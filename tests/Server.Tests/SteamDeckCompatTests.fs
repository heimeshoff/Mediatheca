module Mediatheca.Tests.SteamDeckCompatTests

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Mediatheca.Server
open Mediatheca.Shared

/// games-b8xnw: `ajaxgetdeckappcompatibilityreport` (the endpoint the task
/// was framed around) is verified dead — see `Steam.fs`'s module doc
/// comment. These tests exercise the replacement: the
/// `data-hardwarecompatibility="{...}"` attribute embedded in each store
/// app page's HTML, using fragments shaped exactly like the real,
/// live-fetched attribute value for Hades (verified 2026-08-04).

/// A minimal HTML fragment carrying the real attribute shape (HTML-entity
/// encoded JSON), trimmed to the fields `decodeDeckCompatFromHtml` reads —
/// captures the load-bearing shape (entity-encoded quotes, nested arrays)
/// without reproducing the full multi-KB live page.
let private hardwareCompatHtml (resolvedCategory: int) : string =
    sprintf
        """<html><body><div data-hardwarecompatibility="{&quot;appid&quot;:1145360,&quot;resolved_category&quot;:%d,&quot;resolved_items&quot;:[{&quot;display_type&quot;:4,&quot;loc_token&quot;:&quot;#SteamDeckVerified_TestResult_DefaultControllerConfigFullyFunctional&quot;}],&quot;steam_deck_blog_url&quot;:&quot;&quot;,&quot;steamos_resolved_category&quot;:2}"></div></body></html>"""
        resolvedCategory

type private StubHttpMessageHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

[<Tests>]
let decodeTests =
    testList "Steam.decodeDeckCompatFromHtml (games-b8xnw)" [

        testCase "Extracts resolved_category from a data-hardwarecompatibility attribute" <| fun _ ->
            let html = hardwareCompatHtml 3
            let result = Steam.decodeDeckCompatFromHtml html
            Expect.equal result (Ok 3) "resolved_category read out of the HTML-entity-decoded JSON attribute"

        testCase "No data-hardwarecompatibility attribute at all is a clear Error, not an exception" <| fun _ ->
            let result = Steam.decodeDeckCompatFromHtml "<html><body>age check page, no attribute here</body></html>"
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an Error when the attribute is missing"

        testCase "Malformed JSON inside the attribute is a clear Error, not an exception" <| fun _ ->
            let html = """<div data-hardwarecompatibility="{&quot;resolved_category&quot;:not-json}"></div>"""
            let result = Steam.decodeDeckCompatFromHtml html
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an Error for malformed JSON"
    ]

[<Tests>]
let mapTests =
    testList "Steam.mapDeckCompatCategory (games-b8xnw, live-verified 2026-08-04)" [

        testCase "3 -> Verified (Hades/Valheim/Elden Ring, live-verified)" <| fun _ ->
            Expect.equal (Steam.mapDeckCompatCategory 3) Verified "category 3"

        testCase "2 -> Playable (Elite Dangerous/Counter-Strike 2, live-verified)" <| fun _ ->
            Expect.equal (Steam.mapDeckCompatCategory 2) Playable "category 2"

        testCase "1 -> Unsupported (Beat Saber, live-verified)" <| fun _ ->
            Expect.equal (Steam.mapDeckCompatCategory 1) Unsupported "category 1"

        testCase "0 -> Unknown" <| fun _ ->
            Expect.equal (Steam.mapDeckCompatCategory 0) Unknown "category 0"

        testCase "Any unrecognized value degrades to Unknown, never a fabricated guess" <| fun _ ->
            Expect.equal (Steam.mapDeckCompatCategory 99) Unknown "out-of-range category"
    ]

[<Tests>]
let fetchTests =
    testList "Steam.getDeckCompatibility (games-b8xnw)" [

        testCase "Fetches the store page and decodes+maps the verdict" <| fun _ ->
            let handler = new StubHttpMessageHandler(fun _ ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                response.Content <- new StringContent(hardwareCompatHtml 3)
                response)
            let httpClient = new HttpClient(handler)
            let result = Steam.getDeckCompatibility httpClient 1145360 |> Async.RunSynchronously
            Expect.equal result (Ok Verified) "resolved_category 3 decodes to Verified"

        testCase "Sends the age-gate cookie on every request" <| fun _ ->
            let mutable capturedCookie = None
            let handler = new StubHttpMessageHandler(fun request ->
                capturedCookie <-
                    match request.Headers.TryGetValues("Cookie") with
                    | true, values -> Some (values |> Seq.head)
                    | false, _ -> None
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                response.Content <- new StringContent(hardwareCompatHtml 1)
                response)
            let httpClient = new HttpClient(handler)
            Steam.getDeckCompatibility httpClient 1245620 |> Async.RunSynchronously |> ignore
            match capturedCookie with
            | Some cookie ->
                Expect.stringContains cookie "wants_mature_content=1" "Age-gate cookie is sent so Mature-rated titles don't 302 to /agecheck/"
            | None -> failtest "Expected a Cookie header on the request"

        testCase "A missing attribute (e.g. an age-check redirect page slipping through) is a clear Error" <| fun _ ->
            let handler = new StubHttpMessageHandler(fun _ ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                response.Content <- new StringContent("<html><body>no attribute</body></html>")
                response)
            let httpClient = new HttpClient(handler)
            let result = Steam.getDeckCompatibility httpClient 1145360 |> Async.RunSynchronously
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an Error"
    ]
