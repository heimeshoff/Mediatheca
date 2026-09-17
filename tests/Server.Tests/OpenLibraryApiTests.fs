module Mediatheca.Tests.OpenLibraryApiTests

/// integration-c8d4x: `IMediathecaApi.addBookFromOpenLibrary` (turns a search
/// hit into a Book with its `book_metadata_cache` slice filled) and
/// `refreshBookFromOpenLibrary` (rewrites the cache slice only, never the
/// identity card — ADR-0043's identity-card clause). No live Open Library
/// call — every request goes through a stub `HttpMessageHandler`.

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

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private notFoundResponse () =
    new HttpResponseMessage(HttpStatusCode.NotFound)

/// A well-over-the-1KB-placeholder-threshold fake JPEG body.
let private fakeCoverBytes = Array.create 2048 (byte 0xFF)

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    NotesProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ BookProjection.handler ]

let private testConfig : OpenLibrary.OpenLibraryConfig =
    { UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" }

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) (imageBasePath: string) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (Qbittorrent.createHttpClient ())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> testConfig)
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        imageBasePath
        allProjectionHandlers

let private workJson =
    """{"description": "A survival story on a dying planet.", "subjects": ["Science fiction", "Space flight"]}"""

/// The edition JSON `/books/{OLID}.json` and `/isbn/{isbn}.json` both return
/// (same shape, different addressing key).
let private editionJson =
    """
    {
        "key": "/books/OL33246498M",
        "title": "Project Hail Mary",
        "works": [{"key": "/works/OL893415W"}],
        "authors": [{"key": "/authors/OL1394865A"}],
        "publish_date": "2021",
        "publishers": ["Ballantine Books"],
        "number_of_pages": 496,
        "covers": [12345678]
    }
    """

/// Answers `/works/{key}.json`, `/books/{OLID}.json` (the real shape of
/// `search.json`'s `edition_key`), `/authors/{key}.json` and the covers CDN
/// — everything `addBookFromOpenLibraryImpl` / `refreshBookFromOpenLibraryImpl`
/// reach. Also records every request's `User-Agent` header so the cover
/// download's UA can be asserted (verifier iteration 1: the cover request
/// used to bypass `OpenLibrary.downloadCover` entirely, carrying none).
let private httpClientFor (recordedUserAgents: System.Collections.Concurrent.ConcurrentDictionary<string, string list>) : HttpClient =
    let handler =
        new AsyncStubHandler(fun req ->
            async {
                let url = req.RequestUri.ToString()
                let ua =
                    if req.Headers.Contains("User-Agent") then req.Headers.GetValues("User-Agent") |> List.ofSeq
                    else []
                recordedUserAgents.[url] <- ua
                if url.Contains("covers.openlibrary.org") then
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent(fakeCoverBytes)
                    return resp
                elif url.Contains("/authors/") then
                    return jsonResponse """{"name": "Andy Weir"}"""
                elif url.Contains("/books/OL33246498M.json") then
                    return jsonResponse editionJson
                elif url.Contains("/isbn/") then
                    return jsonResponse editionJson
                elif url.Contains("/works/") then
                    return jsonResponse workJson
                else
                    return notFoundResponse ()
            })
    new HttpClient(handler)

/// A genuine `edition_key` OLID (`OL33246498M`), exactly the shape
/// `search.json` decodes — never an ISBN (verifier iteration 1). `Isbn13`
/// rides its own explicit field, as the real search result would carry it.
let private sampleRequest : AddBookFromOpenLibraryRequest = {
    WorkKey = "/works/OL893415W"
    EditionKey = Some "OL33246498M"
    Isbn13 = Some "9780593135204"
    CoverId = Some 12345678
    Title = "Project Hail Mary"
    SkipDuplicateCheck = false
}

let private withTempImageDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-openlibrary-test-images-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

