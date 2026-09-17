module Mediatheca.Tests.GameDescriptionBackfillTests

/// games-fffvm (ADR-0043/ADR-0045): the resumable re-sanitization backfill
/// for `game_metadata_cache.description` — re-fetches every already-cached
/// game's description (Steam-linked via the storefront, RAWG-only via RAWG
/// details) through games-r1tx4's sanitizer, so games imported before it
/// gain paragraphs/emphasis and RAWG-only games get the description
/// games-v4nqe silently dropped. Mirrors `GameReleaseDateBackfillTests.fs`'s
/// `StubHandler` pattern, extended to also answer RAWG's game-details
/// endpoint.

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Donald
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

/// A minimal `appdetails` success body — `aboutTheGame` is decoded through
/// `Steam.fs`'s own sanitizer at decode time (games-r1tx4), so a raw HTML
/// string here arrives already sanitized by the time `Steam.storeDescription`
/// reads it.
let private storeDetailsJson (appId: int) (shortDesc: string) (aboutTheGame: string) =
    sprintf
        """{"%d":{"success":true,"data":{"short_description":"%s","detailed_description":"","about_the_game":"%s","categories":[],"release_date":{"coming_soon":false,"date":""}}}}"""
        appId shortDesc aboutTheGame

/// A minimal RAWG game-details success body — `Rawg.fs` does NOT sanitize
/// at decode time (only `Steam.fs` does), so the job itself must sanitize.
let private rawgDetailsJson (rawgId: int) (descriptionHtml: string) (descriptionRaw: string) =
    sprintf
        """{"id":%d,"name":"Test Game","released":null,"description":"%s","description_raw":"%s","rating":4.5,"genres":[]}"""
        rawgId descriptionHtml descriptionRaw

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Tenebris Somnia"
    Year = 2026
    Genres = [ "Horror" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

/// Seeds a game with a Steam app id AND a legacy, flattened,
/// never-stamped identity-card row — the exact "imported before r1tx4"
/// state this task's Why section describes.
let private seedLegacySteamGame
    (conn: SqliteConnection)
    (slug: string)
    (steamAppId: int)
    (legacyDescription: string)
    (legacyWebsiteUrl: string option)
    : unit =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library { sampleGameData with Name = slug }) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    EventStore.appendToStream conn (Games.streamId slug) 0L
        [ Games.Serialization.toEventData (Games.Game_steam_app_id_set steamAppId) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    MetadataCache.upsertGameIdentityCard conn slug {
        Description = legacyDescription
        ShortDescription = "old short description"
        WebsiteUrl = legacyWebsiteUrl
    }
    // deliberately NOT stamped -- description_fetched_at stays NULL, the
    // pre-this-task state.

/// Seeds a RAWG-only game with NO `game_metadata_cache` row at all --
/// games-v4nqe's seed only ever created rows for games that existed at seed
/// time, and this game is added after that.
let private seedRawgOnlyGameWithNoCacheRow (conn: SqliteConnection) (slug: string) (rawgId: int) : unit =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library { sampleGameData with Name = slug; RawgId = Some rawgId }) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private descriptionRow (conn: SqliteConnection) (slug: string) =
    conn
    |> Db.newCommand "SELECT description, short_description, website_url, description_fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun rd ->
        rd.ReadString "description",
        rd.ReadString "short_description",
        (if rd.IsDBNull(rd.GetOrdinal("website_url")) then None else Some (rd.ReadString "website_url")),
        rd.IsDBNull(rd.GetOrdinal("description_fetched_at")))

let private noRawgConfig () : Rawg.RawgConfig = { ApiKey = "" }

