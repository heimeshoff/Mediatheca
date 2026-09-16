module Mediatheca.Tests.AudibleTests

/// integration-dhctm (ADR-0074): the Audible adapter. `validateAuthFile` /
/// `marketplaceHost` / `refreshAccessToken` / `withAccessToken` are the
/// no-login, imported-auth-file token orchestration; `searchCatalog` /
/// `getProduct` / `Audnexus.getBook` are the unauthenticated catalog side.
/// All HTTP is faked via a recording `HttpMessageHandler`, same pattern as
/// `QbittorrentTests.fs` / `OpenLibraryTests.fs`.
///
/// The "no registration path exists" acceptance criterion
/// (`grep -rn "auth/register\|from_login\|device_registration" src/Server/Audible.fs`)
/// is deliberately NOT a unit test here — the task's own instructions say
/// the grep itself is the verification, and a reflection-based "no such
/// member" test would be fragile for what the grep already proves.

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Mediatheca.Server
open Mediatheca.Server.Audible

type private RecordingHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let requests = ResizeArray<HttpRequestMessage>()
    let bodies = ResizeArray<string>()
    member _.Requests : HttpRequestMessage list = requests |> List.ofSeq
    member _.RequestBodies : string list = bodies |> List.ofSeq
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        requests.Add(request)
        let body =
            match request.Content with
            | null -> ""
            | content -> content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        bodies.Add(body)
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (status: HttpStatusCode) (json: string) =
    let resp = new HttpResponseMessage(status)
    resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    resp

let private fullAuthFileJson =
    """
    {
        "adp_token": "adp-token-value",
        "device_private_key": "device-private-key-value",
        "access_token": "starting-access-token",
        "refresh_token": "refresh-token-value",
        "expires": 1700000000.0,
        "locale_code": "de",
        "customer_info": { "name": "Marco H", "user_id": "amzn1.account.ABC123" },
        "website_cookies": {},
        "store_authentication_cookie": {},
        "device_info": { "device_name": "test-device" }
    }
    """

let private sampleAuthFile : AudibleAuthFile = {
    RefreshToken = "refresh-token-value"
    AdpToken = "adp-token-value"
    DevicePrivateKey = "device-private-key-value"
    LocaleCode = "de"
    CustomerName = "Marco H"
    CustomerUserId = Some "amzn1.account.ABC123"
}

[<Tests>]
let audibleAuthFileTests =
    testList "Audible auth file (integration-dhctm, ADR-0074)" [

        testCase "validateAuthFile accepts a fixture carrying every field" <| fun _ ->
            match validateAuthFile fullAuthFileJson with
            | Ok authFile -> Expect.equal authFile sampleAuthFile "Decodes the fields this adapter actually uses"
            | Error e -> failtestf "Expected Ok, got Error %s" e

        testCase "validateAuthFile rejects a file missing refresh_token, naming the field" <| fun _ ->
            let missingRefreshToken =
                """{"adp_token": "x", "device_private_key": "y", "locale_code": "de", "customer_info": {"name": "Marco"}}"""
            match validateAuthFile missingRefreshToken with
            | Error e -> Expect.stringContains e "refresh_token" "The decode error names the missing field"
            | Ok _ -> failtest "Expected Error for a file missing refresh_token"

        testCase "validateAuthFile rejects non-JSON" <| fun _ ->
            match validateAuthFile "not json at all" with
            | Error _ -> ()
            | Ok _ -> failtest "Expected Error for non-JSON input"

        testCase "marketplaceHost \"de\" = api.audible.de" <| fun _ ->
            Expect.equal (marketplaceHost "de") "api.audible.de" "The German marketplace host"

        testCase "amazonTokenHost \"de\" = api.amazon.de" <| fun _ ->
            Expect.equal (amazonTokenHost "de") "api.amazon.de" "The German Amazon OAuth token host"
    ]

