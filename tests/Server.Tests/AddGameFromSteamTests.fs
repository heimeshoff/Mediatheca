module Mediatheca.Tests.AddGameFromSteamTests

/// games-k3vps: the search modal's Steam source toggle. Covers the two new
/// `IMediathecaApi` endpoints — `searchSteamGames` (a thin wrapper over
/// `Steam.searchSteamByName`) and `addGameFromSteam` (the store-details ->
/// `Add_game` import path, ADR-0043/ADR-0045's identity-card/cache-slice
/// split applied exactly as games-v4nqe applied it to the Steam library
/// import).

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

/// A minimal `appdetails` success body carrying only the fields
/// `Steam.getSteamStoreDetails` reads.
let private storeDetailsJson (appId: int) (shortDesc: string) (aboutTheGame: string) (categoryIds: (int * string) list) =
    let categoriesJson =
        categoryIds
        |> List.map (fun (id, desc) -> sprintf """{"id":%d,"description":"%s"}""" id desc)
        |> String.concat ","
    sprintf
        """{"%d":{"success":true,"data":{"short_description":"%s","detailed_description":"","about_the_game":"%s","website":"https://example.com/game","categories":[%s],"header_image":"https://example.com/header.jpg"}}}"""
        appId shortDesc aboutTheGame categoriesJson

/// games-ev65k: same shape as `storeDetailsJson`, additionally carrying
/// `release_date` — used by the Tenebris Somnia end-to-end criterion below.
let private storeDetailsJsonWithReleaseDate (appId: int) (comingSoon: bool) (dateStr: string) =
    sprintf
        """{"%d":{"success":true,"data":{"short_description":"A survival horror descent","detailed_description":"","about_the_game":"Long about text","categories":[],"release_date":{"coming_soon":%s,"date":"%s"}}}}"""
        appId (if comingSoon then "true" else "false") dateStr

/// A stub that answers `appdetails` (both `getSteamStoreDetails`'s call and
/// `fetchStoreMeta`'s `filters=basic,release_date` call), `SearchApps`, and
/// 404s the CDN cover/backdrop downloads (image content is out of scope —
/// `Steam.downloadSteamCover`/`downloadSteamBackdrop` degrade to `None` on a
/// non-2xx response without throwing, so this exercises the real code path).
let private httpClientFor
    (searchAppsJson: string)
    (storeDetailsBody: string)
    (storeMetaBody: string)
    : HttpClient =
    let handler =
        new StubHandler(fun req ->
            let url = req.RequestUri.ToString()
            if url.Contains("SearchApps") then jsonResponse searchAppsJson
            elif url.Contains("filters=basic") then jsonResponse storeMetaBody
            elif url.Contains("appdetails") then jsonResponse storeDetailsBody
            else notFoundResponse ())
    new HttpClient(handler)

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
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        noImagesDir
        allProjectionHandlers

