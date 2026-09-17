module Mediatheca.Tests.AudibleApiTests

/// integration-dhctm (ADR-0074): `IMediathecaApi`'s Audible surface --
/// `addBookFromAudible` (turns a catalog hit into a Book with its
/// `book_metadata_cache` slice filled, Audnexus as the description
/// fallback), `getAudibleStatus` (never the auth file or any token),
/// `setAudibleAuthFile`/`testAudibleConnection` (persisting/clearing
/// `audible_last_error` and the minted access token), and
/// `searchAudibleBooks` (works with no auth file at all). No live Audible
/// call -- every request goes through a stub `HttpMessageHandler`.

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
    NotesProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn
    SettingsStore.initialize conn

let private allProjectionHandlers =
    [ BookProjection.handler ]

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

/// A live `createApi` that also uses the real `Composition`-shaped config
/// (reading `SettingsStore` on every call) -- used by the tests that save an
/// auth file via the API and then need the NEXT call to see it, exactly the
/// way `Composition.getAudibleConfig` re-reads on every request.
let private createApiWithDbBackedConfig (db: TestDb.TempDb) (httpClient: HttpClient) (imageBasePath: string) : IMediathecaApi =
    let getAudibleConfig () : Audible.AudibleConfig =
        use conn = db.Factory ()
        let authFile =
            SettingsStore.getSetting conn "audible_auth_file"
            |> Option.bind (fun json -> match Audible.validateAuthFile json with Ok a -> Some a | Error _ -> None)
        let marketplace =
            authFile |> Option.map (fun a -> a.LocaleCode) |> Option.orElse (SettingsStore.getSetting conn "audible_marketplace") |> Option.defaultValue "de"
        let cachedToken = SettingsStore.getSetting conn "audible_access_token"
        let cachedExpiresAt =
            SettingsStore.getSetting conn "audible_access_token_expires"
            |> Option.bind (fun s -> match DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) with true, d -> Some d | _ -> None)
        { AuthFile = authFile; Marketplace = marketplace; CachedAccessToken = cachedToken; CachedAccessTokenExpiresAt = cachedExpiresAt }
    createApi db.Factory httpClient imageBasePath getAudibleConfig

let private noAuthFileConfig : Audible.AudibleConfig =
    { AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None }

let private withTempImageDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-audible-test-images-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

let private productWithoutDescriptionJson =
    """
    {
        "product": {
            "asin": "B002V5BNGY",
            "title": "Dune",
            "authors": [{ "name": "Frank Herbert" }],
            "narrators": [],
            "release_date": "1987-01-01",
            "runtime_length_min": 1260,
            "product_images": { "900": "https://m.media-amazon.com/images/dune-900.jpg" }
        }
    }
    """

let private audnexusFallbackJson =
    """{"summary": "A survival story on the desert planet Arrakis.", "narrators": [{"name": "Simon Vance"}], "seriesPrimary": {"name": "Dune Chronicles", "position": "1"}}"""

let private httpClientFor () : HttpClient =
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                if url.Contains("m.media-amazon.com") then
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
                elif url.Contains("api.audnex.us") then
                    return jsonResponse HttpStatusCode.OK audnexusFallbackJson
                elif url.Contains("/1.0/catalog/products/B002V5BNGY") then
                    return jsonResponse HttpStatusCode.OK productWithoutDescriptionJson
                else
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
            })
    new HttpClient(handler)

let private sampleRequest : AddBookFromAudibleRequest = { Asin = "B002V5BNGY"; SkipDuplicateCheck = false }

