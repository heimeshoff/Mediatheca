module Mediatheca.Tests.GameReleaseDateBackfillTests

open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// games-ev65k (ADR-0043/ADR-0045): the resumable throttled release-date
/// backfill job — reuses `GameFacetBackfill`/`GameDeckCompatBackfill`'s
/// shape, walking its OWN `release_date_fetched_at` cursor. Unlike those two
/// backfills, the steady-state candidate query does NOT drop a row forever
/// on first fetch — a still-unreleased game stays a candidate so slipped
/// release dates correct themselves; only a released, successfully-parsed
/// game drains out of the cursor permanently.

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    resp

let private storeDetailsJson (appId: int) (comingSoon: bool) (dateStr: string) =
    sprintf
        """{"%d":{"success":true,"data":{"short_description":"","detailed_description":"","about_the_game":"","categories":[],"release_date":{"coming_soon":%s,"date":"%s"}}}}"""
        appId (if comingSoon then "true" else "false") dateStr

let private httpClientFor (storeDetailsBody: string) : HttpClient =
    new HttpClient(new StubHandler(fun _ -> jsonResponse storeDetailsBody))

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
    testList "GameReleaseDateBackfill (games-ev65k)" [

        testCase "Fetches release date for a never-fetched game and stamps release_date_fetched_at" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "tenebris-somnia-2026" 2121510
            let candidatesBefore = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.equal candidatesBefore [ ("tenebris-somnia-2026", 2121510) ] "sanity: the seeded, never-fetched game is the one candidate"

            let httpClient = httpClientFor (storeDetailsJson 2121510 true "October 2026")
            let result = GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate processed"
            Expect.equal result.Succeeded 1 "One candidate succeeded"
            Expect.equal result.Errors 0 "No errors"

            let row =
                conn
                |> Db.newCommand "SELECT release_date_raw, release_date_parsed, coming_soon FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "tenebris-somnia-2026" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "release_date_raw", rd.ReadString "release_date_parsed", rd.ReadInt32 "coming_soon")
            Expect.equal row (Some ("October 2026", "2026-10-01", 1)) "raw/parsed/coming_soon all written"

        testCase "A game with no Steam app id is never a candidate" <| fun _ ->
            let conn = createConnection ()
            EventStore.appendToStream conn (Games.streamId "no-steam-game") -1L
                [ Games.Serialization.toEventData (Games.Game_added_to_library { sampleGameData with Name = "No Steam Game" }) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            MetadataCache.seedFromProjections conn

            let candidates = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.isEmpty candidates "No steam_app_id — never a fetchable candidate"

        testCase "A released game with a past parsed date and coming_soon=false drains out of the cursor after one fetch" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "old-game-2010" 12345
            let httpClient = httpClientFor (storeDetailsJson 12345 false "25 Oct, 2010")
            GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let candidatesAfter = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.isEmpty candidatesAfter "released, parsed-past-date, not coming_soon — permanently drains out of the candidate cursor"

        testCase "An unreleased game (coming_soon=true) remains a candidate after being fetched — the steady-state re-poll" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "tenebris-somnia-2026" 2121510
            let httpClient = httpClientFor (storeDetailsJson 2121510 true "October 2026")
            GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let candidatesAfter = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.equal candidatesAfter [ ("tenebris-somnia-2026", 2121510) ] "still coming_soon — stays a candidate so a slipped date self-corrects"

        testCase "A future-dated but not-coming_soon game (e.g. a re-release date fixed after coming_soon cleared) also remains a candidate" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "future-game-2099" 999999
            let httpClient = httpClientFor (storeDetailsJson 999999 false "1 Jan, 2099")
            GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let candidatesAfter = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.equal candidatesAfter [ ("future-game-2099", 999999) ] "parsed date still in the future — stays a candidate regardless of the coming_soon flag"

        testCase "A fetch failure leaves release_date_fetched_at NULL, so the game is retried on the next run" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "tenebris-somnia-2026" 2121510
            let httpClient = new HttpClient(new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.NotFound)))
            let result = GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate attempted"
            Expect.equal result.Succeeded 0 "Steam returned nothing usable — not a success"

            let candidatesAfter = MetadataCache.findGamesNeedingReleaseDateBackfill conn
            Expect.equal candidatesAfter [ ("tenebris-somnia-2026", 2121510) ] "release_date_fetched_at is still NULL — remains a candidate"

        testCase "The release-date backfill never touches the play-facets fetched_at or deck_compat_fetched_at cursors" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "tenebris-somnia-2026" 2121510
            let httpClient = httpClientFor (storeDetailsJson 2121510 true "October 2026")
            GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let otherCursorsStillNull =
                conn
                |> Db.newCommand "SELECT fetched_at, deck_compat_fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "tenebris-somnia-2026" ]
                |> Db.querySingle (fun rd ->
                    rd.IsDBNull(rd.GetOrdinal("fetched_at")), rd.IsDBNull(rd.GetOrdinal("deck_compat_fetched_at")))
            Expect.equal otherCursorsStillNull (Some (true, true)) "facets/deck-compat cursors untouched by the release-date backfill"

        testCase "The backfill never writes game_detail — only game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "tenebris-somnia-2026" 2121510
            let httpClient = httpClientFor (storeDetailsJson 2121510 true "October 2026")
            GameReleaseDateBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient |> Async.RunSynchronously |> ignore

            let overrideStillNull =
                conn
                |> Db.newCommand "SELECT facet_override_solo FROM game_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "tenebris-somnia-2026" ]
                |> Db.querySingle (fun rd -> rd.IsDBNull(rd.GetOrdinal("facet_override_solo")))
            Expect.equal overrideStillNull (Some true) "game_detail is untouched by the release-date backfill"
    ]
