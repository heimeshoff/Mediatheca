module Mediatheca.Tests.GameFacetBackfillTests

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server

/// games-a7dqx (ADR-0053): the resumable throttled play-facets backfill
/// job. Same stub-`HttpMessageHandler` idiom `SeriesRefreshCacheTests.fs`
/// uses to avoid a real network call.

type private StubHttpMessageHandler(responseFor: string -> string) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        let json = responseFor (request.RequestUri.ToString())
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(json)
        Task.FromResult<HttpResponseMessage>(response)

/// `appdetails` response for one appId, carrying the given category ids.
/// `l=english` is asserted on the request URL by the caller inspecting
/// `responseFor`'s captured requests where relevant.
let private appdetailsJson (appId: int) (categoryIds: int list) : string =
    let categoriesJson =
        categoryIds
        |> List.map (fun id -> sprintf """{"id":%d,"description":"Cat%d"}""" id id)
        |> String.concat ","
    sprintf """{"%d":{"success":true,"data":{"short_description":"","detailed_description":"","about_the_game":"","categories":[%s]}}}""" appId categoriesJson

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "It Takes Two"
    Year = 2021
    Genres = [ "Co-op" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private seedGameWithSteamAppId (conn: SqliteConnection) (slug: string) (steamAppId: int) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    EventStore.appendToStream conn (Games.streamId slug) 0L
        [ Games.Serialization.toEventData (Games.Game_steam_app_id_set steamAppId) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    MetadataCache.seedFromProjections conn

[<Tests>]
let tests =
    testList "GameFacetBackfill (games-a7dqx, ADR-0053)" [

        testCase "Fetches facets for a seed-only (fetched_at IS NULL) game and stamps fetched_at, dropping it from the next run's cursor" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "it-takes-two-2021" 1426210
            let candidatesBefore = MetadataCache.findGamesNeedingFacetBackfill conn
            Expect.equal candidatesBefore [ ("it-takes-two-2021", 1426210) ] "sanity: the seeded, never-fetched game is the one candidate"

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> appdetailsJson 1426210 [ 1; 9; 38; 39; 24; 44 ]))
            let result = GameFacetBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate processed"
            Expect.equal result.Succeeded 1 "One candidate succeeded"
            Expect.equal result.Errors 0 "No errors"

            let facets =
                conn
                |> Db.newCommand "SELECT facet_solo, facet_coop_couch, facet_coop_online, fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "it-takes-two-2021" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadInt32 "facet_solo", rd.ReadInt32 "facet_coop_couch", rd.ReadInt32 "facet_coop_online",
                    rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            Expect.equal facets (Some (0, 1, 1, false)) "Derived facets written (no solo, couch+online co-op), fetched_at now stamped (not NULL)"

            // Resumability: the WHERE fetched_at IS NULL clause IS the
            // cursor — a successfully-processed row drops out on its own.
            let candidatesAfter = MetadataCache.findGamesNeedingFacetBackfill conn
            Expect.isEmpty candidatesAfter "The processed row no longer appears in the next run's cursor"

        testCase "A game with no Steam app id is never a candidate" <| fun _ ->
            let conn = createConnection ()
            EventStore.appendToStream conn (Games.streamId "no-steam-game") -1L
                [ Games.Serialization.toEventData (Games.Game_added_to_library { sampleGameData with Name = "No Steam Game" }) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            MetadataCache.seedFromProjections conn

            let candidates = MetadataCache.findGamesNeedingFacetBackfill conn
            Expect.isEmpty candidates "No steam_app_id — never a fetchable candidate"

        testCase "A Steam fetch failure leaves fetched_at NULL, so the game is retried on the next run" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "it-takes-two-2021" 1426210
            // success=false — Steam.getSteamStoreDetails maps this to Error.
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> """{"1426210":{"success":false}}"""))
            let result = GameFacetBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate attempted"
            Expect.equal result.Succeeded 0 "Steam returned no data — not a success"

            let candidatesAfter = MetadataCache.findGamesNeedingFacetBackfill conn
            Expect.equal candidatesAfter [ ("it-takes-two-2021", 1426210) ] "fetched_at is still NULL — the game remains a candidate for the next run"

        testCase "The backfill never writes game_detail's facet_override_* columns — only game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "it-takes-two-2021" 1426210
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> appdetailsJson 1426210 [ 2 ]))
            GameFacetBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let overrideStillNull =
                conn
                |> Db.newCommand "SELECT facet_override_solo FROM game_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "it-takes-two-2021" ]
                |> Db.querySingle (fun rd -> rd.IsDBNull(rd.GetOrdinal("facet_override_solo")))
            Expect.equal overrideStillNull (Some true) "facet_override_solo is untouched by the backfill job"
    ]
