module Mediatheca.Tests.AddGameFromRawgTests

/// games-r1tx4: `addGame`'s RAWG path — closes the latent defect the
/// task's Why section names: since games-v4nqe dropped `game_detail`'s
/// `description` projection column, a RAWG-added game's description used
/// to land only in the (ignored) `Game_added_to_library` payload, never in
/// `game_metadata_cache` — an empty description on the detail page unless
/// the best-effort Steam auto-attach happened to fill it. This mirrors the
/// Steam sites' own creation-code-path identity-card write
/// (ADR-0043/ADR-0045), stubbing `Rawg.getGameDetails` the same way
/// `AddGameFromSteamTests.fs` stubs Steam's `appdetails`.

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

let private notFoundResponse () =
    new HttpResponseMessage(HttpStatusCode.NotFound)

/// A minimal RAWG game-details success body carrying only the fields
/// `Rawg.getGameDetails`/`addGame` read. Single-quoted HTML attributes so
/// the fixture needs no escaping inside the JSON string literal.
let private rawgDetailsJson (rawgId: int) (descriptionHtml: string) (descriptionRaw: string) =
    sprintf
        """{"id":%d,"name":"Test Game","released":"2013-09-17","description":"%s","description_raw":"%s","rating":4.5,"genres":[]}"""
        rawgId descriptionHtml descriptionRaw

/// Answers RAWG's game-details endpoint; everything else (Steam's
/// SearchApps, called by `addGame`'s best-effort auto-attach for any RAWG
/// import) 404s, which every caller on that path already degrades safely
/// from (`Steam.searchApps`'s own try/with -> `[]`).
let private httpClientFor (rawgDetailsBody: string) : HttpClient =
    let handler =
        new StubHandler(fun req ->
            let url = req.RequestUri.ToString()
            if url.Contains("api.rawg.io/api/games/") then jsonResponse rawgDetailsBody
            else notFoundResponse ())
    new HttpClient(handler)

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ GameProjection.handler; PlaySessionProjection.handler ]

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) (rawgApiKey: string) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (Qbittorrent.createHttpClient ()) // ADR-0072: qBittorrent uses its own cookie-jar-free client
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = rawgApiKey } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> ({ UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" } : OpenLibrary.OpenLibraryConfig))
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        allProjectionHandlers

let private sampleRequest (rawgId: int) : AddGameRequest =
    { Name = "Test Game"
      Year = 2013
      Genres = []
      Description = "fallback plain description"
      CoverRef = None
      BackdropRef = None
      RawgId = Some rawgId
      RawgRating = Some 4.5
      SkipDuplicateCheck = false }

[<Tests>]
let addGameFromRawgTests =
    testList "IMediathecaApi.addGame RAWG path (games-r1tx4)" [

        testCase "Writes a sanitized identity-card description to game_metadata_cache from RAWG's HTML description field, dropping disallowed tags/attributes" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let descriptionHtml =
                "<p>Rockstar's <strong>open world</strong> game.<br />Explore a <a href='https://x.com'>huge</a> map.</p>"
            let descriptionRaw = "Rockstar's open world game. Explore a huge map."
            let http = httpClientFor (rawgDetailsJson 3498 descriptionHtml descriptionRaw)
            let api = createApi db.Factory http "test-rawg-key"

            let result = api.addGame (sampleRequest 3498) |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the newly created game to be readable from the projection"
                | Some game ->
                    Expect.stringContains game.Description "<strong>open world</strong>" "the identity card keeps the allowlisted <strong> tag"
                    Expect.stringContains game.Description "<br>" "the identity card keeps <br>, normalized"
                    Expect.isFalse (game.Description.Contains "<a ") "the identity card drops the disallowed <a> tag"
                    Expect.stringContains game.Description "huge" "the <a>'s own text content survives, unwrapped"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh RawgId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)

        testCase "Falls back to sanitized description_raw when RAWG's HTML description field is empty" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor (rawgDetailsJson 42 "" "Plain raw description, no HTML.")
            let api = createApi db.Factory http "test-rawg-key"

            let result = api.addGame { sampleRequest 42 with Name = "Fallback Game" } |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the newly created game to be readable from the projection"
                | Some game ->
                    Expect.equal game.Description "Plain raw description, no HTML." "falls back to sanitized description_raw"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh RawgId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)

        testCase "A RAWG details fetch failure (no API key configured) still creates the game, falling back to the request's own plain Description in the identity card" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor (rawgDetailsJson 7 "" "")
            // Empty ApiKey -> Rawg.getGameDetails throws inside addGame's own
            // try/with, degrading `details` to None -- the same honest-
            // degradation path a failed Steam lookup already exercises.
            let api = createApi db.Factory http ""

            let request = { sampleRequest 7 with Name = "No Key Game"; Description = "the request's own plain description" }
            let result = api.addGame request |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the newly created game to be readable from the projection"
                | Some game ->
                    Expect.equal game.Description "the request's own plain description" "falls back all the way to the request's own Description when RAWG can't be reached"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh RawgId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)

        testCase "The Game_added_to_library event payload keeps the plain description_raw -- no HTML ever rides the event" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let descriptionHtml = "<p>HTML <strong>description</strong>.</p>"
            let descriptionRaw = "Plain description."
            let http = httpClientFor (rawgDetailsJson 99 descriptionHtml descriptionRaw)
            let api = createApi db.Factory http "test-rawg-key"

            let result = api.addGame { sampleRequest 99 with Name = "Event Payload Game" } |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                let addedEvent =
                    EventStore.readStream db.Connection (Games.streamId slug)
                    |> List.tryFind (fun e -> e.EventType = "Game_added_to_library")
                match addedEvent with
                | Some stored ->
                    match Thoth.Json.Net.Decode.fromString (Thoth.Json.Net.Decode.field "description" Thoth.Json.Net.Decode.string) stored.Data with
                    | Ok description -> Expect.equal description descriptionRaw "the event payload carries the plain description_raw, never the sanitized HTML"
                    | Error e -> failtest (sprintf "Failed to decode Game_added_to_library payload: %s" e)
                | None -> failtest "Expected a Game_added_to_library event in the stream"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh RawgId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)

        testCase "games-fffvm: the new row is stamped description_fetched_at — never returned by findGamesNeedingDescriptionBackfill" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor (rawgDetailsJson 4200 "<p>An RPG.</p>" "An RPG.")
            let api = createApi db.Factory http "test-rawg-key"

            let result = api.addGame { sampleRequest 4200 with Name = "Stamped Rawg Game" } |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                let candidates = MetadataCache.findGamesNeedingDescriptionBackfill db.Connection |> List.map fst
                Expect.isFalse (List.contains slug candidates) "the freshly-created row is not a description-backfill candidate"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh RawgId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)
    ]