[<Tests>]
let refreshAccessTokenTests =
    testList "Audible.refreshAccessToken" [

        testCase "sends the five mkb79 form fields to https://api.amazon.de/auth/token for locale_code \"de\", and returns the minted token + expiry" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.OK """{"access_token": "fresh-access-token", "expires_in": 3600}""")
            use http = new HttpClient(handler)
            let result = refreshAccessToken http sampleAuthFile |> Async.RunSynchronously
            match result with
            | Ok token ->
                Expect.equal token.AccessToken "fresh-access-token" "Decodes the minted access token"
                Expect.isTrue (token.ExpiresAt > DateTime.UtcNow.AddMinutes(50.0)) "expires_in=3600 puts the expiry roughly an hour out"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            Expect.equal handler.Requests.Length 1 "Exactly one request sent"
            let request = handler.Requests.[0]
            Expect.equal (request.RequestUri.ToString()) "https://api.amazon.de/auth/token" "Posts to the German Amazon token host for locale_code \"de\""
            Expect.equal request.Method HttpMethod.Post "POST, not GET"

            let body = handler.RequestBodies.[0]
            let formPairs = body.Split('&') |> Array.map (fun kv -> let parts = kv.Split('=') in (parts.[0], Uri.UnescapeDataString(parts.[1])))
            let formMap = Map.ofArray formPairs
            Expect.equal (formMap.TryFind "app_name") (Some "Audible") "app_name"
            Expect.equal (formMap.TryFind "app_version") (Some "3.56.2") "app_version"
            Expect.equal (formMap.TryFind "source_token") (Some "refresh-token-value") "source_token carries the auth file's refresh_token"
            Expect.equal (formMap.TryFind "requested_token_type") (Some "access_token") "requested_token_type"
            Expect.equal (formMap.TryFind "source_token_type") (Some "refresh_token") "source_token_type"

        testCase "a 401 response returns an Error beginning with the fixed \"audible auth file rejected: \" prefix" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.Unauthorized """{"error": "invalid_grant"}""")
            use http = new HttpClient(handler)
            let result = refreshAccessToken http sampleAuthFile |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.isTrue (msg.StartsWith(authFileRejectedPrefix)) (sprintf "Expected the fixed rejection prefix, got: %s" msg)
            | Ok _ -> failtest "Expected Error for a 401 response"
    ]

[<Tests>]
let withAccessTokenTests =
    testList "Audible.withAccessToken" [

        testCase "reuses a cached token with more than 5 minutes left -- zero refresh calls" <| fun _ ->
            let mutable refreshCalls = 0
            let refresh () = async { refreshCalls <- refreshCalls + 1; return Ok { AccessToken = "should-not-be-used"; ExpiresAt = DateTime.UtcNow.AddHours(1.0) } }
            let cached = Some ("cached-token", DateTime.UtcNow.AddMinutes(30.0))
            let fetch (token: string) = async { return (if token = "cached-token" then Ok "success" else Error (OtherFailure "wrong token")) }
            let result = withAccessToken cached refresh ignore fetch |> Async.RunSynchronously
            Expect.equal result (Ok "success") "Fetch used the cached token directly"
            Expect.equal refreshCalls 0 "No refresh call was made"

        testCase "refreshes once when no cached token is fresh enough, then fetches with the fresh token" <| fun _ ->
            let mutable refreshCalls = 0
            let mutable persisted = None
            let refresh () = async { refreshCalls <- refreshCalls + 1; return Ok { AccessToken = "fresh-token"; ExpiresAt = DateTime.UtcNow.AddHours(1.0) } }
            let persist (t: AudibleAccessToken) = persisted <- Some t
            let expiredCached = Some ("stale-token", DateTime.UtcNow.AddMinutes(1.0))
            let fetch (token: string) = async { return (if token = "fresh-token" then Ok "success" else Error (OtherFailure "wrong token")) }
            let result = withAccessToken expiredCached refresh persist fetch |> Async.RunSynchronously
            Expect.equal result (Ok "success") "Fetch used the freshly-minted token"
            Expect.equal refreshCalls 1 "Exactly one refresh call for an expired cached token"
            Expect.equal (persisted |> Option.map (fun t -> t.AccessToken)) (Some "fresh-token") "The fresh token was persisted"

        testCase "a 401 from fetch refreshes once and retries once; a second 401 surfaces AuthFileRejected wording with exactly one refresh call" <| fun _ ->
            let mutable refreshCalls = 0
            let refresh () = async { refreshCalls <- refreshCalls + 1; return Ok { AccessToken = "minted-again"; ExpiresAt = DateTime.UtcNow.AddHours(1.0) } }
            let cached = Some ("cached-token", DateTime.UtcNow.AddMinutes(30.0))
            let fetch (_token: string) = async { return Error Unauthorized }
            let result = withAccessToken cached refresh ignore fetch |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.isTrue (msg.StartsWith(authFileRejectedPrefix)) (sprintf "Expected the fixed rejection prefix, got: %s" msg)
            | Ok _ -> failtest "Expected Error after two consecutive 401s"
            Expect.equal refreshCalls 1 "Exactly one refresh call -- no retry loop"

        testCase "a non-auth fetch failure passes straight through without refreshing" <| fun _ ->
            let mutable refreshCalls = 0
            let refresh () = async { refreshCalls <- refreshCalls + 1; return Ok { AccessToken = "x"; ExpiresAt = DateTime.UtcNow.AddHours(1.0) } }
            let cached = Some ("cached-token", DateTime.UtcNow.AddMinutes(30.0))
            let fetch (_token: string) = async { return Error (OtherFailure "HTTP 500") }
            let result = withAccessToken cached refresh ignore fetch |> Async.RunSynchronously
            Expect.equal result (Error "HTTP 500") "Non-auth failures are not retried or turned into a rejection"
            Expect.equal refreshCalls 0 "No refresh call for a non-auth failure"
    ]