[<Tests>]
let addBookFromAudibleTests =
    testList "IMediathecaApi.addBookFromAudible (integration-dhctm)" [

        testCase "creates a book with Format=Audiobook, AudibleAsin linked, a downloaded cover, and the cache slice's runtime_minutes/narrators filled via the Audnexus fallback" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientFor ()) imageBasePath (fun () -> noAuthFileConfig)

                let result = api.addBookFromAudible sampleRequest |> Async.RunSynchronously
                let slug =
                    match result with
                    | Ok (AddBookOutcome.Book_added slug) -> slug
                    | other -> failtestf "Expected Book_added; got %A" other

                let book =
                    match BookProjection.getBySlug db.Connection slug with
                    | Some b -> b
                    | None -> failtest "Expected the book to be projected"

                Expect.equal book.Format BookFormat.Audiobook "Format is Audiobook"
                Expect.equal book.AudibleAsin (Some "B002V5BNGY") "AudibleAsin external id linked"
                Expect.equal book.Title "Dune" "Title comes from the Audible product"
                Expect.equal book.CoverRef (Some (sprintf "posters/book-%s.jpg" slug)) "Cover ref recorded"
                Expect.isTrue (File.Exists(Path.Combine(imageBasePath, sprintf "posters/book-%s.jpg" slug))) "Cover file actually downloaded to disk"
                Expect.equal book.RuntimeMinutes (Some 1260) "runtime_minutes written to the cache slice, from the product itself"
                Expect.equal book.Narrators [ "Simon Vance" ] "narrators filled by the Audnexus fallback (the product's own narrators list was empty)"
                Expect.equal book.Description (Some "A survival story on the desert planet Arrakis.") "description filled by the Audnexus fallback (the product lacked one)"
                Expect.equal book.SeriesName (Some "Dune Chronicles") "series name filled by the Audnexus fallback"

                let second = api.addBookFromAudible sampleRequest |> Async.RunSynchronously
                match second with
                | Ok (AddBookOutcome.Duplicate_found _) -> ()
                | other -> failtestf "Expected Duplicate_found on the second call; got %A" other)

        testCase "an unknown ASIN (404 from the catalog) is a clear error, never a crash" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientFor ()) imageBasePath (fun () -> noAuthFileConfig)
                let result = api.addBookFromAudible { Asin = "MISSING"; SkipDuplicateCheck = false } |> Async.RunSynchronously
                match result with
                | Error msg -> Expect.stringContains msg "MISSING" "Names the ASIN that was not found"
                | Ok other -> failtestf "Expected Error for an unknown ASIN; got Ok %A" other)
    ]
    |> testSequenced

[<Tests>]
let audibleStatusSecrecyTests =
    testList "IMediathecaApi.getAudibleStatus never leaks the auth file or any token (ADR-0074 point 6)" [

        testCase "after saving a valid auth file, getAudibleStatus reports only Configured/CustomerName/Marketplace/LastError -- never the raw file" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApiWithDbBackedConfig db (new HttpClient()) "test-fixtures-do-not-exist/images"
            let authFileJson =
                """{"adp_token": "adp-secret", "device_private_key": "device-secret", "refresh_token": "refresh-secret", "locale_code": "de", "customer_info": {"name": "Marco H"}}"""

            let saveResult = api.setAudibleAuthFile authFileJson |> Async.RunSynchronously
            match saveResult with
            | Ok status -> Expect.equal status.CustomerName (Some "Marco H") "Save reports the customer name"
            | Error e -> failtestf "Expected the auth file to validate, got Error %s" e

            let status = api.getAudibleStatus () |> Async.RunSynchronously
            Expect.isTrue status.Configured "Configured is true once a valid auth file is stored"
            Expect.equal status.CustomerName (Some "Marco H") "Customer name surfaced"
            Expect.equal status.Marketplace "de" "Marketplace derived from locale_code"
            Expect.isNone status.LastError "No standing error yet"

            // The DTO's own JSON never carries the auth file's secrets --
            // asserted structurally (the type has no such field) and by
            // string search on the encoded response, belt and suspenders.
            let encoded = Thoth.Json.Net.Encode.Auto.toString (0, status)
            Expect.isFalse (encoded.Contains("adp-secret")) "adp_token never appears in the status response"
            Expect.isFalse (encoded.Contains("device-secret")) "device_private_key never appears in the status response"
            Expect.isFalse (encoded.Contains("refresh-secret")) "refresh_token never appears in the status response"
    ]

