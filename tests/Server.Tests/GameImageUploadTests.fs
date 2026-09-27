module Mediatheca.Tests.GameImageUploadTests

/// games-hm3sf: the game detail page's "Change cover" / "Change backdrop"
/// picker gains two manual entry points, alongside the existing candidate
/// grid — upload a file from disk (`IMediathecaApi.uploadGameImage`, a new
/// byte-array endpoint mirroring `uploadFriendImage`'s transport shape), or
/// paste an image URL (the EXISTING `selectGameImage`, unchanged signature).
/// Both converge server-side on one shared validate-save-command helper
/// (`Api.fs`'s `saveGameImageAndReplace`) that writes the game's fixed
/// `posters/game-{slug}.jpg` / `backdrops/game-{slug}.jpg` ref and executes
/// `Replace_cover` / `Replace_backdrop` through the normal command path, so
/// `Game_cover_replaced` / `Game_backdrop_replaced` fires exactly as it does
/// for a picked candidate (ADR-0043).

open System
open System.IO
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

let private byteResponse (status: HttpStatusCode) (contentType: string) (bytes: byte[]) =
    let resp = new HttpResponseMessage(status)
    resp.Content <- new ByteArrayContent(bytes)
    resp.Content.Headers.ContentType <- Headers.MediaTypeHeaderValue.Parse(contentType)
    resp

let private httpClientAlwaysReturning (response: HttpResponseMessage) : HttpClient =
    let handler = new StubHandler(fun _ -> response)
    new HttpClient(handler)

/// A minimal-but-sniffable JPEG: the SOI + APP0 magic bytes our sniff checks
/// for, plus a little padding — never decoded as a real image anywhere in
/// this test, only sniffed and stored as bytes.
let private jpegBytes = [| 0xFFuy; 0xD8uy; 0xFFuy; 0xE0uy; 0x00uy; 0x10uy; 0x4Auy; 0x46uy; 0x49uy; 0x46uy |]

let private htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body>not an image</body></html>")

let private textRenamedJpgBytes = System.Text.Encoding.UTF8.GetBytes("just some plain text, not an image at all")

/// SVG is refused even though it's technically "an image format" — the task's
/// validation explicitly calls out SVG as never stored.
let private svgBytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")

/// Oversized fixture MUST sniff as a valid JPEG (SOI + APP0 magic bytes,
/// same as `jpegBytes` above) so that padding it past the 25 MB cap means
/// the size check is the ONLY thing that can refuse it — never the format
/// check. Padded with harmless trailing bytes past the cap.
let private oversizedBytes =
    Array.append
        [| 0xFFuy; 0xD8uy; 0xFFuy; 0xE0uy; 0x00uy; 0x10uy; 0x4Auy; 0x46uy; 0x49uy; 0x46uy |]
        (Array.zeroCreate<byte> (26 * 1024 * 1024))

let private withTempImagesDir (f: string -> unit) : unit =
    let tempRoot = Path.Combine(Path.GetTempPath(), "mediatheca-game-image-test-" + Guid.NewGuid().ToString("N"))
    let imagesDir = Path.Combine(tempRoot, "images")
    Directory.CreateDirectory(imagesDir) |> ignore
    try
        f imagesDir
    finally
        Directory.Delete(tempRoot, true)

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ GameProjection.handler; PlaySessionProjection.handler ]

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) (imagesDir: string) : IMediathecaApi =
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
        (fun () -> ({ BaseUrl = ""; ApiToken = ""; SelectedPlatformIds = [] } : RomM.RomMConfig))
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        imagesDir
        "" // pdfBasePath, unused here
        allProjectionHandlers

/// Creates a game with no RAWG id (so `addGame` never makes a network call)
/// and no cover/backdrop, returning its slug.
let private addPlainGame (api: IMediathecaApi) (name: string) : string =
    let request: AddGameRequest = {
        Name = name
        Year = 2020
        Genres = []
        Description = ""
        CoverRef = None
        BackdropRef = None
        RawgId = None
        RawgRating = None
        SkipDuplicateCheck = false
    }
    match api.addGame request |> Async.RunSynchronously with
    | Ok (Created slug) -> slug
    | other -> failtestf "Expected addGame to create a plain game, got %A" other

let private fileExists (imagesDir: string) (relativePath: string) : bool =
    File.Exists(Path.Combine(imagesDir, relativePath.Replace('/', Path.DirectorySeparatorChar)))