let private searchResponseJson =
    """
    {
        "products": [
            {
                "asin": "B002V5BNGY",
                "title": "Dune",
                "authors": [{ "name": "Frank Herbert" }],
                "narrators": [{ "name": "Simon Vance" }],
                "runtime_length_min": 1260,
                "release_date": "1987-01-01",
                "product_images": { "500": "https://m.media-amazon.com/images/dune-500.jpg" },
                "series": [{ "title": "Dune Chronicles" }]
            }
        ]
    }
    """

[<Tests>]
let searchCatalogTests =
    testList "Audible.searchCatalog" [

        testCase "decodes asin, title, authors, narrators, runtime and the 500px cover, unauthenticated (no Authorization header sent)" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.OK searchResponseJson)
            use http = new HttpClient(handler)
            let results = searchCatalog http "api.audible.com" (sprintf "dune-%s" (Guid.NewGuid().ToString("N"))) |> Async.RunSynchronously
            match results with
            | [ result ] ->
                Expect.equal result.Asin "B002V5BNGY" "asin"
                Expect.equal result.Title "Dune" "title"
                Expect.equal result.Authors [ "Frank Herbert" ] "authors"
                Expect.equal result.Narrators [ "Simon Vance" ] "narrators"
                Expect.equal result.RuntimeMinutes (Some 1260) "runtime"
                Expect.equal result.CoverUrl (Some "https://m.media-amazon.com/images/dune-500.jpg") "500px cover"
                Expect.equal result.SeriesName (Some "Dune Chronicles") "series name"
            | other -> failtestf "Expected exactly one result, got %A" other
            Expect.isFalse (handler.Requests.[0].Headers.Contains("Authorization")) "Catalog search never sends an Authorization header -- it works with no auth file stored"
    ]

let private productWithDescriptionJson =
    """
    {
        "product": {
            "asin": "B002V5BNGY",
            "title": "Dune",
            "authors": [{ "name": "Frank Herbert" }],
            "narrators": [{ "name": "Simon Vance" }],
            "publisher_name": "Macmillan Audio",
            "release_date": "1987-01-01",
            "runtime_length_min": 1260,
            "publisher_summary": "<p>Set on the desert planet Arrakis.</p>",
            "language": "english",
            "series": [{ "title": "Dune Chronicles", "sequence": "1" }],
            "category_ladders": [{ "ladder": [{ "name": "Science Fiction & Fantasy" }] }],
            "product_images": { "900": "https://m.media-amazon.com/images/dune-900.jpg" }
        }
    }
    """