[<Tests>]
let openLibraryApiTests =
    testList "IMediathecaApi Open Library (integration-c8d4x)" [

        testCase "addBookFromOpenLibrary resolves a genuine OLID edition_key, creates the book with OpenLibraryWork + Isbn13 linked, downloads the cover through the throttled/UA'd path, writes the cache slice" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let recordedUserAgents = System.Collections.Concurrent.ConcurrentDictionary<string, string list>()
                let api = createApi db.Factory (httpClientFor recordedUserAgents) imageBasePath

                let result = api.addBookFromOpenLibrary sampleRequest |> Async.RunSynchronously
                let slug =
                    match result with
                    | Ok (AddBookOutcome.Book_added slug) -> slug
                    | other -> failtestf "Expected Book_added; got %A" other

                let book =
                    match BookProjection.getBySlug db.Connection slug with
                    | Some b -> b
                    | None -> failtest "Expected the book to be projected"

                Expect.equal book.OpenLibraryWorkKey (Some "/works/OL893415W") "OpenLibraryWork external id linked"
                Expect.equal book.OpenLibraryEditionKey (Some "/books/OL33246498M") "The OLID edition_key resolved a real edition, not a 404"
                Expect.equal book.Isbn13 (Some "9780593135204") "Isbn13 external id linked (from the request's own explicit field, not parsed off the OLID)"
                Expect.equal book.Title "Project Hail Mary" "Title comes from the resolved edition, not a work-key fallback"
                Expect.equal book.CoverRef (Some (sprintf "posters/book-%s.jpg" slug)) "Cover ref recorded"
                Expect.isTrue (File.Exists(Path.Combine(imageBasePath, sprintf "posters/book-%s.jpg" slug))) "Cover file actually downloaded to disk"
                Expect.equal book.PageCount (Some 496) "page_count written to the cache slice"
                Expect.equal book.Description (Some "<p>A survival story on a dying planet.</p>") "description written to the cache slice, converted from Markdown to the allowlisted HTML subset (books-xntts)"

                let coverRequestUserAgents =
                    recordedUserAgents
                    |> Seq.filter (fun kv -> kv.Key.Contains("covers.openlibrary.org"))
                    |> List.ofSeq
                Expect.isNonEmpty coverRequestUserAgents "The cover was fetched through a recorded (i.e. throttled/UA'd) request, not a bare unrecorded httpClient.GetAsync"
                for kv in coverRequestUserAgents do
                    Expect.isTrue
                        (kv.Value |> List.exists (fun v -> v.Contains("Mediatheca/1.0")))
                        (sprintf "Expected the cover request to carry the configured User-Agent (verifier iteration 1), got %A for %s" kv.Value kv.Key)

                let second = api.addBookFromOpenLibrary sampleRequest |> Async.RunSynchronously
                match second with
                | Ok (AddBookOutcome.Duplicate_found _) -> ()
                | other -> failtestf "Expected Duplicate_found on the second call; got %A" other)

        // books-xntts: the cover the user clicked in the search tile
        // (`request.CoverId`) must win even when the OLID `EditionKey`
        // resolves to a different-language edition whose own `covers[0]`
        // differs -- that was the second half of the Lord of the Rings bug
        // (the tile showed the English cover; the imported book got the
        // Spanish edition's cover).
        testCase "addBookFromOpenLibrary downloads the cover for the search hit's CoverId, even when the resolved edition's covers[0] differs" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let recordedUrls = System.Collections.Concurrent.ConcurrentBag<string>()
                let differentCoverEditionJson =
                    """
                    {
                        "key": "/books/OL33246498M",
                        "title": "Project Hail Mary",
                        "works": [{"key": "/works/OL893415W"}],
                        "authors": [{"key": "/authors/OL1394865A"}],
                        "publish_date": "2021",
                        "publishers": ["Ballantine Books"],
                        "number_of_pages": 496,
                        "covers": [99999999]
                    }
                    """
                let handler =
                    new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            recordedUrls.Add(url)
                            if url.Contains("covers.openlibrary.org") then
                                let resp = new HttpResponseMessage(HttpStatusCode.OK)
                                resp.Content <- new ByteArrayContent(fakeCoverBytes)
                                return resp
                            elif url.Contains("/authors/") then
                                return jsonResponse """{"name": "Andy Weir"}"""
                            elif url.Contains("/books/OL33246498M.json") then
                                return jsonResponse differentCoverEditionJson
                            elif url.Contains("/works/") then
                                return jsonResponse workJson
                            else
                                return notFoundResponse ()
                        })
                let api = createApi db.Factory (new HttpClient(handler)) imageBasePath

                // sampleRequest.CoverId = Some 12345678 (the search hit's
                // own cover_i); the resolved edition's own cover is 99999999.
                let result = api.addBookFromOpenLibrary sampleRequest |> Async.RunSynchronously
                match result with
                | Ok (AddBookOutcome.Book_added _) -> ()
                | other -> failtestf "Expected Book_added; got %A" other

                Expect.isTrue
                    (recordedUrls |> Seq.exists (fun u -> u.Contains("covers.openlibrary.org") && u.Contains("/b/id/12345678-L.jpg")))
                    "The cover downloaded is the search hit's own CoverId (12345678), not the resolved edition's covers[0] (99999999)"
                Expect.isFalse
                    (recordedUrls |> Seq.exists (fun u -> u.Contains("covers.openlibrary.org") && u.Contains("/b/id/99999999-L.jpg")))
                    "The edition's own cover id is never requested when the request already carries one")

        // books-xntts: `Option.defaultValue request.WorkKey` used to yield a
        // book literally titled `/works/OL27448W` when the edition never
        // resolved -- the search hit's own `Title` is the fallback now.
        testCase "addBookFromOpenLibrary with an edition key that resolves to a 404 stores the search hit's Title, never the work key string" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let handler =
                    new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("/works/") then return jsonResponse workJson
                            else return notFoundResponse () // the edition lookup 404s
                        })
                let api = createApi db.Factory (new HttpClient(handler)) imageBasePath

                let requestWithUnresolvableEdition = { sampleRequest with EditionKey = Some "OL00000000M" }
                let result = api.addBookFromOpenLibrary requestWithUnresolvableEdition |> Async.RunSynchronously
                let slug =
                    match result with
                    | Ok (AddBookOutcome.Book_added slug) -> slug
                    | other -> failtestf "Expected Book_added; got %A" other

                let book =
                    match BookProjection.getBySlug db.Connection slug with
                    | Some b -> b
                    | None -> failtest "Expected the book to be projected"

                Expect.equal book.Title sampleRequest.Title "Falls back to the search hit's own Title"
                Expect.notEqual book.Title sampleRequest.WorkKey "Never the raw work key string")

        testCase "refreshBookFromOpenLibrary rewrites the cache slice only, leaving book_list.title/cover_ref untouched" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let recordedUserAgents = System.Collections.Concurrent.ConcurrentDictionary<string, string list>()
                let api = createApi db.Factory (httpClientFor recordedUserAgents) imageBasePath

                let created = api.addBookFromOpenLibrary sampleRequest |> Async.RunSynchronously
                let slug =
                    match created with
                    | Ok (AddBookOutcome.Book_added slug) -> slug
                    | other -> failtestf "Expected Book_added; got %A" other

                let before =
                    match BookProjection.getBySlug db.Connection slug with
                    | Some b -> b
                    | None -> failtest "Expected the book to be projected"
                let beforeListRow = BookProjection.getAll db.Connection |> List.find (fun b -> b.Slug = slug)

                // Change what the stub reports for the work's description —
                // the refresh should pick this up in the cache, and nothing
                // else.
                let updatedWorkJson = """{"description": "A revised description.", "subjects": []}"""
                let refreshHandler =
                    new AsyncStubHandler(fun req ->
                        async {
                            let url = req.RequestUri.ToString()
                            if url.Contains("/isbn/") then return jsonResponse editionJson
                            elif url.Contains("/works/") then return jsonResponse updatedWorkJson
                            else return notFoundResponse ()
                        })
                let refreshApi = createApi db.Factory (new HttpClient(refreshHandler)) imageBasePath

                let refreshResult = refreshApi.refreshBookFromOpenLibrary slug |> Async.RunSynchronously
                match refreshResult with
                | Ok () -> ()
                | Error e -> failtestf "Expected refresh to succeed, got Error %s" e

                let after =
                    match BookProjection.getBySlug db.Connection slug with
                    | Some b -> b
                    | None -> failtest "Expected the book to still be projected"
                let afterListRow = BookProjection.getAll db.Connection |> List.find (fun b -> b.Slug = slug)

                Expect.equal after.Description (Some "<p>A revised description.</p>") "Cache slice's description was rewritten by the refresh, converted from Markdown to the allowlisted HTML subset (books-xntts)"
                Expect.equal after.Title before.Title "book_detail.title is untouched by a refresh"
                Expect.equal after.CoverRef before.CoverRef "book_detail.cover_ref is untouched by a refresh"
                Expect.equal afterListRow.Title beforeListRow.Title "book_list.title is untouched by a refresh"
                Expect.equal afterListRow.CoverRef beforeListRow.CoverRef "book_list.cover_ref is untouched by a refresh")
    ]
    |> testSequenced