[<Tests>]
let gameImageUploadTests =
    testList "IMediathecaApi game cover/backdrop manual upload (games-hm3sf)" [

        testCase "Uploading a JPEG as the cover writes the fixed poster ref, fires Game_cover_replaced, and CoverRef reflects it" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "Cover Upload Game"

                let result = api.uploadGameImage slug jpegBytes "my-cover.png" "cover" |> Async.RunSynchronously

                Expect.equal result (Ok ()) "Upload should succeed"
                let expectedRef = $"posters/game-{slug}.jpg"
                Expect.isTrue (fileExists imagesDir expectedRef) "The fixed poster ref should exist on disk"
                match GameProjection.getBySlug db.Connection slug with
                | Some game -> Expect.equal game.CoverRef (Some expectedRef) "CoverRef should equal the fixed poster ref"
                | None -> failtest "Expected the game to still exist"

                match EventStore.readStream db.Connection (Games.streamId slug) |> List.map (fun e -> e.EventType) with
                | types -> Expect.contains types "Game_cover_replaced" "Game_cover_replaced should have been appended")

        testCase "Uploading a JPEG as the backdrop writes the fixed backdrop ref and fires Game_backdrop_replaced" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "Backdrop Upload Game"

                let result = api.uploadGameImage slug jpegBytes "my-backdrop.png" "backdrop" |> Async.RunSynchronously

                Expect.equal result (Ok ()) "Upload should succeed"
                let expectedRef = $"backdrops/game-{slug}.jpg"
                Expect.isTrue (fileExists imagesDir expectedRef) "The fixed backdrop ref should exist on disk"
                match GameProjection.getBySlug db.Connection slug with
                | Some game -> Expect.equal game.BackdropRef (Some expectedRef) "BackdropRef should equal the fixed backdrop ref"
                | None -> failtest "Expected the game to still exist"

                match EventStore.readStream db.Connection (Games.streamId slug) |> List.map (fun e -> e.EventType) with
                | types -> Expect.contains types "Game_backdrop_replaced" "Game_backdrop_replaced should have been appended")

        testCase "Submitting a URL that returns JPEG bytes stores them under the fixed ref via selectGameImage (existing behaviour)" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "URL Cover Game"

                let result = api.selectGameImage slug "https://example.com/cover.jpg" "cover" |> Async.RunSynchronously

                Expect.equal result (Ok ()) "URL selection should succeed"
                let expectedRef = $"posters/game-{slug}.jpg"
                Expect.isTrue (fileExists imagesDir expectedRef) "The fixed poster ref should exist on disk"
                match GameProjection.getBySlug db.Connection slug with
                | Some game -> Expect.equal game.CoverRef (Some expectedRef) "CoverRef should equal the fixed poster ref"
                | None -> failtest "Expected the game to still exist")

        testCase "A non-2xx URL response yields Error and writes no file" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.NotFound "text/plain" [||])) imagesDir
                let slug = addPlainGame api "Bad URL Game"

                let result = api.selectGameImage slug "https://example.com/missing.jpg" "cover" |> Async.RunSynchronously

                match result with
                | Error _ -> ()
                | Ok () -> failtest "Expected a non-2xx response to be refused"
                Expect.isFalse (fileExists imagesDir $"posters/game-{slug}.jpg") "No file should have been written")

        testCase "A 200 response whose body is not JPEG/PNG/WebP (an HTML page) yields Error and writes no file" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "text/html" htmlBytes)) imagesDir
                let slug = addPlainGame api "HTML Body Game"

                let result = api.selectGameImage slug "https://example.com/not-really-an-image.jpg" "cover" |> Async.RunSynchronously

                match result with
                | Error _ -> ()
                | Ok () -> failtest "Expected an HTML body to be refused as an image"
                Expect.isFalse (fileExists imagesDir $"posters/game-{slug}.jpg") "No file should have been written")

        testCase "Uploading bytes that are not JPEG/PNG/WebP (a text file renamed .jpg) yields Error and writes no file" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "Fake Upload Game"

                let result = api.uploadGameImage slug textRenamedJpgBytes "fake.jpg" "cover" |> Async.RunSynchronously

                match result with
                | Error _ -> ()
                | Ok () -> failtest "Expected non-image bytes to be refused"
                Expect.isFalse (fileExists imagesDir $"posters/game-{slug}.jpg") "No file should have been written")

        testCase "Uploading an SVG is refused and never stored, even though it is technically an image format" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "SVG Upload Game"

                let result = api.uploadGameImage slug svgBytes "icon.svg" "backdrop" |> Async.RunSynchronously

                match result with
                | Error _ -> ()
                | Ok () -> failtest "Expected an SVG upload to be refused"
                Expect.isFalse (fileExists imagesDir $"backdrops/game-{slug}.jpg") "No file should have been written")

        testCase "An upload over the 25 MB cap is refused with Error and writes no file" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" jpegBytes)) imagesDir
                let slug = addPlainGame api "Oversized Upload Game"

                let result = api.uploadGameImage slug oversizedBytes "huge.jpg" "cover" |> Async.RunSynchronously

                match result with
                | Error message -> Expect.stringContains message "25 MB" "The error should name the 25 MB limit"
                | Ok () -> failtest "Expected an oversized upload to be refused"
                Expect.isFalse (fileExists imagesDir $"posters/game-{slug}.jpg") "No file should have been written")

        testCase "A URL response over the 25 MB cap is refused with Error and writes no file" <| fun _ ->
            withTempImagesDir (fun imagesDir ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory (httpClientAlwaysReturning (byteResponse HttpStatusCode.OK "image/jpeg" oversizedBytes)) imagesDir
                let slug = addPlainGame api "Oversized URL Game"

                let result = api.selectGameImage slug "https://example.com/huge.jpg" "backdrop" |> Async.RunSynchronously

                match result with
                | Error message -> Expect.stringContains message "25 MB" "The error should name the 25 MB limit"
                | Ok () -> failtest "Expected an oversized URL response to be refused"
                Expect.isFalse (fileExists imagesDir $"backdrops/game-{slug}.jpg") "No file should have been written")
    ]