[<Tests>]
let testAudibleConnectionTests =
    testList "IMediathecaApi.testAudibleConnection (ADR-0074 points 3/4)" [

        testCase "a stubbed 401 from the token endpoint persists audible_last_error with the fixed rejection prefix" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        let url = req.RequestUri.ToString()
                        if url.Contains("/auth/token") then
                            return jsonResponse HttpStatusCode.Unauthorized """{"error": "invalid_grant"}"""
                        else
                            return new HttpResponseMessage(HttpStatusCode.NotFound)
                    })
            let api = createApiWithDbBackedConfig db (new HttpClient(handler)) "test-fixtures-do-not-exist/images"

            let authFileJson =
                """{"adp_token": "adp", "device_private_key": "priv", "refresh_token": "bad-refresh-token", "locale_code": "de", "customer_info": {"name": "Marco H"}}"""
            api.setAudibleAuthFile authFileJson |> Async.RunSynchronously |> ignore

            let result = api.testAudibleConnection () |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.isTrue (msg.StartsWith(Audible.authFileRejectedPrefix)) (sprintf "Expected the fixed rejection prefix, got: %s" msg)
            | Ok summary -> failtestf "Expected the stubbed 401 to reject; got Ok %s" summary

            let lastError = SettingsStore.getSetting db.Connection "audible_last_error"
            match lastError with
            | Some msg -> Expect.isTrue (msg.StartsWith(Audible.authFileRejectedPrefix)) "Persisted last-error carries the fixed prefix"
            | None -> failtest "Expected audible_last_error to be persisted"

        testCase "a successful connection mints and persists an access token, clears any standing last-error, and names the customer + marketplace" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let handler =
                new AsyncStubHandler(fun req ->
                    async {
                        let url = req.RequestUri.ToString()
                        if url.Contains("/auth/token") then
                            return jsonResponse HttpStatusCode.OK """{"access_token": "minted-token", "expires_in": 3600}"""
                        elif url.Contains("/1.0/library") then
                            return jsonResponse HttpStatusCode.OK """{"total_results": 7}"""
                        else
                            return new HttpResponseMessage(HttpStatusCode.NotFound)
                    })
            let api = createApiWithDbBackedConfig db (new HttpClient(handler)) "test-fixtures-do-not-exist/images"

            let authFileJson =
                """{"adp_token": "adp", "device_private_key": "priv", "refresh_token": "good-refresh-token", "locale_code": "de", "customer_info": {"name": "Marco H"}}"""
            api.setAudibleAuthFile authFileJson |> Async.RunSynchronously |> ignore
            SettingsStore.setSetting db.Connection "audible_last_error" "audible auth file rejected: stale notice from a previous run"

            let result = api.testAudibleConnection () |> Async.RunSynchronously
            match result with
            | Ok summary ->
                Expect.stringContains summary "Marco H" "Names the customer"
                Expect.stringContains summary "de" "Names the marketplace"
                Expect.stringContains summary "7" "Reports the library total from the probe"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            Expect.equal (SettingsStore.getSetting db.Connection "audible_access_token") (Some "minted-token") "The minted access token was persisted"
            Expect.isSome (SettingsStore.getSetting db.Connection "audible_access_token_expires") "The expiry was persisted"
            Expect.isNone (SettingsStore.getSetting db.Connection "audible_last_error") "The stale standing notice was cleared by a successful test"
    ]
    |> testSequenced

[<Tests>]
let searchAudibleBooksTests =
    testList "IMediathecaApi.searchAudibleBooks works with no auth file stored (ADR-0074 point 5)" [

        testCase "returns catalog results against the default marketplace when nothing is configured" <| fun _ ->
            let searchJson =
                """{"products": [{"asin": "B002V5BNGY", "title": "Dune", "authors": [{"name": "Frank Herbert"}], "narrators": [], "product_images": {"500": "https://example.com/dune-500.jpg"}}]}"""
            let handler = new AsyncStubHandler(fun _ -> async { return jsonResponse HttpStatusCode.OK searchJson })
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory (new HttpClient(handler)) "test-fixtures-do-not-exist/images" (fun () -> noAuthFileConfig)
            let results = api.searchAudibleBooks "dune" |> Async.RunSynchronously
            match results with
            | [ r ] -> Expect.equal r.Title "Dune" "Search works with no auth file at all"
            | other -> failtestf "Expected one result, got %A" other
    ]