[<Tests>]
let tests =
    testList "GameDescriptionBackfill (games-fffvm)" [

        testCase "A stubbed HttpClient serving both Steam appdetails and RAWG game-details: rewrites a legacy Steam-linked row, inserts a RAWG-only row, and leaves a Steam-error row untouched" <| fun _ ->
            let conn = createConnection ()
            seedLegacySteamGame conn "old-flat-game-2015" 620
                "A flat legacy description with no markup at all"
                (Some "https://legacy-site.example.com")
            seedRawgOnlyGameWithNoCacheRow conn "rawg-only-game-2020" 9001
            seedLegacySteamGame conn "steam-error-game-2016" 999
                "The old description Steam couldn't refresh"
                None

            let candidatesBefore = MetadataCache.findGamesNeedingDescriptionBackfill conn |> List.map fst |> Set.ofList |> Set.count
            Expect.equal candidatesBefore 3 "sanity: all three seeded games are candidates before the run"

            let httpClient =
                new HttpClient(
                    new StubHandler(fun req ->
                        let url = req.RequestUri.ToString()
                        if url.Contains("appids=620") then
                            jsonResponse (storeDetailsJson 620 "New short description" "<p>New <strong>rich</strong> text</p><h2>Header</h2><img src='x.png'>")
                        elif url.Contains("appids=999") then
                            notFoundResponse ()
                        elif url.Contains("api.rawg.io/api/games/9001") then
                            jsonResponse (rawgDetailsJson 9001 "<p>RAWG <strong>rich</strong> text</p><h2>Section</h2>" "RAWG plain fallback")
                        else
                            notFoundResponse ()))

            let result = GameDescriptionBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient (fun () -> ({ ApiKey = "test-rawg-key" } : Rawg.RawgConfig)) |> Async.RunSynchronously

            Expect.equal result.Processed 3 "Three candidates processed"
            Expect.equal result.Succeeded 2 "Steam success + RAWG success"
            Expect.equal result.Errors 0 "A Steam 404 is a handled Error, not an exception"
            Expect.equal result.Skipped 0 "No blank RAWG key in this run"

            // Steam-linked row: rewritten, sanitized, short_description
            // refreshed, website_url survives, stamped.
            match descriptionRow conn "old-flat-game-2015" with
            | Some (desc, shortDesc, websiteUrl, fetchedAtIsNull) ->
                Expect.stringContains desc "<strong>rich</strong>" "keeps the allowlisted <strong> tag"
                Expect.stringContains desc "<p>" "keeps the allowlisted <p> tag"
                Expect.isFalse (desc.Contains "<h2>") "drops the disallowed <h2> tag"
                Expect.isFalse (desc.Contains "<img") "drops the disallowed <img> tag"
                Expect.equal shortDesc "New short description" "short_description refreshed from the Steam fetch"
                Expect.equal websiteUrl (Some "https://legacy-site.example.com") "website_url survives the description write untouched"
                Expect.isFalse fetchedAtIsNull "description_fetched_at is stamped"
            | None -> failtest "expected the Steam-linked row to still exist"

            // RAWG-only row: a fresh cache row is inserted, sanitized, stamped.
            match descriptionRow conn "rawg-only-game-2020" with
            | Some (desc, _, _, fetchedAtIsNull) ->
                Expect.stringContains desc "<strong>rich</strong>" "RAWG's sanitized description keeps the allowlisted <strong> tag"
                Expect.isFalse (desc.Contains "<h2>") "RAWG's sanitized description drops the disallowed <h2> tag"
                Expect.isFalse fetchedAtIsNull "description_fetched_at is stamped on the freshly-inserted row"
            | None -> failtest "expected a fresh cache row to be inserted for the RAWG-only game"

            // Steam-error row: unstamped, old description intact.
            match descriptionRow conn "steam-error-game-2016" with
            | Some (desc, _, _, fetchedAtIsNull) ->
                Expect.equal desc "The old description Steam couldn't refresh" "old description survives a Steam fetch failure"
                Expect.isTrue fetchedAtIsNull "description_fetched_at stays NULL -- retried next run"
            | None -> failtest "expected the Steam-error row to still exist"

            let candidatesAfter = MetadataCache.findGamesNeedingDescriptionBackfill conn |> List.map fst |> Set.ofList
            Expect.equal candidatesAfter (Set.ofList [ "steam-error-game-2016" ]) "only the Steam-error row remains a candidate after the run"

        testCase "A blank RAWG API key skips a RAWG-only candidate without an HTTP call -- BackfillResult.Skipped counts it, and it remains a candidate" <| fun _ ->
            let conn = createConnection ()
            seedRawgOnlyGameWithNoCacheRow conn "rawg-only-game-2021" 9002

            let mutable rawgRequests = 0
            let httpClient =
                new HttpClient(
                    new StubHandler(fun req ->
                        if req.RequestUri.ToString().Contains("api.rawg.io") then rawgRequests <- rawgRequests + 1
                        notFoundResponse ()))

            let result = GameDescriptionBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient noRawgConfig |> Async.RunSynchronously

            Expect.equal rawgRequests 0 "no HTTP call was made for the blank-key RAWG candidate"
            Expect.equal result.Skipped 1 "the blank-key RAWG candidate is counted as skipped"
            Expect.equal result.Succeeded 0 "not a success"
            Expect.equal result.Errors 0 "not an error either"

            let candidatesAfter = MetadataCache.findGamesNeedingDescriptionBackfill conn |> List.map fst
            Expect.equal candidatesAfter [ "rawg-only-game-2021" ] "still unstamped -- remains a candidate for a future run with a real key"

        testCase "A Steam Ok response with an empty about_the_game/detailed_description still stamps the row" <| fun _ ->
            let conn = createConnection ()
            seedLegacySteamGame conn "empty-desc-game-2017" 700
                "An old description that will be overwritten by Steam's now-empty one"
                None

            let httpClient =
                new HttpClient(new StubHandler(fun _ -> jsonResponse (storeDetailsJson 700 "" "")))

            let result = GameDescriptionBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient noRawgConfig |> Async.RunSynchronously
            Expect.equal result.Succeeded 1 "an Ok fetch, even with an empty description, is a success"

            match descriptionRow conn "empty-desc-game-2017" with
            | Some (_, _, _, fetchedAtIsNull) ->
                Expect.isFalse fetchedAtIsNull "the row is stamped even though the source genuinely had nothing"
            | None -> failtest "expected the row to still exist"

            let candidatesAfter = MetadataCache.findGamesNeedingDescriptionBackfill conn
            Expect.isEmpty candidatesAfter "a stamped row leaves the candidate set on the next call"
    ]