let private productWithoutDescriptionJson =
    """
    {
        "product": {
            "asin": "B002V5BNGY",
            "title": "Dune",
            "authors": [{ "name": "Frank Herbert" }],
            "narrators": [],
            "runtime_length_min": 1260
        }
    }
    """

[<Tests>]
let getProductTests =
    testList "Audible.getProduct" [

        testCase "decodes the full product shape, stripping HTML from publisher_summary" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.OK productWithDescriptionJson)
            use http = new HttpClient(handler)
            match getProduct http "api.audible.de" "B002V5BNGY" |> Async.RunSynchronously with
            | Some product ->
                Expect.equal product.Title "Dune" "title"
                Expect.equal product.Description (Some "Set on the desert planet Arrakis.") "HTML-stripped description"
                Expect.equal product.SeriesPosition (Some 1) "sequence parsed to an int"
                Expect.equal product.Categories [ "Science Fiction & Fantasy" ] "top-level category names"
                Expect.equal product.CoverUrl (Some "https://m.media-amazon.com/images/dune-900.jpg") "900px cover"
            | None -> failtest "Expected Some product"

        testCase "a product lacking a description decodes with Description = None" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.OK productWithoutDescriptionJson)
            use http = new HttpClient(handler)
            match getProduct http "api.audible.de" "B002V5BNGY" |> Async.RunSynchronously with
            | Some product -> Expect.isNone product.Description "No publisher_summary in the fixture"
            | None -> failtest "Expected Some product"

        testCase "a 404 yields None, never an exception" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.NotFound))
            use http = new HttpClient(handler)
            let result = getProduct http "api.audible.de" "MISSING" |> Async.RunSynchronously
            Expect.isNone result "A 404 is None, not an exception"
    ]

[<Tests>]
let audnexusTests =
    testList "Audnexus.getBook" [

        testCase "fills description, narrators and series when the fixture carries them" <| fun _ ->
            let handler = new RecordingHandler(fun _ ->
                jsonResponse HttpStatusCode.OK
                    """{"summary": "A survival story.", "narrators": [{"name": "Simon Vance"}], "seriesPrimary": {"name": "Dune Chronicles", "position": "1"}}""")
            use http = new HttpClient(handler)
            match Audnexus.getBook http "B002V5BNGY" "de" |> Async.RunSynchronously with
            | Some book ->
                Expect.equal book.Description (Some "A survival story.") "description"
                Expect.equal book.Narrators [ "Simon Vance" ] "narrators"
                Expect.equal book.SeriesName (Some "Dune Chronicles") "series name"
                Expect.equal book.SeriesPosition (Some 1) "series position"
            | None -> failtest "Expected Some book"

        testCase "a 404 yields None, never a failure" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.NotFound))
            use http = new HttpClient(handler)
            let result = Audnexus.getBook http "MISSING" "de" |> Async.RunSynchronously
            Expect.isNone result "A 404 is None, never a hard dependency failure"
    ]

[<Tests>]
let getCustomerSummaryTests =
    testList "Audible.getCustomerSummary (the \"Test connection\" probe)" [

        testCase "sends a Bearer Authorization header and client-id: 0, and reports the library's total count" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> jsonResponse HttpStatusCode.OK """{"total_results": 42}""")
            use http = new HttpClient(handler)
            let result = getCustomerSummary http "api.audible.de" "my-access-token" |> Async.RunSynchronously
            Expect.equal result (Ok "42 titles") "Reports the total from the response"
            let request = handler.Requests.[0]
            Expect.equal (request.Headers.GetValues("Authorization") |> Seq.head) "Bearer my-access-token" "Bearer token"
            Expect.equal (request.Headers.GetValues("client-id") |> Seq.head) "0" "client-id: 0"

        testCase "a 401 maps to Unauthorized (the withAccessToken retry trigger)" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.Unauthorized))
            use http = new HttpClient(handler)
            let result = getCustomerSummary http "api.audible.de" "rejected-token" |> Async.RunSynchronously
            Expect.equal result (Error Unauthorized) "401 is the retry trigger, not a generic failure"
    ]