[<Tests>]
let addGameFromSteamTests =
    testList "IMediathecaApi.addGameFromSteam (games-k3vps)" [

        testCase "Creates a new game, sets SteamAppId, and writes description/short-description/website-url/facets to the metadata cache — never through GameProjection.handleEvent" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http =
                httpClientFor
                    "[]"
                    (storeDetailsJson 620 "A puzzle game" "Long about text" [ (2, "Single-player"); (1, "Multi-player") ])
                    "{}"
            let api = createApi db.Factory http

            let request: AddGameFromSteamRequest = { AppId = 620; Name = "Portal 2"; Year = Some 2011; SkipDuplicateCheck = false }
            let result = api.addGameFromSteam request |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the newly created game to be readable from the projection"
                | Some game ->
                    Expect.equal game.Name "Portal 2" "Name rides the Add_game event"
                    Expect.equal game.Year 2011 "Year rides the Add_game event"
                    Expect.equal game.SteamAppId (Some 620) "Set_steam_app_id was dispatched"
                    Expect.equal game.Description "Long about text" "Description sourced from the cache-backed identity card"
                    Expect.equal game.ShortDescription "A puzzle game" "ShortDescription sourced from the cache-backed identity card"
                    Expect.equal game.WebsiteUrl (Some "https://example.com/game") "WebsiteUrl sourced from the cache-backed identity card"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh AppId/Name pair to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)

        testCase "An existing game with the same SteamAppId is reported as Duplicate_found, not created a second time" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor "[]" (storeDetailsJson 620 "" "" []) "{}"
            let api = createApi db.Factory http

            let request: AddGameFromSteamRequest = { AppId = 620; Name = "Portal 2"; Year = Some 2011; SkipDuplicateCheck = false }
            let first = api.addGameFromSteam request |> Async.RunSynchronously
            let firstSlug =
                match first with
                | Ok (Created slug) -> slug
                | other -> failtestf "Expected the first call to create; got %A" other

            let second = api.addGameFromSteam request |> Async.RunSynchronously
            match second with
            | Ok (Duplicate_found (existingSlug, existingName)) ->
                Expect.equal existingSlug firstSlug "Duplicate points at the game created by the first call"
                Expect.equal existingName "Portal 2" "Duplicate reports the existing game's name"
            | other -> failtestf "Expected Duplicate_found on the second call with the same AppId, got %A" other

            let allGames = GameProjection.getAll db.Connection
            Expect.equal (List.length allGames) 1 "No second game was created"

        testCase "SkipDuplicateCheck=true bypasses the duplicate check and creates a second entry (the 'add as duplicate' flow)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor "[]" (storeDetailsJson 620 "" "" []) "{}"
            let api = createApi db.Factory http

            let request: AddGameFromSteamRequest = { AppId = 620; Name = "Portal 2"; Year = Some 2011; SkipDuplicateCheck = false }
            api.addGameFromSteam request |> Async.RunSynchronously |> ignore

            let forced = { request with SkipDuplicateCheck = true }
            let result = api.addGameFromSteam forced |> Async.RunSynchronously
            match result with
            | Ok (Created _) -> ()
            | other -> failtestf "Expected SkipDuplicateCheck=true to create anyway, got %A" other

            let allGames = GameProjection.getAll db.Connection
            Expect.equal (List.length allGames) 2 "Both games exist — the deliberate duplicate was added"

        testCase "A failed Steam store lookup still creates the game, with empty description fields and no facets (never throws)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            // appdetails 404s -> Steam.getSteamStoreDetails returns Error internally.
            let handler = new StubHandler(fun _ -> notFoundResponse ())
            let http = new HttpClient(handler)
            let api = createApi db.Factory http

            let request: AddGameFromSteamRequest = { AppId = 999; Name = "Obscure Title"; Year = None; SkipDuplicateCheck = false }
            let result = api.addGameFromSteam request |> Async.RunSynchronously
            match result with
            | Ok (Created slug) ->
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the game to exist"
                | Some game ->
                    Expect.equal game.Description "" "No store details fetched — empty description, not a fabricated value"
                    Expect.equal game.SteamAppId (Some 999) "Set_steam_app_id still fires even when the store lookup fails"
            | other -> failtestf "Expected the game to be created despite the failed Steam lookup, got %A" other

        testCase "games-ev65k end-to-end: importing Tenebris Somnia (appId 2121510) yields its October 2026 release date on the detail page, an upcoming hint on its list card, and a row in the Upcoming section" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor "[]" (storeDetailsJsonWithReleaseDate 2121510 true "October 2026") "{}"
            let api = createApi db.Factory http

            let request: AddGameFromSteamRequest = { AppId = 2121510; Name = "Tenebris Somnia"; Year = None; SkipDuplicateCheck = false }
            let result = api.addGameFromSteam request |> Async.RunSynchronously

            match result with
            | Ok (Created slug) ->
                // Detail page
                match GameProjection.getBySlug db.Connection slug with
                | None -> failtest "Expected the newly created game to be readable from the projection"
                | Some game ->
                    Expect.equal game.ReleaseDate.Raw "October 2026" "the detail page's release-date field carries Steam's raw string"
                    Expect.equal game.ReleaseDate.Parsed (Some "2026-10-01") "parsed sortable date for October 2026"
                    Expect.equal game.ReleaseDate.IsUnreleased true "coming_soon marks it unreleased on the detail page"

                // List card hint
                let listItem = GameProjection.getAll db.Connection |> List.find (fun g -> g.Slug = slug)
                Expect.equal listItem.ReleaseDate.IsUnreleased true "the list card's upcoming hint is driven by the same IsUnreleased flag"
                Expect.equal listItem.ReleaseDate.Raw "October 2026" "the list card can show Steam's raw date string"

                // Upcoming section
                let upcoming = GameProjection.getUpcomingGames db.Connection
                Expect.exists upcoming (fun g -> g.Slug = slug) "Tenebris Somnia appears as a row in the Upcoming section"
            | Ok (Duplicate_found _) -> failtest "Expected a fresh import to create, not duplicate"
            | Error e -> failtest (sprintf "Expected success, got Error %s" e)
    ]

[<Tests>]
let searchSteamGamesTests =
    testList "IMediathecaApi.searchSteamGames (games-k3vps)" [

        testCase "Delegates to Steam.searchSteamByName and returns matching candidates" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let searchAppsJson = """[{"appid":620,"name":"Portal 2","icon":"icon.jpg"}]"""
            let storeMetaJson = """{"620":{"success":true,"data":{"type":"game","release_date":{"date":"18 Apr, 2011"},"header_image":"https://example.com/header.jpg"}}}"""
            let http = httpClientFor searchAppsJson "{}" storeMetaJson
            let api = createApi db.Factory http

            let results = api.searchSteamGames ("Portal 2", Some 2011) |> Async.RunSynchronously
            match results with
            | [ r ] ->
                Expect.equal r.AppId 620 "AppId comes through unchanged"
                Expect.equal r.Name "Portal 2" "Name comes through unchanged"
                Expect.equal r.ReleaseYear (Some 2011) "Year boosted from the store meta call"
            | other -> failtestf "Expected exactly one high-confidence match, got %A" other

        testCase "An empty SearchApps response yields an empty result list, not an error" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http = httpClientFor "[]" "{}" "{}"
            let api = createApi db.Factory http

            let results = api.searchSteamGames ("Nonexistent Game Xyz", None) |> Async.RunSynchronously
            Expect.isEmpty results "No candidates from Steam — an empty list, matching searchSteamForGame's existing degrade-to-[] behavior"
    ]
